using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

public partial class SenderBackend
{
    // How often OBS is asked, how often the clock hears from us while OBS is
    // busy (the clock drops the tile after about six seconds of silence), and
    // how often the clock's own OBS switch is read.
    private static readonly TimeSpan ObsPollEvery = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ObsKeepaliveEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ObsClockReadEvery = TimeSpan.FromSeconds(2);
    // A firmware without the tile is asked again rarely, in case it gets updated.
    private static readonly TimeSpan ObsClockUnsupportedReadEvery = TimeSpan.FromSeconds(30);
    // After OBS refused the password there is no point asking again soon; a
    // change of password in Integrations reconnects straight away anyway.
    private static readonly TimeSpan ObsAuthRetryEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] ObsBackoff =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
    ];

    private enum ObsClockRead { Enabled, Disabled, Unsupported, Failed }

    private enum ObsProblem { None, Unavailable, Auth }

    // What was last sent to /obsstatus. Elapsed is in milliseconds, as OBS
    // reports it in outputDuration; Second is what the clock shows of it.
    private sealed record ObsSent(bool Active, bool Streaming, bool Recording, long ElapsedMs)
    {
        public long Second => ElapsedMs / 1000;
    }

    private async Task ObsLoopAsync(CancellationToken token)
    {
        ObsWebSocketClient? obs = null;
        var clockEnabled = false;
        var clockShowsElapsed = true;
        var clockUnsupported = false;
        var nextClockRead = DateTime.MinValue;
        var settingsVersion = -1;
        var retryAt = DateTime.MinValue;
        var failures = 0;
        var problem = ObsProblem.None;
        var everConnected = false;
        ObsSent? sent = null;
        var sentAt = DateTime.MinValue;

        async Task DropObsAsync()
        {
            if (obs is null) return;
            await obs.CloseAsync();
            obs = null;
        }

        // Takes the tile off the clock at once rather than letting it time out.
        async Task SendInactiveAsync(CancellationToken sendToken)
        {
            if (sent is not { Active: true }) return;
            var idle = new ObsSent(false, false, false, 0);
            // A clock that has the tile off shows nothing already.
            if (await SendObsStatusAsync(idle, sendToken) != ObsSendResult.Failed)
            {
                sent = idle;
                sentAt = DateTime.UtcNow;
            }
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // 1. Does the clock want OBS at all?
                if (now >= nextClockRead)
                {
                    var (read, showsElapsed) = await ReadObsClockStateAsync(token);
                    switch (read)
                    {
                        case ObsClockRead.Enabled:
                        case ObsClockRead.Disabled:
                            clockEnabled = read == ObsClockRead.Enabled;
                            clockShowsElapsed = showsElapsed;
                            clockUnsupported = false;
                            nextClockRead = now + ObsClockReadEvery;
                            break;
                        case ObsClockRead.Unsupported:
                            if (!clockUnsupported)
                                _log("[OBS] Firmware-ul ceasului nu are încă tile-ul OBS Studio. Actualizează firmware-ul ceasului.");
                            clockUnsupported = true;
                            clockEnabled = false;
                            nextClockRead = now + ObsClockUnsupportedReadEvery;
                            break;
                        default:
                            // The clock didn't answer: keep what it said last.
                            nextClockRead = now + ObsClockReadEvery;
                            break;
                    }
                }

                // 2. New address, port or password, or "Reconnect" was pressed.
                var version = ConfigStore.ObsSettingsVersion;
                if (version != settingsVersion)
                {
                    settingsVersion = version;
                    await DropObsAsync();
                    retryAt = DateTime.MinValue;
                    failures = 0;
                    problem = ObsProblem.None;
                }

                if (!clockEnabled)
                {
                    // Nothing on the clock to show it on: no connection kept open for it.
                    await DropObsAsync();
                    await SendInactiveAsync(token);
                    ObsStudioStatus.Set(clockUnsupported
                        ? ObsSnapshot.Off with { State = ObsConnectionState.ClockUnsupported }
                        : ObsSnapshot.Off);
                    await Task.Delay(500, token);
                    continue;
                }

                // 3. Connect when there is no connection.
                if (obs is null)
                {
                    if (now < retryAt)
                    {
                        await Task.Delay(250, token);
                        continue;
                    }
                    if (problem == ObsProblem.None)
                        ObsStudioStatus.Set(ObsSnapshot.Off with { State = ObsConnectionState.Connecting });
                    try
                    {
                        obs = await ObsWebSocketClient.ConnectAsync(ConfigStore.ObsHost, ConfigStore.ObsPort, ConfigStore.ObsPassword, token);
                        _log(everConnected ? "[OBS] Reconectat la OBS Studio." : "[OBS] Conectat la OBS Studio.");
                        everConnected = true;
                        failures = 0;
                        problem = ObsProblem.None;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (ObsAuthenticationException)
                    {
                        if (problem != ObsProblem.Auth)
                            _log("[OBS] OBS Studio a respins autentificarea. Verifică parola OBS WebSocket din Integrări.");
                        problem = ObsProblem.Auth;
                        retryAt = now + ObsAuthRetryEvery;
                        ObsStudioStatus.Set(ObsSnapshot.Off with { State = ObsConnectionState.AuthFailed });
                    }
                    catch (Exception)
                    {
                        if (problem != ObsProblem.Unavailable)
                            _log("[OBS] OBS Studio nu este disponibil (închis sau cu serverul WebSocket oprit).");
                        problem = ObsProblem.Unavailable;
                        retryAt = now + ObsBackoff[Math.Min(failures++, ObsBackoff.Length - 1)];
                        ObsStudioStatus.Set(ObsSnapshot.Off with { State = ObsConnectionState.Unavailable });
                    }
                    if (obs is null)
                    {
                        await SendInactiveAsync(token);
                        continue;
                    }
                }

                // 4. Ask OBS, one request after the other on the one socket.
                ObsOutputStatus stream, record;
                try
                {
                    stream = await obs.GetStreamStatusAsync(token);
                    record = await obs.GetRecordStatusAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // OBS closed or stopped answering: the tile goes at once.
                    await DropObsAsync();
                    if (problem != ObsProblem.Unavailable)
                        _log("[OBS] Conexiunea cu OBS Studio s-a întrerupt.");
                    problem = ObsProblem.Unavailable;
                    failures = 0;
                    retryAt = DateTime.UtcNow + ObsBackoff[0];
                    ObsStudioStatus.Set(ObsSnapshot.Off with { State = ObsConnectionState.Unavailable });
                    await SendInactiveAsync(token);
                    continue;
                }

                // Streaming outranks recording: with both going, the clock says
                // LIVE and counts the stream's time.
                var streaming = stream.Active;
                var recording = record.Active;
                var active = streaming || recording;
                var elapsed = streaming ? stream.DurationMs : recording ? record.DurationMs : 0;
                ObsStudioStatus.Set(new ObsSnapshot(ObsConnectionState.Connected, streaming, recording,
                    !streaming && recording && record.Paused, elapsed));

                // 5. Tell the clock only what it would show differently, plus a
                // keepalive while the tile is up.
                var next = new ObsSent(active, streaming, recording, elapsed);
                var changed = sent is null || sent.Active != next.Active || sent.Streaming != next.Streaming ||
                              sent.Recording != next.Recording || (active && clockShowsElapsed && sent.Second != next.Second);
                var keepalive = active && DateTime.UtcNow - sentAt >= ObsKeepaliveEvery;
                if (changed || keepalive)
                {
                    switch (await SendObsStatusAsync(next, token))
                    {
                        case ObsSendResult.Sent:
                            sent = next;
                            sentAt = DateTime.UtcNow;
                            break;
                        case ObsSendResult.Disabled:
                            // Switched off on the clock since it was last read: stop now
                            // rather than at the next read.
                            clockEnabled = false;
                            sent = new ObsSent(false, false, false, 0);
                            sentAt = DateTime.UtcNow;
                            nextClockRead = DateTime.UtcNow + ObsClockReadEvery;
                            break;
                    }
                }

                await Task.Delay(ObsPollEvery, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            await DropObsAsync();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendInactiveAsync(timeout.Token);
            }
            catch
            {
                // The clock times the tile out by itself.
            }
            ObsStudioStatus.Set(ObsSnapshot.Off);
        }
    }

    // GET /obsstate. A 404 is a firmware from before the tile.
    private async Task<(ObsClockRead Read, bool ShowsElapsed)> ReadObsClockStateAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/obsstate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return (ObsClockRead.Unsupported, true);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return (ObsClockRead.Failed, true);
            }
            if (!response.IsSuccessStatusCode) return (ObsClockRead.Failed, true);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;
            var enabled = root.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
            var showsElapsed = !root.TryGetProperty("showElapsed", out var s) || s.ValueKind != JsonValueKind.False;
            return (enabled ? ObsClockRead.Enabled : ObsClockRead.Disabled, showsElapsed);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return (ObsClockRead.Failed, true);
        }
    }

    private enum ObsSendResult { Sent, Disabled, Failed }

    // POST /obsstatus: active, streaming, recording, and elapsed in milliseconds.
    // Nothing else about OBS - no scenes, sources or file names - is sent. The
    // clock answers DISABLED while its OBS tile is switched off.
    private async Task<ObsSendResult> SendObsStatusAsync(ObsSent status, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/obsstatus")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["active"] = status.Active ? "1" : "0",
                    ["streaming"] = status.Streaming ? "1" : "0",
                    ["recording"] = status.Recording ? "1" : "0",
                    ["elapsed"] = status.ElapsedMs.ToString(CultureInfo.InvariantCulture),
                })
            };
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return ObsSendResult.Failed;
            }
            if (!response.IsSuccessStatusCode) return ObsSendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? ObsSendResult.Disabled : ObsSendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return ObsSendResult.Failed;
        }
    }
}
