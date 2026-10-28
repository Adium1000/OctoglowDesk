using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

// Need for Speed Underground 2: the player's car speed, read from SPEED2.EXE's
// memory (Nfsu2Telemetry) and sent to the clock's tile as whole km/h and whole
// mph, through the same planner as BeamNG.drive's and Assetto Corsa's. While
// the clock doesn't want the tile - switched off, or out of the Tile Manager -
// the game is not opened at all; the clock is only asked now and then whether
// that changed. The log hears about each change once: the speed starting or
// stopping, a build that isn't supported, data that fails its checks.
public partial class SenderBackend
{
    private static readonly TimeSpan Nfsu2ReadEvery = TimeSpan.FromMilliseconds(100);
    // Between looks while no game is open, or the clock doesn't want the tile.
    private static readonly TimeSpan Nfsu2IdleEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Nfsu2ClockReadEvery = TimeSpan.FromSeconds(5);
    // A firmware without the tile, rarely, in case it gets updated.
    private static readonly TimeSpan Nfsu2UnsupportedRecheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Nfsu2SendRetryAfter = TimeSpan.FromSeconds(1);

    private enum Nfsu2ClockRead { Wanted, NotWanted, Unsupported, Failed }

    private enum Nfsu2SendResult { Sent, Disabled, Unsupported, Failed }

    // What reads the game; tests put a stand-in for the game here.
    internal static Func<Nfsu2Telemetry> CreateNfsu2Telemetry = () => new Nfsu2Telemetry();

    private async Task Nfsu2LoopAsync(CancellationToken token)
    {
        var planner = new SpeedTilePlanner();
        using var game = CreateNfsu2Telemetry();
        var wanted = false;
        var clockUnsupported = false;
        var nextClockRead = DateTime.MinValue;
        var wasDriving = false;
        Nfsu2State? loggedState = null;
        string? loggedProblem = null;
        var holdUntil = DateTime.MinValue;

        void Unsupported()
        {
            if (!clockUnsupported)
                _log("[NFSU2] Firmware-ul ceasului nu are încă tile-ul NFS Underground 2. Actualizează firmware-ul ceasului.");
            clockUnsupported = true;
            wanted = false;
            nextClockRead = DateTime.UtcNow + Nfsu2UnsupportedRecheck;
        }

        void LogState(Nfsu2Reading reading)
        {
            if (reading.State == loggedState) return;
            // Only what someone would want to know about: which build was
            // refused, and why the game can't be read. Menus and pauses are
            // shown on the Integrations page, not written down.
            if (reading.State == Nfsu2State.UnsupportedVersion)
                _log("[NFSU2 WARN] SPEED2.EXE nu este versiunea acceptată (v1.2 NTSC): viteza nu se citește.");
            else if (reading.State == Nfsu2State.NoAccess)
                _log("[NFSU2 WARN] Jocul rulează ca administrator, iar Octoglow Connect nu: viteza nu se poate citi.");
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
                    switch (await ReadNfsu2ClockAsync(token))
                    {
                        case Nfsu2ClockRead.Wanted:
                            wanted = true;
                            clockUnsupported = false;
                            nextClockRead = now + Nfsu2ClockReadEvery;
                            break;
                        case Nfsu2ClockRead.NotWanted:
                            wanted = false;
                            clockUnsupported = false;
                            nextClockRead = now + Nfsu2ClockReadEvery;
                            break;
                        case Nfsu2ClockRead.Unsupported:
                            Unsupported();
                            break;
                        default:
                            // The clock didn't answer: keep what it said last.
                            nextClockRead = now + Nfsu2ClockReadEvery;
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
                    Nfsu2Status.Set(Nfsu2Snapshot.Off with { ClockUnsupported = clockUnsupported });
                    await Task.Delay(Nfsu2IdleEvery, token);
                    continue;
                }

                // 2. What the game is doing.
                var reading = game.Read(now);
                var driving = reading.State == Nfsu2State.Driving;
                if (driving) planner.OnSpeed(reading.Kmh, now, reading.Mph);
                else planner.Silence();
                if (driving != wasDriving)
                {
                    wasDriving = driving;
                    _log(driving ? "[NFSU2] Primesc viteza din NFS Underground 2." : "[NFSU2] Nu mai sosește viteza din NFS Underground 2.");
                }
                LogState(reading);
                if (game.Problem != loggedProblem)
                {
                    loggedProblem = game.Problem;
                    if (loggedProblem is not null)
                        _log($"[NFSU2 WARN] Datele citite din joc nu trec verificarea ({loggedProblem}); viteza nu se afișează.");
                }
                Nfsu2Status.Set(new Nfsu2Snapshot(true, false, reading, game.Problem));

                // 3. The clock hears only what the planner lets through.
                if (now >= holdUntil && planner.Next(now, ConfigStore.StopNfsu2TileWhenCarStops) is { } send)
                {
                    switch (await SendNfsu2Async(send, token))
                    {
                        case Nfsu2SendResult.Sent:
                            planner.Sent(send, DateTime.UtcNow);
                            break;
                        case Nfsu2SendResult.Disabled:
                            // Switched off on the clock, or taken out of the Tile Manager.
                            planner.Forget();
                            wanted = false;
                            nextClockRead = DateTime.UtcNow + Nfsu2ClockReadEvery;
                            break;
                        case Nfsu2SendResult.Unsupported:
                            planner.Forget();
                            Unsupported();
                            break;
                        default:
                            holdUntil = DateTime.UtcNow + Nfsu2SendRetryAfter;
                            break;
                    }
                }

                await Task.Delay(game.IsOpen ? Nfsu2ReadEvery : Nfsu2IdleEvery, token);
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
                    await SendNfsu2Async(SpeedSend.Stop, timeout.Token);
                }
                catch
                {
                    // The clock times the tile out by itself.
                }
            }
            Nfsu2Status.Set(Nfsu2Snapshot.Off);
        }
    }

    // GET /nfsu2state. "wanted" is whether the tile is switched on and in the
    // Tile Manager; a 404 is a firmware from before the tile.
    private async Task<Nfsu2ClockRead> ReadNfsu2ClockAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/nfsu2state");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return Nfsu2ClockRead.Unsupported;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return Nfsu2ClockRead.Failed;
            }
            if (!response.IsSuccessStatusCode) return Nfsu2ClockRead.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var wanted = json.RootElement.TryGetProperty("wanted", out var w) && w.ValueKind == JsonValueKind.True;
            return wanted ? Nfsu2ClockRead.Wanted : Nfsu2ClockRead.NotWanted;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return Nfsu2ClockRead.Failed;
        }
    }

    // POST /nfsu2speed: speed=<whole km/h>&mph=<whole mph>, or active=0 to take
    // the tile down. The clock answers DISABLED while it doesn't want the tile,
    // and 404 when its firmware predates it.
    private async Task<Nfsu2SendResult> SendNfsu2Async(SpeedSend send, CancellationToken token)
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
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/nfsu2speed")
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
                return Nfsu2SendResult.Failed;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return Nfsu2SendResult.Unsupported;
            if (!response.IsSuccessStatusCode) return Nfsu2SendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? Nfsu2SendResult.Disabled : Nfsu2SendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return Nfsu2SendResult.Failed;
        }
    }
}
