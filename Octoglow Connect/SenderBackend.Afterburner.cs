using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

public partial class SenderBackend
{
    // How often the shared memory is read (Afterburner polls once a second,
    // RTSS measures over about a second), how often the clock hears from us
    // while there is something to show (it drops the values after six seconds
    // of silence), and how often the clock's tile switch is read.
    private static readonly TimeSpan AbReadEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AbKeepaliveEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AbClockReadEvery = TimeSpan.FromSeconds(3);
    // A firmware without the tile is asked again rarely, in case it gets updated.
    private static readonly TimeSpan AbClockUnsupportedReadEvery = TimeSpan.FromSeconds(30);

    private enum AbClockRead { Wanted, NotWanted, Unsupported, Failed }

    private enum AbSendResult { Sent, Disabled, Failed }

    // What goes to /abstatus: whole frames per second and tenths of a degree
    // Celsius, each null while it is unavailable, and whether the frame rate
    // is a game's (for the Game FPS tile).
    private sealed record AbSent(int? Fps, int? CpuDeci, int? GpuDeci, bool Game = false)
    {
        public bool Any => Fps is not null || CpuDeci is not null || GpuDeci is not null;
        public static readonly AbSent None = new(null, null, null);
    }

    private async Task AbLoopAsync(CancellationToken token)
    {
        var clockWanted = false;
        var clockUnsupported = false;
        var nextClockRead = DateTime.MinValue;
        AbSent? sent = null;
        var sentAt = DateTime.MinValue;
        // What was last written about each program: it is written again only
        // when that changes, never for each reading.
        bool? loggedAfterburner = null;
        bool? loggedRtss = null;

        void LogSources(AbReading reading)
        {
            if (loggedAfterburner != reading.AfterburnerLive)
            {
                _log(reading.AfterburnerLive
                    ? "[AFTERBURNER] Temperaturile se citesc din MSI Afterburner."
                    : "[AFTERBURNER] MSI Afterburner nu rulează sau nu publică date: temperaturile nu sunt disponibile.");
                loggedAfterburner = reading.AfterburnerLive;
            }
            if (loggedRtss != reading.RtssLive)
            {
                _log(reading.RtssLive
                    ? "[AFTERBURNER] FPS-ul se citește din RivaTuner Statistics Server."
                    : "[AFTERBURNER] RivaTuner Statistics Server nu rulează: FPS-ul nu este disponibil.");
                loggedRtss = reading.RtssLive;
            }
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // 1. Does the clock want the tile at all?
                if (now >= nextClockRead)
                {
                    var read = await ReadAbClockAsync(token);
                    switch (read)
                    {
                        case AbClockRead.Wanted:
                        case AbClockRead.NotWanted:
                            clockWanted = read == AbClockRead.Wanted;
                            clockUnsupported = false;
                            nextClockRead = now + AbClockReadEvery;
                            break;
                        case AbClockRead.Unsupported:
                            if (!clockUnsupported)
                                _log("[AFTERBURNER] Firmware-ul ceasului nu are încă tile-ul MSI Afterburner. Actualizează firmware-ul ceasului.");
                            clockUnsupported = true;
                            clockWanted = false;
                            nextClockRead = now + AbClockUnsupportedReadEvery;
                            break;
                        default:
                            // The clock didn't answer: keep what it said last.
                            nextClockRead = now + AbClockReadEvery;
                            break;
                    }
                }

                if (!clockWanted)
                {
                    // Switched off or out of the Tile Manager: nothing is read.
                    // The clock forgot the values when it answered DISABLED.
                    sent = null;
                    loggedAfterburner = null;
                    loggedRtss = null;
                    AfterburnerStatus.Set(clockUnsupported
                        ? new AbSnapshot(AbState.ClockUnsupported, AbReading.Nothing)
                        : AbSnapshot.Off);
                    await Task.Delay(500, token);
                    continue;
                }

                // 2. Read both programs' shared memory.
                var reading = AfterburnerReader.Read(ConfigStore.AfterburnerGpu);
                AfterburnerStatus.Set(new AbSnapshot(AbState.Reading, reading));
                LogSources(reading);

                // 3. Tell the clock when something changed - an empty post takes a
                // value off at once - and keep it fresh while there is something.
                var next = new AbSent(reading.Fps, Deci(reading.CpuCelsius), Deci(reading.GpuCelsius), reading.FpsIsGame);
                var changed = sent is null || sent != next;
                var keepalive = next.Any && DateTime.UtcNow - sentAt >= AbKeepaliveEvery;
                if (changed || keepalive)
                {
                    switch (await SendAbStatusAsync(next, token))
                    {
                        case AbSendResult.Sent:
                            sent = next;
                            sentAt = DateTime.UtcNow;
                            break;
                        case AbSendResult.Disabled:
                            // Switched off on the clock since it was last read.
                            clockWanted = false;
                            sent = null;
                            nextClockRead = DateTime.UtcNow + AbClockReadEvery;
                            continue;
                    }
                }

                await Task.Delay(AbReadEvery, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            // Closing Octoglow Connect takes the tile off at once rather than
            // leaving the last reading up until the clock times it out.
            if (sent is { Any: true })
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await SendAbStatusAsync(AbSent.None, timeout.Token);
                }
                catch
                {
                    // The clock times the values out by itself.
                }
            }
            AfterburnerStatus.Set(AbSnapshot.Off);
        }
    }

    private static int? Deci(double? celsius) =>
        celsius is { } c ? (int)Math.Round(c * 10, MidpointRounding.AwayFromZero) : null;

    // GET /abstate. "wanted" is whether the MSI Afterburner tile or the Game
    // FPS tile is in the Tile Manager and switched on; a 404 is a firmware from
    // before the tile.
    private async Task<AbClockRead> ReadAbClockAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/abstate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return AbClockRead.Unsupported;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return AbClockRead.Failed;
            }
            if (!response.IsSuccessStatusCode) return AbClockRead.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var wanted = json.RootElement.TryGetProperty("wanted", out var w) && w.ValueKind == JsonValueKind.True;
            return wanted ? AbClockRead.Wanted : AbClockRead.NotWanted;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return AbClockRead.Failed;
        }
    }

    // POST /abstatus: the numbers and nothing else - no application, GPU or
    // program names; game=1 says only that the frame rate is a game's. A value
    // left out is unavailable. The clock answers DISABLED while it wants
    // neither tile.
    private async Task<AbSendResult> SendAbStatusAsync(AbSent status, CancellationToken token)
    {
        try
        {
            var form = new Dictionary<string, string>();
            if (status.Fps is { } fps) form["fps"] = fps.ToString(CultureInfo.InvariantCulture);
            if (status.Fps is not null && status.Game) form["game"] = "1";
            if (status.CpuDeci is { } cpu) form["cpu"] = cpu.ToString(CultureInfo.InvariantCulture);
            if (status.GpuDeci is { } gpu) form["gpu"] = gpu.ToString(CultureInfo.InvariantCulture);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/abstatus")
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
                return AbSendResult.Failed;
            }
            if (!response.IsSuccessStatusCode) return AbSendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? AbSendResult.Disabled : AbSendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return AbSendResult.Failed;
        }
    }
}
