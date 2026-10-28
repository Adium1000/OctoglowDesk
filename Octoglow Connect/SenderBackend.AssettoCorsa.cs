using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

// Assetto Corsa: the car's speed, read from the game's shared memory
// (AssettoCorsaTelemetry) and sent as whole km/h to the clock's tile, through
// the same planner as BeamNG.drive's. While the clock doesn't want the tile -
// switched off, or out of the Tile Manager - the game is not read at all; the
// clock is only asked now and then whether that changed. Nothing is written to
// the activity log per reading: only when the speed starts or stops coming in,
// or something is wrong.
public partial class SenderBackend
{
    private static readonly TimeSpan AssettoReadEvery = TimeSpan.FromMilliseconds(100);
    // Between looks while no game is open, or the clock doesn't want the tile.
    private static readonly TimeSpan AssettoIdleEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AssettoClockReadEvery = TimeSpan.FromSeconds(5);
    // A firmware without the tile, rarely, in case it gets updated.
    private static readonly TimeSpan AssettoUnsupportedRecheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AssettoSendRetryAfter = TimeSpan.FromSeconds(1);

    private enum AssettoClockRead { Wanted, NotWanted, Unsupported, Failed }

    private enum AssettoSendResult { Sent, Disabled, Unsupported, Failed }

    private async Task AssettoCorsaLoopAsync(CancellationToken token)
    {
        var planner = new SpeedTilePlanner();
        using var game = new AssettoCorsaTelemetry();
        var wanted = false;
        var nextClockRead = DateTime.MinValue;
        var loggedUnsupported = false;
        var wasDriving = false;
        string? loggedProblem = null;
        var holdUntil = DateTime.MinValue;

        void Unsupported()
        {
            if (!loggedUnsupported)
                _log("[ASSETTO] Firmware-ul ceasului nu are încă tile-ul Assetto Corsa. Actualizează firmware-ul ceasului.");
            loggedUnsupported = true;
            wanted = false;
            nextClockRead = DateTime.UtcNow + AssettoUnsupportedRecheck;
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
                    var read = await ReadAssettoClockAsync(token);
                    switch (read)
                    {
                        case AssettoClockRead.Wanted:
                        case AssettoClockRead.NotWanted:
                            wanted = read == AssettoClockRead.Wanted;
                            loggedUnsupported = false;
                            nextClockRead = now + AssettoClockReadEvery;
                            break;
                        case AssettoClockRead.Unsupported:
                            Unsupported();
                            break;
                        default:
                            // The clock didn't answer: keep what it said last.
                            nextClockRead = now + AssettoClockReadEvery;
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
                    AssettoCorsaStatus.Set(false);
                    await Task.Delay(AssettoIdleEvery, token);
                    continue;
                }

                // 2. What the game is doing.
                var reading = game.Read(now);
                var driving = reading.State == AssettoCorsaState.Driving;
                if (driving) planner.OnSpeed(reading.Kmh, now);
                else planner.Silence();
                if (driving != wasDriving)
                {
                    wasDriving = driving;
                    _log(driving ? "[ASSETTO] Primesc viteza din Assetto Corsa." : "[ASSETTO] Nu mai sosește viteza din Assetto Corsa.");
                }
                if (game.Problem != loggedProblem)
                {
                    loggedProblem = game.Problem;
                    if (loggedProblem is not null) _log($"[ASSETTO WARN] Nu pot citi telemetria Assetto Corsa: {loggedProblem}");
                }
                AssettoCorsaStatus.Set(driving);

                // 3. The clock hears only what the planner lets through.
                if (now >= holdUntil && planner.Next(now, ConfigStore.StopAssettoTileWhenCarStops) is { } send)
                {
                    switch (await SendAssettoAsync(send, token))
                    {
                        case AssettoSendResult.Sent:
                            planner.Sent(send, DateTime.UtcNow);
                            break;
                        case AssettoSendResult.Disabled:
                            // Switched off on the clock, or taken out of the Tile Manager.
                            planner.Forget();
                            wanted = false;
                            nextClockRead = DateTime.UtcNow + AssettoClockReadEvery;
                            break;
                        case AssettoSendResult.Unsupported:
                            planner.Forget();
                            Unsupported();
                            break;
                        default:
                            holdUntil = DateTime.UtcNow + AssettoSendRetryAfter;
                            break;
                    }
                }

                await Task.Delay(game.IsOpen ? AssettoReadEvery : AssettoIdleEvery, token);
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
                    await SendAssettoAsync(SpeedSend.Stop, timeout.Token);
                }
                catch
                {
                    // The clock times the tile out by itself.
                }
            }
            AssettoCorsaStatus.Set(false);
        }
    }

    // GET /assettostate. "wanted" is whether the tile is switched on and in the
    // Tile Manager; a 404 is a firmware from before the tile.
    private async Task<AssettoClockRead> ReadAssettoClockAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/assettostate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return AssettoClockRead.Unsupported;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return AssettoClockRead.Failed;
            }
            if (!response.IsSuccessStatusCode) return AssettoClockRead.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;
            var wanted = root.TryGetProperty("wanted", out var w) ? w.ValueKind == JsonValueKind.True
                : root.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
            return wanted ? AssettoClockRead.Wanted : AssettoClockRead.NotWanted;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return AssettoClockRead.Failed;
        }
    }

    // POST /assettospeed: speed=<whole km/h>, or active=0 to take the tile down.
    // The clock answers DISABLED while it doesn't want the tile, and 404 when
    // its firmware predates it.
    private async Task<AssettoSendResult> SendAssettoAsync(SpeedSend send, CancellationToken token)
    {
        try
        {
            var form = send.Kind == SpeedSendKind.Stop
                ? new Dictionary<string, string> { ["active"] = "0" }
                : new Dictionary<string, string> { ["speed"] = send.Kmh.ToString(CultureInfo.InvariantCulture) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/assettospeed")
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
                return AssettoSendResult.Failed;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return AssettoSendResult.Unsupported;
            if (!response.IsSuccessStatusCode) return AssettoSendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? AssettoSendResult.Disabled : AssettoSendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return AssettoSendResult.Failed;
        }
    }
}
