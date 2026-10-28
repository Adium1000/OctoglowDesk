using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OctoglowSender;

// OBS STUDIO
//
// Octoglow Connect talks to the WebSocket server OBS Studio 28+ ships with
// (obs-websocket 5.x, ws://127.0.0.1:4455 by default) and asks it, one request
// at a time, whether it is streaming or recording and for how long. Nothing
// else is read: no scenes, no sources, no file names.

public enum ObsConnectionState
{
    /// <summary>The backend isn't running, or the OBS tile is off on the clock.</summary>
    Off,
    /// <summary>The clock's firmware has no OBS tile.</summary>
    ClockUnsupported,
    Connecting,
    Connected,
    /// <summary>OBS isn't running, or its WebSocket server is off.</summary>
    Unavailable,
    /// <summary>OBS refused the password (or wants one and none is set).</summary>
    AuthFailed,
}

public sealed record ObsSnapshot(ObsConnectionState State, bool Streaming, bool Recording, bool Paused, long ElapsedMs)
{
    public static readonly ObsSnapshot Off = new(ObsConnectionState.Off, false, false, false, 0);
    public bool Active => State == ObsConnectionState.Connected && (Streaming || Recording);
}

/// <summary>What the OBS loop last saw, for the pages that show it. Only the backend writes it.</summary>
public static class ObsStudioStatus
{
    private static readonly object Gate = new();
    private static ObsSnapshot _current = ObsSnapshot.Off;

    /// <summary>Raised on the backend's thread whenever the snapshot changes.</summary>
    public static event Action? Changed;

    public static ObsSnapshot Current
    {
        get
        {
            lock (Gate) return _current;
        }
    }

    public static void Set(ObsSnapshot next)
    {
        lock (Gate)
        {
            if (_current == next) return;
            _current = next;
        }
        Changed?.Invoke();
    }

    /// <summary>"REC 5:42", "LIVE 1:35:05": minutes and seconds below the hour, hours, minutes and seconds from it on.</summary>
    public static string FormatElapsed(long milliseconds)
    {
        var total = Math.Max(0, milliseconds) / 1000;
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var seconds = total % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
    }
}

internal sealed class ObsAuthenticationException : Exception
{
    public ObsAuthenticationException(string message) : base(message) { }
}

internal sealed record ObsOutputStatus(bool Active, bool Paused, long DurationMs);

/// <summary>One connection to obs-websocket 5.x. Requests go out one at a time and wait for their own answer.</summary>
internal sealed class ObsWebSocketClient : IDisposable
{
    private const int RpcVersion = 1;
    // WebSocketCloseCode.AuthenticationFailed in obs-websocket 5.
    private const int CloseAuthenticationFailed = 4009;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    private readonly ClientWebSocket _socket = new();
    private readonly byte[] _buffer = new byte[8192];
    private int _nextRequestId;
    private bool _disposed;

    private ObsWebSocketClient()
    {
        _socket.Options.AddSubProtocol("obswebsocket.json");
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    }

