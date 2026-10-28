using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

// Need for Speed Carbon: the player's car speed, read from NFSC.exe's
// memory (NfsCarbonTelemetry) and sent to the clock's tile as whole km/h and
// whole mph, through the same planner as the other speed tiles. While the
// clock doesn't want the tile - switched off, or out of the Tile Manager -
// the game is not opened at all; the clock is only asked now and then whether
// that changed. The log hears about each change once: the speed starting or
// stopping, a build that isn't supported, data that fails its checks.
public partial class SenderBackend
{
    private static readonly TimeSpan NfsCarbonReadEvery = TimeSpan.FromMilliseconds(100);
    // Between looks while no game is open, or the clock doesn't want the tile.
    private static readonly TimeSpan NfsCarbonIdleEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan NfsCarbonClockReadEvery = TimeSpan.FromSeconds(5);
    // A firmware without the tile, rarely, in case it gets updated.
    private static readonly TimeSpan NfsCarbonUnsupportedRecheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NfsCarbonSendRetryAfter = TimeSpan.FromSeconds(1);

    private enum NfsCarbonClockRead { Wanted, NotWanted, Unsupported, Failed }

    private enum NfsCarbonSendResult { Sent, Disabled, Unsupported, Failed }

    // What reads the game; tests put a stand-in for the game here.
    internal static Func<NfsCarbonTelemetry> CreateNfsCarbonTelemetry = () => new NfsCarbonTelemetry();

