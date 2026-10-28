using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

// Need for Speed Most Wanted (2005): the player's car speed, read from
// speed.exe's memory (NfsMwTelemetry) and sent to the clock's tile as whole
// km/h and whole mph, through the same planner as NFS Underground 2's. While
// the clock doesn't want the tile - switched off, or out of the Tile Manager -
// the game is not opened at all; the clock is only asked now and then whether
// that changed. The log hears about each change once: the speed starting or
// stopping, a build that isn't supported, data that fails its checks.
public partial class SenderBackend
{
    private static readonly TimeSpan NfsMwReadEvery = TimeSpan.FromMilliseconds(100);
    // Between looks while no game is open, or the clock doesn't want the tile.
    private static readonly TimeSpan NfsMwIdleEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan NfsMwClockReadEvery = TimeSpan.FromSeconds(5);
    // A firmware without the tile, rarely, in case it gets updated.
    private static readonly TimeSpan NfsMwUnsupportedRecheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NfsMwSendRetryAfter = TimeSpan.FromSeconds(1);

    private enum NfsMwClockRead { Wanted, NotWanted, Unsupported, Failed }

    private enum NfsMwSendResult { Sent, Disabled, Unsupported, Failed }

    // What reads the game; tests put a stand-in for the game here.
    internal static Func<NfsMwTelemetry> CreateNfsMwTelemetry = () => new NfsMwTelemetry();

    private async Task NfsMwLoopAsync(CancellationToken token)
    {
        var planner = new SpeedTilePlanner();
        using var game = CreateNfsMwTelemetry();
        var wanted = false;
        var clockUnsupported = false;
        var nextClockRead = DateTime.MinValue;
        var wasDriving = false;
        NfsMwState? loggedState = null;
        string? loggedProblem = null;
        var holdUntil = DateTime.MinValue;

        void Unsupported()
        {
            if (!clockUnsupported)
                _log("[NFSMW] Firmware-ul ceasului nu are încă tile-ul NFS Most Wanted. Actualizează firmware-ul ceasului.");
            clockUnsupported = true;
            wanted = false;
            nextClockRead = DateTime.UtcNow + NfsMwUnsupportedRecheck;
        }

        void LogState(NfsMwReading reading)
        {
            if (reading.State == loggedState) return;
            // Only what someone would want to know about: which build was
            // refused, and why the game can't be read. Menus and pauses are
            // shown on the Integrations page, not written down.
            if (reading.State == NfsMwState.UnsupportedVersion)
                _log("[NFSMW WARN] speed.exe nu este versiunea acceptată (v1.3): viteza nu se citește.");
            else if (reading.State == NfsMwState.NoAccess)
                _log("[NFSMW WARN] Jocul rulează ca administrator, iar Octoglow Connect nu: viteza nu se poate citi.");
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
                    switch (await ReadNfsMwClockAsync(token))
                    {
                        case NfsMwClockRead.Wanted:
                            wanted = true;
                            clockUnsupported = false;
                            nextClockRead = now + NfsMwClockReadEvery;
                            break;
                        case NfsMwClockRead.NotWanted:
                            wanted = false;
                            clockUnsupported = false;
                            nextClockRead = now + NfsMwClockReadEvery;
                            break;
                        case NfsMwClockRead.Unsupported:
                            Unsupported();
                            break;
                        default:
                            // The clock didn't answer: keep what it said last.
                            nextClockRead = now + NfsMwClockReadEvery;
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
                    NfsMwStatus.Set(NfsMwSnapshot.Off with { ClockUnsupported = clockUnsupported });
                    await Task.Delay(NfsMwIdleEvery, token);
                    continue;
                }

                // 2. What the game is doing.
                var reading = game.Read(now);
                var driving = reading.State == NfsMwState.Driving;
                if (driving) planner.OnSpeed(reading.Kmh, now, reading.Mph);
                else planner.Silence();
                if (driving != wasDriving)
                {
                    wasDriving = driving;
                    _log(driving ? "[NFSMW] Primesc viteza din NFS Most Wanted." : "[NFSMW] Nu mai sosește viteza din NFS Most Wanted.");
                }
                LogState(reading);
                if (game.Problem != loggedProblem)
                {
                    loggedProblem = game.Problem;
                    if (loggedProblem is not null)
                        _log($"[NFSMW WARN] Datele citite din joc nu trec verificarea ({loggedProblem}); viteza nu se afișează.");
                }
                NfsMwStatus.Set(new NfsMwSnapshot(true, false, reading, game.Problem));

                // 3. The clock hears only what the planner lets through.
                if (now >= holdUntil && planner.Next(now, ConfigStore.StopNfsMwTileWhenCarStops) is { } send)
                {
                    switch (await SendNfsMwAsync(send, token))
                    {
                        case NfsMwSendResult.Sent:
                            planner.Sent(send, DateTime.UtcNow);
                            break;
                        case NfsMwSendResult.Disabled:
                            // Switched off on the clock, or taken out of the Tile Manager.
                            planner.Forget();
                            wanted = false;
                            nextClockRead = DateTime.UtcNow + NfsMwClockReadEvery;
                            break;
                        case NfsMwSendResult.Unsupported:
                            planner.Forget();
                            Unsupported();
                            break;
                        default:
                            holdUntil = DateTime.UtcNow + NfsMwSendRetryAfter;
                            break;
                    }
                }

                await Task.Delay(game.IsOpen ? NfsMwReadEvery : NfsMwIdleEvery, token);
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
                    await SendNfsMwAsync(SpeedSend.Stop, timeout.Token);
                }
                catch
                {
                    // The clock times the tile out by itself.
                }
            }
            NfsMwStatus.Set(NfsMwSnapshot.Off);
        }
    }

    // GET /nfsmwstate. "wanted" is whether the tile is switched on and in the
    // Tile Manager; a 404 is a firmware from before the tile.
    private async Task<NfsMwClockRead> ReadNfsMwClockAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/nfsmwstate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return NfsMwClockRead.Unsupported;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return NfsMwClockRead.Failed;
            }
            if (!response.IsSuccessStatusCode) return NfsMwClockRead.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var wanted = json.RootElement.TryGetProperty("wanted", out var w) && w.ValueKind == JsonValueKind.True;
            return wanted ? NfsMwClockRead.Wanted : NfsMwClockRead.NotWanted;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return NfsMwClockRead.Failed;
        }
    }

    // POST /nfsmwspeed: speed=<whole km/h>&mph=<whole mph>, or active=0 to take
    // the tile down. The clock answers DISABLED while it doesn't want the tile,
    // and 404 when its firmware predates it.
    private async Task<NfsMwSendResult> SendNfsMwAsync(SpeedSend send, CancellationToken token)
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
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/nfsmwspeed")
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
                return NfsMwSendResult.Failed;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return NfsMwSendResult.Unsupported;
            if (!response.IsSuccessStatusCode) return NfsMwSendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? NfsMwSendResult.Disabled : NfsMwSendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return NfsMwSendResult.Failed;
        }
    }
}
