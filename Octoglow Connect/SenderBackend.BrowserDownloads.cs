using System.Net;

namespace OctoglowSender;

public partial class SenderBackend
{
    private async Task BrowserDownloadsLoopAsync(CancellationToken token)
    {
        var enabled = false;
        var unsupportedLogged = false;
        var lastVersion = -1L;
        var lastSent = DateTime.MinValue;
        var sentActive = false;
        var nextStateRead = DateTime.MinValue;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                if (now >= nextStateRead)
                {
                    var state = await ReadBrowserDownloadsStateAsync(token);
                    if (state.HasValue)
                    {
                        enabled = state.Value;
                        unsupportedLogged = false;
                    }
                    else if (!unsupportedLogged)
                    {
                        unsupportedLogged = true;
                        _log("[BROWSER DOWNLOADS] Firmware-ul ceasului nu ofera inca tile-ul Browser Downloads.");
                    }
                    nextStateRead = now + TimeSpan.FromSeconds(2);
                }

                var snapshot = BrowserDownloadsBridge.Snapshot();
                var shouldSend = enabled &&
                    (snapshot.Version != lastVersion || (snapshot.Active && now - lastSent >= TimeSpan.FromSeconds(2)) || (sentActive && !snapshot.Active));
                if (shouldSend)
                {
                    var sent = await SendBrowserDownloadsAsync(snapshot, token);
                    if (sent)
                    {
                        lastVersion = snapshot.Version;
                        lastSent = now;
                        sentActive = snapshot.Active;
                    }
                }
                else if (!enabled)
                {
                    sentActive = false;
                }

                await Task.Delay(250, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (sentActive)
            {
                try { await SendBrowserDownloadsAsync(new BrowserDownloadsSnapshot(true, false, 0, 0, 0, DateTime.UtcNow), CancellationToken.None); }
                catch { }
            }
        }
    }

    private async Task<bool?> ReadBrowserDownloadsStateAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/browserdownloadstate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return json.RootElement.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<bool> SendBrowserDownloadsAsync(BrowserDownloadsSnapshot snapshot, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/browserdownload")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["active"] = snapshot.Active ? "1" : "0",
                    ["progress"] = snapshot.Progress.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["count"] = snapshot.ActiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                })
            };
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return false;
            }
            return response.IsSuccessStatusCode;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }
}