    private async Task NfsCarbonLoopAsync(CancellationToken token)
    {
        var planner = new SpeedTilePlanner();
        using var game = CreateNfsCarbonTelemetry();
        var wanted = false;
        var clockUnsupported = false;
        var nextClockRead = DateTime.MinValue;
        var wasDriving = false;
        NfsCarbonState? loggedState = null;
        string? loggedProblem = null;
        var holdUntil = DateTime.MinValue;

        void Unsupported()
        {
            if (!clockUnsupported)
                _log("[NFSC] Firmware-ul ceasului nu are încă tile-ul NFS Carbon. Actualizează firmware-ul ceasului.");
            clockUnsupported = true;
            wanted = false;
            nextClockRead = DateTime.UtcNow + NfsCarbonUnsupportedRecheck;
        }

        void LogState(NfsCarbonReading reading)
        {
            if (reading.State == loggedState) return;
            // Only what someone would want to know about: which build was
            // refused, and why the game can't be read. Menus and pauses are
            // shown on the Integrations page, not written down.
            if (reading.State == NfsCarbonState.UnsupportedVersion)
                _log("[NFSC WARN] NFSC.exe nu este versiunea acceptată (v1.4): viteza nu se citește.");
            else if (reading.State == NfsCarbonState.NoAccess)
                _log("[NFSC WARN] Jocul rulează ca administrator, iar Octoglow Connect nu: viteza nu se poate citi.");
            loggedState = reading.State;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // 1. Does the clock want the tile? Asked while the tile isn't up:
                // while it is, the clock's answer to each speed says as much.
                if (now >= nextClockRead && !planner.Showing)
                {
                    switch (await ReadNfsCarbonClockAsync(token))
                    {
                        case NfsCarbonClockRead.Wanted:
                            wanted = true;
                            clockUnsupported = false;
                            nextClockRead = now + NfsCarbonClockReadEvery;
                            break;
                        case NfsCarbonClockRead.NotWanted:
                            wanted = false;
                            clockUnsupported = false;
                            nextClockRead = now + NfsCarbonClockReadEvery;
                            break;
                        case NfsCarbonClockRead.Unsupported:
                            Unsupported();
                            break;
                        default:
                            // The clock didn't answer: keep what it said last.
                            nextClockRead = now + NfsCarbonClockReadEvery;
                            break;
                    }
                }

                if (!wanted)
                {
                    // Nothing on the clock to show it on: the game is left alone.
                    game.Close();
                    planner.Silence();
                    planner.Forget();
                    wasDriving = false;
                    loggedState = null;
                    NfsCarbonStatus.Set(NfsCarbonSnapshot.Off with { ClockUnsupported = clockUnsupported });
                    await Task.Delay(NfsCarbonIdleEvery, token);
                    continue;
                }

                // 2. What the game is doing.
                var reading = game.Read(now);
                var driving = reading.State == NfsCarbonState.Driving;
                if (driving) planner.OnSpeed(reading.Kmh, now, reading.Mph);
                else planner.Silence();
                if (driving != wasDriving)
                {
                    wasDriving = driving;
                    _log(driving ? "[NFSC] Primesc viteza din NFS Carbon." : "[NFSC] Nu mai sosește viteza din NFS Carbon.");
                }
                LogState(reading);
                if (game.Problem != loggedProblem)
                {
                    loggedProblem = game.Problem;
                    if (loggedProblem is not null)
                        _log($"[NFSC WARN] Datele citite din joc nu trec verificarea ({loggedProblem}); viteza nu se afișează.");
                }
                NfsCarbonStatus.Set(new NfsCarbonSnapshot(true, false, reading, game.Problem));

                // 3. The clock hears only what the planner lets through.
                if (now >= holdUntil && planner.Next(now, ConfigStore.StopNfsCarbonTileWhenCarStops) is { } send)
                {
                    switch (await SendNfsCarbonAsync(send, token))
                    {
                        case NfsCarbonSendResult.Sent:
                            planner.Sent(send, DateTime.UtcNow);
                            break;
                        case NfsCarbonSendResult.Disabled:
                            // Switched off on the clock, or taken out of the Tile Manager.
                            planner.Forget();
                            wanted = false;
                            nextClockRead = DateTime.UtcNow + NfsCarbonClockReadEvery;
                            break;
                        case NfsCarbonSendResult.Unsupported:
                            planner.Forget();
                            Unsupported();
                            break;
                        default:
                            holdUntil = DateTime.UtcNow + NfsCarbonSendRetryAfter;
                            break;
                    }
                }

                await Task.Delay(game.IsOpen ? NfsCarbonReadEvery : NfsCarbonIdleEvery, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            // Octoglow Connect is stopping: the tile goes now rather than timing out.
            if (planner.Showing)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await SendNfsCarbonAsync(SpeedSend.Stop, timeout.Token);
                }
                catch
                {
                    // The clock times the tile out by itself.
                }
            }
            NfsCarbonStatus.Set(NfsCarbonSnapshot.Off);
        }
    }

    // GET /nfscstate. "wanted" is whether the tile is switched on and in the
    // Tile Manager; a 404 is a firmware from before the tile.
    private async Task<NfsCarbonClockRead> ReadNfsCarbonClockAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/nfscstate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return NfsCarbonClockRead.Unsupported;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return NfsCarbonClockRead.Failed;
            }
            if (!response.IsSuccessStatusCode) return NfsCarbonClockRead.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var wanted = json.RootElement.TryGetProperty("wanted", out var w) && w.ValueKind == JsonValueKind.True;
            return wanted ? NfsCarbonClockRead.Wanted : NfsCarbonClockRead.NotWanted;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return NfsCarbonClockRead.Failed;
        }
    }

    // POST /nfscspeed: speed=<whole km/h>&mph=<whole mph>, or active=0 to take
    // the tile down. The clock answers DISABLED while it doesn't want the tile,
    // and 404 when its firmware predates it.
    private async Task<NfsCarbonSendResult> SendNfsCarbonAsync(SpeedSend send, CancellationToken token)
    {
        try
        {
            var form = send.Kind == SpeedSendKind.Stop
                ? new Dictionary<string, string> { ["active"] = "0" }
                : new Dictionary<string, string>
                {
                    ["speed"] = send.Kmh.ToString(CultureInfo.InvariantCulture),
                    ["mph"] = Math.Max(send.Mph, 0).ToString(CultureInfo.InvariantCulture),
                };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/nfscspeed")
            {
                Content = new FormUrlEncodedContent(form)
            };
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return NfsCarbonSendResult.Failed;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return NfsCarbonSendResult.Unsupported;
            if (!response.IsSuccessStatusCode) return NfsCarbonSendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? NfsCarbonSendResult.Disabled : NfsCarbonSendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return NfsCarbonSendResult.Failed;
        }
    }
}
