using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OctoglowSender;

public readonly record struct BrowserDownloadsSnapshot(
    bool BrowserConnected,
    bool Active,
    int Progress,
    int ActiveCount,
    long Version,
    DateTime LastUpdateUtc);

// Receives aggregate download progress from the bundled Chromium extension.
// It is deliberately loopback-only: no other computer on the LAN can submit data.
public static class BrowserDownloadsBridge
{
    public const int Port = 38742;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Source> Sources = new(StringComparer.Ordinal);
    private static CancellationTokenSource? _cancellation;
    private static TcpListener? _listener;
    private static long _version;

    public static string ExtensionFolder => Path.Combine(AppContext.BaseDirectory, "BrowserExtension");

    public static void Start()
    {
        lock (Gate)
        {
            if (_cancellation is not null) return;
            _cancellation = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            try
            {
                _listener.Start(8);
                _ = AcceptLoopAsync(_listener, _cancellation.Token);
                AppLog.Write($"[BROWSER DOWNLOADS] Serviciul local asculta pe 127.0.0.1:{Port}.");
            }
            catch (Exception error) when (error is SocketException or InvalidOperationException)
            {
                AppLog.Write($"[BROWSER DOWNLOADS] Serviciul local nu a pornit: {error.Message}");
                _listener = null;
                _cancellation.Dispose();
                _cancellation = null;
            }
        }
    }

    public static void Stop()
    {
        CancellationTokenSource? cancellation;
        TcpListener? listener;
        lock (Gate)
        {
            cancellation = _cancellation;
            listener = _listener;
            _cancellation = null;
            _listener = null;
            Sources.Clear();
            _version++;
        }
        cancellation?.Cancel();
        listener?.Stop();
        cancellation?.Dispose();
    }

    public static BrowserDownloadsSnapshot Snapshot()
    {
        lock (Gate)
        {
            RemoveExpiredSources(DateTime.UtcNow);
            if (Sources.Count == 0)
                return new BrowserDownloadsSnapshot(false, false, 0, 0, _version, DateTime.MinValue);

            var active = Sources.Values.Where(source => source.ActiveCount > 0).ToArray();
            var count = active.Sum(source => source.ActiveCount);
            var progress = active.Length == 0 ? 0 : (int)Math.Round(active.Average(source => source.Progress));
            var last = Sources.Values.Max(source => source.LastSeenUtc);
            return new BrowserDownloadsSnapshot(true, count > 0, Math.Clamp(progress, 0, 100), count, _version, last);
        }
    }

    private static async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested)
        {
        }
        catch (SocketException) when (token.IsCancellationRequested)
        {
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 3000;
                client.SendTimeout = 3000;
                using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, token);
                if (request is null)
                {
                    await WriteResponseAsync(stream, 400, "Bad Request", token);
                    return;
                }

                if (request.Method == "OPTIONS")
                {
                    await WriteResponseAsync(stream, 204, "", token);
                    return;
                }
                if (request.Method != "POST" || request.Path != "/v1/downloads")
                {
                    await WriteResponseAsync(stream, 404, "Not Found", token);
                    return;
                }

                using var json = JsonDocument.Parse(request.Body);
                var root = json.RootElement;
                var clientId = root.TryGetProperty("clientId", out var id) ? id.GetString() : null;
                if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 80)
                {
                    await WriteResponseAsync(stream, 400, "Invalid client", token);
                    return;
                }
                var count = root.TryGetProperty("activeCount", out var countElement) && countElement.TryGetInt32(out var parsedCount)
                    ? Math.Clamp(parsedCount, 0, 1000) : 0;
                var progress = root.TryGetProperty("progress", out var progressElement) && progressElement.TryGetDouble(out var parsedProgress)
                    ? Math.Clamp((int)Math.Round(parsedProgress), 0, 100) : 0;
                lock (Gate)
                {
                    Sources[clientId] = new Source(count, progress, DateTime.UtcNow);
                    _version++;
                }
                await WriteResponseAsync(stream, 200, "OK", token);
            }
            catch (Exception error) when (error is IOException or SocketException or JsonException or OperationCanceledException)
            {
                // A browser can close a short-lived localhost request at any point.
            }
        }
    }

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        const int maxHeader = 16 * 1024;
        const int maxBody = 16 * 1024;
        var data = new List<byte>(2048);
        var buffer = new byte[2048];
        var headerEnd = -1;
        while (headerEnd < 0 && data.Count < maxHeader)
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read == 0) return null;
            data.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = HeaderEnd(data);
        }
        if (headerEnd < 0) return null;

        var header = Encoding.ASCII.GetString(data.Take(headerEnd).ToArray());
        var lines = header.Split("\r\n", StringSplitOptions.None);
        var first = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (first.Length < 2) return null;
        var length = 0;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            if (line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line[(colon + 1)..].Trim(), out length);
        }
        if (length is < 0 or > maxBody) return null;
        var bodyStart = headerEnd + 4;
        while (data.Count - bodyStart < length)
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read == 0) return null;
            data.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        return new Request(first[0].ToUpperInvariant(), first[1], Encoding.UTF8.GetString(data.Skip(bodyStart).Take(length).ToArray()));
    }

    private static int HeaderEnd(List<byte> data)
    {
        for (var i = 0; i + 3 < data.Count; i++)
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) return i;
        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 404 => "Not Found", _ => "Error" };
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {payload.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, token);
        if (payload.Length > 0) await stream.WriteAsync(payload, token);
    }

    private static void RemoveExpiredSources(DateTime now)
    {
        var expired = Sources.Where(pair => now - pair.Value.LastSeenUtc > TimeSpan.FromSeconds(75)).Select(pair => pair.Key).ToArray();
        foreach (var key in expired) Sources.Remove(key);
        if (expired.Length > 0) _version++;
    }

    private sealed record Source(int ActiveCount, int Progress, DateTime LastSeenUtc);
    private sealed record Request(string Method, string Path, string Body);
}
