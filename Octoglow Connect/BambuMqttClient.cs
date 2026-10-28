using System.Net.Security;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace OctoglowSender;

// Minimal MQTT 3.1.1 subscriber for the printer's LAN status topic. It never
// sends print-control commands: only CONNECT, SUBSCRIBE, PING and pushall
// (the printer's read-only full-status request).
internal sealed class BambuMqttClient : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly SslStream _tls;
    private readonly string _serial;

    private BambuMqttClient(TcpClient tcp, SslStream tls, string serial)
    {
        _tcp = tcp;
        _tls = tls;
        _serial = serial;
    }

    public static async Task<BambuMqttClient> ConnectAsync(string host, string serial, string accessCode, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(accessCode))
            throw new InvalidOperationException("Missing printer LAN credentials.");
        if (serial.IndexOfAny(['/', '+', '#', '\0']) >= 0)
            throw new ArgumentException("Invalid printer serial.", nameof(serial));
        if (!IsLocalAddress(host)) throw new ArgumentException("Printer address must be on a private LAN.", nameof(host));

        var tcp = new TcpClient();
        SslStream? tls = null;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(host, 8883, connectTimeout.Token);
            // Bambu LAN printers use a device-generated self-signed certificate.
            // Restrict this exception to the user's explicitly configured LAN host.
            tls = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) => certificate is not null);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, connectTimeout.Token);

            var client = new BambuMqttClient(tcp, tls, serial);
            await client.SendConnectAsync(accessCode, connectTimeout.Token);
            var (type, body) = await client.ReadPacketAsync(connectTimeout.Token);
            if (type != 0x20 || body.Length < 2 || body[1] != 0)
                throw new BambuMqttException(body.Length >= 2 ? body[1] : -1);
            await client.SubscribeAsync(connectTimeout.Token);
            return client;
        }
        catch
        {
            if (tls is not null) await tls.DisposeAsync();
            tcp.Dispose();
            throw;
        }
    }

    internal static bool IsLocalAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 169 && bytes[1] == 254;
    }

    private async Task SendConnectAsync(string accessCode, CancellationToken token)
    {
        using var payload = new MemoryStream();
        WriteMqttString(payload, "MQTT");
        payload.WriteByte(4); // MQTT 3.1.1
        payload.WriteByte(0xC2); // user + password + clean session
        payload.WriteByte(0);
        payload.WriteByte(30); // keepalive seconds
        WriteMqttString(payload, "octoglow-" + Guid.NewGuid().ToString("N")[..12]);
        WriteMqttString(payload, "bblp");
        WriteMqttString(payload, accessCode);
        await WritePacketAsync(0x10, payload.ToArray(), token);
    }

    private async Task SubscribeAsync(CancellationToken token)
    {
        using var payload = new MemoryStream();
        payload.WriteByte(0);
        payload.WriteByte(1); // packet id
        WriteMqttString(payload, $"device/{_serial}/report");
        payload.WriteByte(0); // QoS 0
        await WritePacketAsync(0x82, payload.ToArray(), token);
        var (type, body) = await ReadPacketAsync(token);
        if (type != 0x90 || body.Length < 3 || body[2] == 0x80)
            throw new IOException("Printer rejected the status subscription.");
        await RequestFullStatusAsync(token);
    }

    public async Task RequestFullStatusAsync(CancellationToken token)
    {
        using var payload = new MemoryStream();
        WriteMqttString(payload, $"device/{_serial}/request");
        payload.Write(Encoding.UTF8.GetBytes("{\"pushing\":{\"command\":\"pushall\",\"sequence_id\":\"0\"}}"));
        await WritePacketAsync(0x30, payload.ToArray(), token);
    }

    public Task PingAsync(CancellationToken token) => WritePacketAsync(0xC0, [], token);

    // Returns null on a short idle timeout so the caller can refresh the clock
    // and react to tile/config changes without opening another MQTT connection.
    public async Task<string?> ReceiveAsync(CancellationToken token)
    {
        var first = new byte[1];
        using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        idleTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _tls.ReadExactlyAsync(first, idleTimeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
        using var packetTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        packetTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var (header, body) = await ReadPacketAsync(packetTimeout.Token, first[0]);
            if ((header & 0xF0) != 0x30) return null;
            if (body.Length < 2) throw new IOException("Invalid MQTT publish packet.");
            var topicLength = (body[0] << 8) | body[1];
            var payloadAt = 2 + topicLength + (((header >> 1) & 3) == 0 ? 0 : 2);
            if (payloadAt > body.Length) throw new IOException("Invalid MQTT topic length.");
            return Encoding.UTF8.GetString(body, payloadAt, body.Length - payloadAt);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("Printer MQTT packet timed out."); }
    }

    private async Task<(byte Header, byte[] Body)> ReadPacketAsync(CancellationToken token, byte? firstHeader = null)
    {
        var one = new byte[1];
        if (firstHeader is null) await _tls.ReadExactlyAsync(one, token);
        var header = firstHeader ?? one[0];
        var length = 0;
        var multiplier = 1;
        for (var i = 0; i < 4; i++)
        {
            await _tls.ReadExactlyAsync(one, token);
            length += (one[0] & 0x7F) * multiplier;
            if ((one[0] & 0x80) == 0) break;
            if (i == 3) throw new IOException("Invalid MQTT remaining length.");
            multiplier *= 128;
        }
        if (length > 256 * 1024) throw new IOException("MQTT status packet too large.");
        var body = new byte[length];
        await _tls.ReadExactlyAsync(body, token);
        return (header, body);
    }

    private async Task WritePacketAsync(byte header, byte[] body, CancellationToken token)
    {
        using var packet = new MemoryStream();
        packet.WriteByte(header);
        var length = body.Length;
        do
        {
            var part = length % 128;
            length /= 128;
            packet.WriteByte((byte)(part | (length > 0 ? 0x80 : 0)));
        } while (length > 0);
        packet.Write(body);
        await _tls.WriteAsync(packet.ToArray(), token);
        await _tls.FlushAsync(token);
    }

    private static void WriteMqttString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue) throw new ArgumentException("MQTT string too long.");
        stream.WriteByte((byte)(bytes.Length >> 8));
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        await _tls.DisposeAsync();
        _tcp.Dispose();
    }
}

internal sealed class BambuMqttException(int reason) : IOException($"Printer MQTT connection refused ({reason}).")
{
    public int Reason { get; } = reason;
}