    /// <summary>Connects and completes Hello / Identify / Identified. Throws ObsAuthenticationException when OBS refuses the password.</summary>
    public static async Task<ObsWebSocketClient> ConnectAsync(string host, int port, string password, CancellationToken token)
    {
        var client = new ObsWebSocketClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(HandshakeTimeout);
            await client._socket.ConnectAsync(new UriBuilder("ws", host, port).Uri, timeout.Token);

            using var hello = await client.ReceiveAsync(timeout.Token);
            if (OpCode(hello.RootElement) != 0) throw new WebSocketException("OBS did not send Hello.");
            var helloData = hello.RootElement.GetProperty("d");

            string? authentication = null;
            if (helloData.TryGetProperty("authentication", out var challenge) && challenge.ValueKind == JsonValueKind.Object)
            {
                if (string.IsNullOrEmpty(password))
                    throw new ObsAuthenticationException("OBS asks for a password and none is set.");
                authentication = Authentication(password,
                    challenge.GetProperty("salt").GetString() ?? "",
                    challenge.GetProperty("challenge").GetString() ?? "");
            }

            // No events: everything the tile needs is asked for.
            var identifyData = new JsonObject { ["rpcVersion"] = RpcVersion };
            if (authentication is not null) identifyData["authentication"] = authentication;
            identifyData["eventSubscriptions"] = 0;
            await client.SendAsync(new JsonObject { ["op"] = 1, ["d"] = identifyData }, timeout.Token);

            using var identified = await client.ReceiveAsync(timeout.Token);
            if (OpCode(identified.RootElement) != 2) throw new WebSocketException("OBS did not confirm Identify.");
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // secret = Base64(SHA256(password + salt)); authentication = Base64(SHA256(secret + challenge))
    private static string Authentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    public Task<ObsOutputStatus> GetStreamStatusAsync(CancellationToken token) => GetOutputStatusAsync("GetStreamStatus", token);

    public Task<ObsOutputStatus> GetRecordStatusAsync(CancellationToken token) => GetOutputStatusAsync("GetRecordStatus", token);

    private async Task<ObsOutputStatus> GetOutputStatusAsync(string requestType, CancellationToken token)
    {
        using var response = await RequestAsync(requestType, token);
        var d = response.RootElement.GetProperty("d");
        if (!d.TryGetProperty("requestStatus", out var status) || !status.TryGetProperty("result", out var ok) || !ok.GetBoolean())
            throw new WebSocketException($"OBS refused {requestType}.");
        if (!d.TryGetProperty("responseData", out var data) || data.ValueKind != JsonValueKind.Object)
            return new ObsOutputStatus(false, false, 0);

        var active = data.TryGetProperty("outputActive", out var a) && a.ValueKind == JsonValueKind.True;
        var paused = data.TryGetProperty("outputPaused", out var p) && p.ValueKind == JsonValueKind.True;
        long duration = 0;
        if (data.TryGetProperty("outputDuration", out var ms) && ms.ValueKind == JsonValueKind.Number)
            duration = ms.TryGetInt64(out var whole) ? whole : (long)ms.GetDouble();
        else if (data.TryGetProperty("outputTimecode", out var timecode))
            duration = ParseTimecode(timecode.GetString());
        return new ObsOutputStatus(active, paused, Math.Max(0, duration));
    }

    // "HH:MM:SS.mmm", only for a server that leaves outputDuration out.
    private static long ParseTimecode(string? value)
    {
        return TimeSpan.TryParseExact(value, @"hh\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture, out var span)
            ? (long)span.TotalMilliseconds
            : 0;
    }

    private async Task<JsonDocument> RequestAsync(string requestType, CancellationToken token)
    {
        var id = (++_nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(RequestTimeout);
        await SendAsync(new JsonObject { ["op"] = 6, ["d"] = new JsonObject { ["requestType"] = requestType, ["requestId"] = id } }, timeout.Token);
        while (true)
        {
            var message = await ReceiveAsync(timeout.Token);
            var root = message.RootElement;
            if (OpCode(root) == 7 &&
                root.GetProperty("d").TryGetProperty("requestId", out var answered) &&
                answered.GetString() == id)
                return message;
            message.Dispose();
        }
    }

    // Built as JSON nodes, which need no reflection to write - anonymous objects
    // do, and a Native AOT build has none.
    private Task SendAsync(JsonObject message, CancellationToken token) =>
        _socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message.ToJsonString())), WebSocketMessageType.Text, true, token);

    private async Task<JsonDocument> ReceiveAsync(CancellationToken token)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(_buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if ((int?)_socket.CloseStatus == CloseAuthenticationFailed)
                    throw new ObsAuthenticationException("OBS refused the password.");
                throw new WebSocketException($"OBS closed the connection ({(int?)_socket.CloseStatus}).");
            }
            message.Write(_buffer, 0, result.Count);
            if (message.Length > 1024 * 1024) throw new WebSocketException("OBS sent an oversized message.");
            if (result.EndOfMessage) break;
        }
        return JsonDocument.Parse(message.ToArray());
    }

    private static int OpCode(JsonElement root) =>
        root.TryGetProperty("op", out var op) && op.TryGetInt32(out var value) ? value : -1;

    /// <summary>Says goodbye if the socket is still open, then lets it go; never throws.</summary>
    public async Task CloseAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
        }
        catch
        {
            // Closed or aborted under us: nothing left to close.
        }
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Abort();
        _socket.Dispose();
    }
}
