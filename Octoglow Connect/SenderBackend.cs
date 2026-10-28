using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
namespace OctoglowSender;
public partial class SenderBackend : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly Action<string> _log;
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _disposed;
    private readonly ConcurrentDictionary<string, string> _lastSendErrors = new(StringComparer.Ordinal);
    private string? _sessionCookie;
    private Ets2TelemetryService? _ets2;
    private static readonly Dictionary<char, char> DiacriticsMap = new()
    {
        ['ă'] = 'a', ['â'] = 'a', ['Ă'] = 'A', ['Â'] = 'A',
        ['ș'] = 's', ['ş'] = 's', ['Ș'] = 'S', ['Ş'] = 'S',
        ['ț'] = 't', ['ţ'] = 't', ['Ț'] = 'T', ['Ţ'] = 'T',
        ['î'] = 'i', ['Î'] = 'I',
        ['é'] = 'e', ['è'] = 'e', ['ê'] = 'e', ['É'] = 'E',
        ['ñ'] = 'n', ['Ñ'] = 'N', ['ç'] = 'c', ['Ç'] = 'C',
    };
    public SenderBackend(AppConfig cfg, Action<string> log)
    {
        _cfg = cfg;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        // The ESP32 closes every HTTP response, so never try to reuse its socket.
        _http.DefaultRequestHeaders.ConnectionClose = true;
    }
    private string BaseUrl => $"http://{_cfg.Esp32Ip}:{_cfg.Esp32Port}";
    public void Start()
    {
        if (_disposed != 0 || _cts is not null) return;
        ScreenMirror.Reset();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _runTask = Task.Run(() => LoginThenRunAsync(token), token);
    }
    public void Stop()
    {
        var cancellation = Interlocked.Exchange(ref _cts, null);
        if (cancellation is not null)
        {
            cancellation.Cancel();
            var run = _runTask;
            if (run is null) cancellation.Dispose();
            else _ = run.ContinueWith(completed =>
            {
                _ = completed.Exception; // Observe an unexpected integration failure.
                cancellation.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        ScreenMirror.Reset();
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        var run = _runTask;
        if (run is null) _http.Dispose();
        else _ = run.ContinueWith(_ => _http.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private async Task LoginThenRunAsync(CancellationToken token)
    {
        while (!await LoginAsync(token, retries: 5, delaySeconds: 5))
        {
            // A clock that closed its login opens it again when it restarts:
            // keep looking, the tries made meanwhile don't count.
            if (_lastSignIn == ClockSession.Result.Locked && !token.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(60), token); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            if (_lastSignIn == ClockSession.Result.Unreachable) _log("[AUTH] Login eșuat după toate încercările.");
            return;
        }
        var sessionTask = Task.Run(() => SessionRenewLoopAsync(token), token);
        var notifTask = Task.Run(() => RunNotificationListenerAsync(token), token);
        var npTask = Task.Run(() => NowPlayingLoopAsync(token), token);
        var ets2Task = Task.Run(() => Ets2LoopAsync(token), token);
        var screenTask = Task.Run(() => ScreenLoopAsync(token), token);
        var voicemeeterTask = Task.Run(() => VoicemeeterLoopAsync(token), token);
        var browserDownloadsTask = Task.Run(() => BrowserDownloadsLoopAsync(token), token);
        var obsTask = Task.Run(() => ObsLoopAsync(token), token);
        var beamNgTask = Task.Run(() => BeamNgLoopAsync(token), token);
        var assettoTask = Task.Run(() => AssettoCorsaLoopAsync(token), token);
        var nfsu2Task = Task.Run(() => Nfsu2LoopAsync(token), token);
        var nfsmwTask = Task.Run(() => NfsMwLoopAsync(token), token);
        var nfscTask = Task.Run(() => NfsCarbonLoopAsync(token), token);
        var qbitTask = Task.Run(() => QbitLoopAsync(token), token);
        var bambuTask = Task.Run(() => BambuLoopAsync(token), token);
        var afterburnerTask = Task.Run(() => AbLoopAsync(token), token);
        var volumeTask = Task.Run(() => VolumeLoopAsync(token), token);
        var batteryTask = Task.Run(() => BatteryLoopAsync(token), token);
        try { await Task.WhenAll(sessionTask, notifTask, npTask, ets2Task, screenTask, voicemeeterTask, browserDownloadsTask, obsTask, beamNgTask, assettoTask, nfsu2Task, nfsmwTask, nfscTask, qbitTask, bambuTask, afterburnerTask, batteryTask, volumeTask); }
        catch (OperationCanceledException) { /* normal on Stop() */ }
    }
    private string SessionKey => ClockSession.Key(_cfg.Esp32Ip, _cfg.Esp32Port, _cfg.ScUser);
    private ClockSession.Result _lastSignIn = ClockSession.Result.Ok;

    // At the start, and after the clock refused the session (a 401). The saved
    // session is used while the clock takes it; see ClockSession for why the
    // password goes to the clock as seldom as it does.
    private async Task<bool> LoginAsync(CancellationToken token, int retries, double delaySeconds)
    {
        var failed = _sessionCookie;
        for (int attempt = 1; attempt <= retries; attempt++)
        {
            if (token.IsCancellationRequested) return false;
            ClockSession.Result result;
            string? cookie;
            try
            {
                (result, cookie) = await ClockSession.SignInAsync(BaseUrl, SessionKey, _cfg.ScUser, _cfg.ScPass, failed, token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            // Each state is said once, not by every loop that runs into it.
            var changed = result != _lastSignIn;
            _lastSignIn = result;
            switch (result)
            {
                case ClockSession.Result.Ok:
                    if (cookie != _sessionCookie)
                    {
                        _sessionCookie = cookie;
                        // ConnectionStatus reads "Connected" from these words.
                        _log($"[AUTH] Autentificat cu succes ca '{_cfg.ScUser}'.");
                    }
                    return true;
                case ClockSession.Result.Rejected:
                    if (changed) _log("[AUTH] Utilizator sau parolă incorectă. Nu mai încerc până nu le corectezi în Despre, la Date de conectare.");
                    return false;
                case ClockSession.Result.Locked:
                    if (changed) _log("[AUTH] " + ClockSession.LockedMessageRo);
                    return false;
                case ClockSession.Result.NoCredentials:
                    if (changed) _log("[AUTH] Completează utilizatorul și parola în Despre, la Date de conectare.");
                    return false;
            }
            _log($"[AUTH] Nu mă pot conecta la {_cfg.Esp32Ip} (încercarea {attempt}/{retries})");
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    // The clock renews a session near its end when asked /whoami; asked every
    // few hours, one that stays in use never runs out.
    private async Task SessionRenewLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromHours(6), token); }
            catch (OperationCanceledException) { return; }
            try
            {
                var cookie = await ClockSession.RenewAsync(BaseUrl, SessionKey, token);
                if (cookie is not null) _sessionCookie = cookie;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // SCREEN MIRROR
    //
    // The clock streams its panel over UDP (firmware /screensub): the key comes
    // over the logged-in HTTP session, then we keep sending it to the clock from
    // our UDP socket about once a second and the clock pushes every new frame back
    // to that socket. The traffic starting on our side is what lets the frames
    // through Windows Firewall - they arrive as replies.
    //
    // Firmware without the stream (404), or a network that never delivers the UDP
    // frames, falls back to polling /getscreen, and the stream is tried again every
    // minute. A slow answer or a lost frame never clears the picture. UDP only
    // carries changed frames, so a quiet stream is not proof that the clock went
    // away; only repeated HTTP failures are allowed to mark it offline.
    //
    // All of it runs only while a page showing the panel is open
    // (ScreenMirror.IsWatched). With nobody looking the clock is left alone, and
    // its WiFi, kept awake for the stream, goes back to sleep.
    private static readonly TimeSpan ScreenNoUdpAfter = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ScreenResubscribeAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScreenPunchEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ScreenStreamRetryEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ScreenFailureGrace = TimeSpan.FromSeconds(8);
    private const int ScreenFailuresBeforeOffline = 3;
    private DateTime _screenFirstFailureUtc = DateTime.MinValue;
    private int _screenConsecutiveFailures;
    private bool _screenUnsupportedLogged;
    private bool _screenFallbackLogged;

    private enum ScreenStreamEnd { Retry, NoStream, NoUdp }

    private async Task ScreenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!ScreenMirror.IsWatched)
            {
                ScreenMirror.Reset();
                ResetScreenFailures();
                if (!await DelayAsync(TimeSpan.FromMilliseconds(250), token)) break;
                continue;
            }

            ScreenStreamEnd end;
            try
            {
                end = await RunScreenStreamAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }

            if (end == ScreenStreamEnd.Retry)
            {
                if (!await DelayAsync(TimeSpan.FromSeconds(1), token)) break;
                continue;
            }

            if (end == ScreenStreamEnd.NoUdp && !_screenFallbackLogged)
            {
                _screenFallbackLogged = true;
                _log("[SCREEN] Cadrele UDP nu ajung (firewall?) - folosesc HTTP.");
            }

            await RunScreenPollingAsync(ScreenStreamRetryEvery, token);
        }
    }

    private async Task<ScreenStreamEnd> RunScreenStreamAsync(CancellationToken token)
    {
        int port;
        byte[] key;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/screensub");
                if (!string.IsNullOrWhiteSpace(_sessionCookie))
                    request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
                using var response = await _http.SendAsync(request, timeout.Token);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await LoginAsync(token, retries: 3, delaySeconds: 2);
                    return ScreenStreamEnd.Retry;
                }
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return ScreenStreamEnd.NoStream;
                if (!response.IsSuccessStatusCode)
                {
                    MarkScreenMissed();
                    return ScreenStreamEnd.Retry;
                }

                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                MarkScreenReachable();
                if (!TryParseScreenSubscription(body, out port, out key))
                    return ScreenStreamEnd.NoStream;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                MarkScreenMissed();
                return ScreenStreamEnd.Retry;
            }
            catch (HttpRequestException)
            {
                MarkScreenMissed();
                return ScreenStreamEnd.Retry;
            }
        }

        var punch = new byte[8];
        "OGSK"u8.CopyTo(punch);
        key.CopyTo(punch, 4);

        using var udp = new System.Net.Sockets.UdpClient(System.Net.Sockets.AddressFamily.InterNetwork);
        try
        {
            // A connected UDP socket on Windows turns an ICMP "port unreachable"
            // into a reset on the next receive; with this off it is just ignored.
            const int SioUdpConnReset = -1744830452;
            udp.Client.IOControl((System.Net.Sockets.IOControlCode)SioUdpConnReset, new byte[4], null);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or PlatformNotSupportedException)
        {
        }

        try
        {
            udp.Connect(_cfg.Esp32Ip, port);
        }
        catch (System.Net.Sockets.SocketException)
        {
            MarkScreenMissed();
            return ScreenStreamEnd.Retry;
        }

        var subscribedAt = DateTime.UtcNow;
        var lastPunch = DateTime.MinValue;
        var lastPacket = DateTime.MinValue;
        var lastSeq = -1;
        var columns = new byte[32];

        while (!token.IsCancellationRequested)
        {
            if (!ScreenMirror.IsWatched)
                return ScreenStreamEnd.Retry;

            var now = DateTime.UtcNow;
            if (now - lastPunch >= ScreenPunchEvery)
            {
                try { await udp.SendAsync(punch, token); }
                catch (System.Net.Sockets.SocketException) { }
                lastPunch = now;
            }

            if (lastPacket == DateTime.MinValue)
            {
                // Subscribed, yet not one frame: something between here and the
                // clock drops UDP.
                if (now - subscribedAt >= ScreenNoUdpAfter)
                {
                    return ScreenStreamEnd.NoUdp;
                }
            }
            else
            {
                // Long enough that the clock may have restarted with a new key.
                if (now - lastPacket >= ScreenResubscribeAfter)
                    return ScreenStreamEnd.Retry;
            }

            System.Net.Sockets.UdpReceiveResult received;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                wait.CancelAfter(TimeSpan.FromMilliseconds(250));
                try
                {
                    received = await udp.ReceiveAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    continue;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    await Task.Delay(100, token);
                    continue;
                }
            }

            var data = received.Buffer;
            if (data.Length != 40 || data[0] != 'O' || data[1] != 'G' || data[2] != 'F' || data[3] != '1')
                continue;

            var seq = (data[4] << 8) | data[5];
            if (lastSeq >= 0)
            {
                var ahead = (seq - lastSeq) & 0xFFFF;
                // 0 is a duplicate and a small step back a datagram overtaken by a
                // newer one. Anything else is taken - a big jump back is the clock
                // having restarted its count.
                if (ahead == 0 || ahead >= 0xFF00)
                    continue;
            }

            lastSeq = seq;
            lastPacket = DateTime.UtcNow;
            Buffer.BlockCopy(data, 8, columns, 0, 32);
            PublishScreenFrame((data[6] & 1) != 0, Math.Clamp((int)data[7], 0, 16), columns);
        }

        return ScreenStreamEnd.Retry;
    }

    // The fallback: one /getscreen at a time, for as long as `duration`.
    private async Task RunScreenPollingAsync(TimeSpan duration, CancellationToken token)
    {
        var until = DateTime.UtcNow + duration;
        while (!token.IsCancellationRequested && DateTime.UtcNow < until && ScreenMirror.IsWatched)
        {
            var nextDelay = TimeSpan.FromMilliseconds(100);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(2500));

                using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/getscreen");
                if (!string.IsNullOrWhiteSpace(_sessionCookie))
                    request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
                using var response = await _http.SendAsync(request, timeout.Token);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var json = await response.Content.ReadAsStringAsync(timeout.Token);
                    if (TryParseScreenFrame(json, out var on, out var brightness, out var frame))
                        PublishScreenFrame(on, brightness, frame);
                    else
                        MarkScreenMissed();
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    MarkScreenReachable();
                    await LoginAsync(token, retries: 3, delaySeconds: 2);
                    continue;
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    ScreenMirror.SetState(ScreenMirrorState.Unsupported);
                    if (!_screenUnsupportedLogged)
                    {
                        _screenUnsupportedLogged = true;
                        _log("[SCREEN] Firmware-ul ceasului nu are /getscreen.");
                    }
                    nextDelay = TimeSpan.FromSeconds(10);
                }
                else
                {
                    MarkScreenMissed();
                    nextDelay = TimeSpan.FromMilliseconds(500);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                MarkScreenMissed();
                nextDelay = TimeSpan.FromMilliseconds(500);
            }
            catch (HttpRequestException)
            {
                MarkScreenMissed();
                nextDelay = TimeSpan.FromMilliseconds(500);
            }

            if (!await DelayAsync(nextDelay, token)) break;
        }
    }

    private void PublishScreenFrame(bool on, int brightness, byte[] columns)
    {
        ResetScreenFailures();
        ScreenMirror.Publish(on, brightness, columns);
    }

    private void MarkScreenReachable() => ResetScreenFailures();

    private void ResetScreenFailures()
    {
        _screenConsecutiveFailures = 0;
        _screenFirstFailureUtc = DateTime.MinValue;
    }

    // Do not turn one busy response or Wi-Fi hiccup into a visible disconnect.
    // The endpoint must fail repeatedly for a minimum grace period first.
    private void MarkScreenMissed()
    {
        var now = DateTime.UtcNow;
        if (_screenConsecutiveFailures++ == 0)
            _screenFirstFailureUtc = now;

        if (_screenConsecutiveFailures < ScreenFailuresBeforeOffline ||
            now - _screenFirstFailureUtc < ScreenFailureGrace)
        {
            return;
        }

        if (ScreenMirror.State is ScreenMirrorState.Offline or ScreenMirrorState.Unsupported) return;
        ScreenMirror.SetState(ScreenMirrorState.Offline);
        _log("[SCREEN] Ceasul nu răspunde.");
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static bool TryParseScreenSubscription(string json, out int port, out byte[] key)
    {
        port = 0;
        key = [];
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("port", out var portElement) ||
                !portElement.TryGetInt32(out port) ||
                port is < 1 or > 65535)
            {
                return false;
            }

            if (!root.TryGetProperty("key", out var keyElement) ||
                keyElement.GetString() is not { Length: 8 } hex)
            {
                return false;
            }

            key = Convert.FromHexString(hex);
            return true;
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryParseScreenFrame(
        string json,
        out bool on,
        out int brightness,
        out byte[] columns)
    {
        on = false;
        brightness = 0;
        columns = [];

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("on", out var onElement) ||
                (onElement.ValueKind != JsonValueKind.True && onElement.ValueKind != JsonValueKind.False) ||
                !root.TryGetProperty("b", out var brightnessElement) ||
                !brightnessElement.TryGetInt32(out brightness) ||
                !root.TryGetProperty("px", out var pixelsElement))
            {
                return false;
            }

            var pixels = pixelsElement.GetString();
            if (pixels is null || pixels.Length != 64)
                return false;

            columns = Convert.FromHexString(pixels);
            if (columns.Length != 32)
                return false;

            on = onElement.GetBoolean();
            brightness = Math.Clamp(brightness, 0, 16);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> PostAuthenticatedAsync(
        string path, Dictionary<string, string> data, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}")
        {
            Content = new FormUrlEncodedContent(data)
        };
        if (!string.IsNullOrWhiteSpace(_sessionCookie))
            request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
        return await _http.SendAsync(request, token);
    }
    private async Task SendAsync(
        string path, Dictionary<string, string> data, CancellationToken token, string? label = null)
    {
        var said = label ?? string.Join(" ", data.Values);
        try
        {
            using var resp = await PostAuthenticatedAsync(path, data, token);
            if (resp.IsSuccessStatusCode)
            {
                _lastSendErrors.TryRemove(path, out _);
                if (path != "/ets2speed") _log($"[{path.TrimStart('/').ToUpperInvariant()}] {said}");
            }
            else if ((int)resp.StatusCode == 401)
            {
                _log("[AUTH] Sesiune expirată - reautentificare...");
                if (await LoginAsync(token, retries: 3, delaySeconds: 2))
                {
                    using var retryResponse = await PostAuthenticatedAsync(path, data, token);
                    if (retryResponse.IsSuccessStatusCode)
                    {
                        _lastSendErrors.TryRemove(path, out _);
                        if (path != "/ets2speed") _log($"[{path.TrimStart('/').ToUpperInvariant()}] {said}");
                    }
                    else
                        ReportSendError(path, $"[WARN] Status {(int)retryResponse.StatusCode} la {path} după reautentificare");
                }
            }
            else
            {
                ReportSendError(path, $"[WARN] Status {(int)resp.StatusCode} la {path}");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The old backend was stopped; this is not a connection error.
        }
        catch (HttpRequestException)
        {
            ReportSendError(path, $"[ERR] Nu mă pot conecta la {_cfg.Esp32Ip}");
        }
        catch (TaskCanceledException)
        {
            ReportSendError(path, $"[ERR] Timeout la {path}");
        }
    }

    private void ReportSendError(string path, string message)
    {
        if (_lastSendErrors.TryGetValue(path, out var previous) && previous == message) return;
        _lastSendErrors[path] = message;
        _log(message);
    }
    private string Sanitize(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (DiacriticsMap.TryGetValue(ch, out var replacement))
            {
                sb.Append(replacement);
                continue;
            }
            if ((ch >= 0x20 && ch < 0x7F) || ch == '°')
                sb.Append(ch);
        }
        return sb.ToString();
    }
    private string Truncate(string text)
    {
        var max = _cfg.NotifMaxChars;
        if (text.Length <= max) return text;
        return text[..(max - 3)].TrimEnd() + "...";
    }

    private string BuildNotifText(string app, string title, string body)
    {
        var parts = new[] { app, title, body }.Where(p => !string.IsNullOrEmpty(p));
        var text = parts.Any() ? string.Join(" - ", parts) : "(notificare)";
        return Truncate(Sanitize(text));
    }
    private async Task RunNotificationListenerAsync(CancellationToken token)
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var status = await listener.RequestAccessAsync();
            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                _log("[NOTIF] Acces REFUZAT de Windows. Activează în Settings > Notifications > acces la notificări.");
                return;
            }

            _log("[NOTIF] Ascultător activ.");
            var seenIds = new HashSet<uint>();

            var existing = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in existing) seenIds.Add(n.Id);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var notifs = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                    var currentIds = new HashSet<uint>();
                    foreach (var n in notifs)
                    {
                        currentIds.Add(n.Id);
                        if (seenIds.Contains(n.Id)) continue;
                        seenIds.Add(n.Id);

                        var (app, title, body) = ParseNotification(n);
                        if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(body))
                        {
                            var text = BuildNotifText(app, title, body);
                            await SendAsync("/notification", new Dictionary<string, string> { ["text"] = text }, token);
                        }
                    }
                    seenIds.IntersectWith(currentIds);
                }
                catch (Exception e)
                {
                    _log($"[NOTIF ERR] Eroare la interogare: {e.Message}");
                }
                await Task.Delay(350, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _log($"[NOTIF] Ascultătorul s-a oprit: {e.Message}");
        }
    }

    private static (string app, string title, string body) ParseNotification(UserNotification n)
    {
        string app = "", title = "", body = "";
        try
        {
            app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var texts = binding.GetTextElements();
                if (texts.Count > 0) title = texts[0].Text ?? "";
                if (texts.Count > 1) body = texts[1].Text ?? "";
            }
        }
        catch { /* best-effort parsing */ }
        return (app, title, body);
    }
    // ------------------------------------------------------------------ now playing

    // Windows says when the track, the player or the playback state changes, so
    // the clock is told as it happens rather than at the next poll. A change
    // raises several events in a row, and the properties are not always the new
    // ones on the first of them, so the read waits for them to settle.
    private static readonly TimeSpan NowPlayingSettle = TimeSpan.FromMilliseconds(150);
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private readonly List<GlobalSystemMediaTransportControlsSession> _watchedSessions = new();
    private readonly object _mediaGate = new();
    private readonly SemaphoreSlim _mediaChanged = new(0, 1);

    private async Task NowPlayingLoopAsync(CancellationToken token)
    {
        string? lastText = null;
        string? lastKind = null;
        string? lastState = null;
        var lastSend = DateTime.MinValue;
        await WatchMediaSessionsAsync();
        // The clock drops Now Playing 20 seconds after the last update, so the
        // loop also wakes on its own to keep it alive - well inside that, even if
        // the wait drifts - and that same tick is what catches a player that
        // raises no events at all.
        var keepAlive = TimeSpan.FromSeconds(Math.Clamp(_cfg.SendIntervalSeconds / 2.0, 2, 8));

        while (!token.IsCancellationRequested)
        {
            try
            {
                var (text, kind, paused) = await GetNowPlayingAsync();
                var now = DateTime.UtcNow;
                if (text != null)
                {
                    // Held rather than playing: the clock shows its pause icon for
                    // as long as it is told so.
                    var state = paused ? "paused" : "playing";
                    if (text != lastText || kind != lastKind || state != lastState
                        || (now - lastSend).TotalSeconds >= _cfg.SendIntervalSeconds)
                    {
                        var data = new Dictionary<string, string> { ["text"] = text, ["state"] = state };
                        if (!string.IsNullOrWhiteSpace(kind)) data["kind"] = kind;
                        await SendAsync("/nowplaying", data, token,
                            paused ? $"{text} ({(AppStrings.IsEnglish ? "paused" : "pe pauză")})" : text);
                        lastText = text;
                        lastKind = kind;
                        lastState = state;
                        lastSend = now;
                    }
                }
                else
                {
                    // Paused, or the player is gone: the clock is told now, so
                    // the tile drops at once instead of timing out twenty
                    // seconds later. If the message does not get there, that
                    // timeout is still the backstop.
                    if (lastText != null)
                        await SendAsync("/nowplaying", new Dictionary<string, string> { ["state"] = "stopped" }, token, "Playback Stopped");
                    lastText = null;
                    lastKind = null;
                    lastState = null;
                }
            }
            catch (Exception e)
            {
                _log($"[NP WARN] {e.Message}");
            }

            try
            {
                // Whichever comes first: something changed, or the keep-alive is due.
                if (await _mediaChanged.WaitAsync(keepAlive, token))
                    await Task.Delay(NowPlayingSettle, token);
            }
            catch (OperationCanceledException) { break; }
        }

        UnwatchMediaSessions();
    }

    // One pending wake-up is all the loop needs, however many events arrive.
    private void NoteMediaChanged()
    {
        try
        {
            if (_mediaChanged.CurrentCount == 0) _mediaChanged.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task WatchMediaSessionsAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.SessionsChanged += OnMediaSessionsChanged;
            manager.CurrentSessionChanged += OnMediaCurrentSessionChanged;
            _mediaManager = manager;
            WatchSessions(manager);
        }
        catch (Exception error)
        {
            // Without the events it still works, just on the keep-alive tick.
            _log($"[NP WARN] {error.Message}");
        }
    }

    private void OnMediaSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        ReWatchSessions(sender);

    private void OnMediaCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        ReWatchSessions(sender);

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        NoteMediaChanged();

    private void OnMediaPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        NoteMediaChanged();

    // A player that opens or closes brings its own session with it, so the list
    // of what is being listened to is made again from scratch.
    private void ReWatchSessions(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        WatchSessions(manager);
        NoteMediaChanged();
    }

    private void WatchSessions(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        lock (_mediaGate)
        {
            DropWatchedSessions();
            try
            {
                foreach (var session in manager.GetSessions())
                {
                    session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    session.PlaybackInfoChanged += OnMediaPlaybackInfoChanged;
                    _watchedSessions.Add(session);
                }
            }
            catch (Exception error)
            {
                _log($"[NP WARN] {error.Message}");
            }
        }
    }

    private void UnwatchMediaSessions()
    {
        lock (_mediaGate)
        {
            DropWatchedSessions();
            if (_mediaManager is not { } manager) return;
            _mediaManager = null;
            try
            {
                manager.SessionsChanged -= OnMediaSessionsChanged;
                manager.CurrentSessionChanged -= OnMediaCurrentSessionChanged;
            }
            catch (Exception)
            {
                // Nothing to do about a manager Windows has already taken away.
            }
        }
    }

    private void DropWatchedSessions()
    {
        foreach (var session in _watchedSessions)
        {
            try
            {
                session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                session.PlaybackInfoChanged -= OnMediaPlaybackInfoChanged;
            }
            catch (Exception)
            {
                // A player that closed took its session, and its handlers, with it.
            }
        }
        _watchedSessions.Clear();
    }
    private async Task Ets2LoopAsync(CancellationToken token)
    {
        _log("[ETS2] Modul detecție pornit, aștept eurotrucks2.exe...");
        _ets2 = new Ets2TelemetryService(_log);
        var wasRunning = false;
        int? lastSpeedSent = null;
        var lastSpeedSentAt = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            var running = _ets2.IsEts2RunningLogged();
            if (running != wasRunning)
            {
                _log(running ? "[ETS2] eurotrucks2.exe detectat." : "[ETS2] eurotrucks2.exe închis - deconectat.");
                wasRunning = running;
                lastSpeedSent = null;
            }
            if (running)
            {
                var speed = _ets2.GetSpeedKmh();
                var shouldRefreshPriorityTile = !ConfigStore.StopPriorityTileWhenTruckStops
                    && DateTime.UtcNow - lastSpeedSentAt >= TimeSpan.FromSeconds(2);
                if (speed.HasValue && (speed != lastSpeedSent || shouldRefreshPriorityTile))
                {
                    await SendAsync("/ets2speed", new Dictionary<string, string> { ["speed"] = speed.Value.ToString() }, token);
                    lastSpeedSent = speed;
                    lastSpeedSentAt = DateTime.UtcNow;
                }
            }
            try { await Task.Delay(running ? 500 : 2000, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<(string? text, string kind, bool paused)> GetNowPlayingAsync()
    {
        // Asked for once and kept: requesting the manager on every read was the
        // expensive half of this, and it is also what carries the events.
        var mgr = _mediaManager ?? await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        // GetCurrentSession can point to an inactive music player while a browser or video
        // player is actually running. Prefer a session that is actively playing.
        var session = mgr.GetSessions()
            .FirstOrDefault(candidate => candidate.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? mgr.GetCurrentSession();
        if (session is null) return (null, "", false);
        var paused = session.GetPlaybackInfo().PlaybackStatus !=
                     GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (!ConfigStore.KeepNowPlayingWhenPaused && paused) return (null, "", false);
        var props = await session.TryGetMediaPropertiesAsync();
        if (props is null) return (null, "", false);
        var title = (props.Title ?? "").Trim();
        var artist = (props.Artist ?? "").Trim();
        if (string.IsNullOrEmpty(title)) return (null, "", false);

        var text = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
        return (text, ClassifyNowPlayingSource(session.SourceAppUserModelId), paused);
    }
    private string ClassifyNowPlayingSource(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return "";
        var source = sourceApp.ToLowerInvariant();

        foreach (var player in ConfigStore.VideoPlayers)
            if (player.Enabled && !string.IsNullOrWhiteSpace(player.Match) && source.Contains(player.Match.ToLowerInvariant()))
                return "video";
        foreach (var player in ConfigStore.MusicPlayers)
            if (player.Enabled && !string.IsNullOrWhiteSpace(player.Match) && source.Contains(player.Match.ToLowerInvariant()))
                return "music";

        return "";
    }
}
