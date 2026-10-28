using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OctoglowSender;

/// <summary>
/// Finds Desk Clock firmware on the local network by probing its public
/// /authstate endpoint: the configured endpoint, the saved clock's name on the
/// network (&lt;name&gt;.local, which firmware with mDNS answers to), the
/// provisioning address and the local /24 slices. /authstate also carries the
/// clock's id and name, which is how a clock is recognised at a new address.
/// </summary>
public sealed class DeskClockDiscoveryService : IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ConfiguredProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(400),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<IReadOnlyList<DeskClockEndpoint>> DiscoverAsync(
        AppConfig config, CancellationToken cancellationToken, bool scanLocalNetwork = true)
    {
        var candidates = BuildCandidates(config,
            await ResolveNameAsync(config.Esp32Host, cancellationToken).ConfigureAwait(false), scanLocalNetwork);
        var found = new ConcurrentDictionary<string, DeskClockEndpoint>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 48 },
            async (candidate, token) =>
            {
                var result = await ProbeAsync(candidate, token).ConfigureAwait(false);
                if (result is not null)
                    found.TryAdd(result.Id, result);
            }).ConfigureAwait(false);

        // The same clock may answer through its saved IP, mDNS and another
        // network adapter. Its firmware id is the physical identity.
        return found.Values
            .GroupBy(endpoint => string.IsNullOrWhiteSpace(endpoint.DeviceId)
                ? endpoint.Id : "device:" + endpoint.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(endpoint => endpoint.IsConfiguredAddress).First())
            .OrderByDescending(endpoint => endpoint.IsConfiguredAddress)
            .ThenBy(endpoint => ParseSortableAddress(endpoint.Address))
            .ToList();
    }

    private async Task<DeskClockEndpoint?> ProbeAsync(DeskClockCandidate candidate, CancellationToken cancellationToken)
    {
        // The saved clock is the one users care about most. ESP32 can answer a
        // little late while changing tiles or fetching data, so give this one a
        // longer timeout and a second chance. Network-wide candidates stay fast.
        var attempts = candidate.IsConfiguredAddress ? 2 : 1;
        var probeTimeout = candidate.IsConfiguredAddress ? ConfiguredProbeTimeout : ProbeTimeout;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(probeTimeout);

            try
            {
                var builder = new UriBuilder(Uri.UriSchemeHttp, candidate.Address, candidate.Port, "/authstate");
                using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
                request.Headers.UserAgent.ParseAdd("Octoglow-Connect/2.0");
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode) continue;
                // /authstate is tiny. Never let an unrelated LAN web server
                // make discovery buffer an unbounded response.
                if (response.Content.Headers.ContentLength is > 4096) continue;
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var bytes = new byte[4097];
                var count = 0;
                while (count < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(count), timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    count += read;
                }
                if (count > 4096) continue;
                var body = Encoding.UTF8.GetString(bytes, 0, count);
                using var json = JsonDocument.Parse(body);
                if (!json.RootElement.TryGetProperty("configured", out var configured) ||
                    configured.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    continue;
                }

                return new DeskClockEndpoint(
                    candidate.Address,
                    candidate.Port,
                    candidate.IsConfiguredAddress,
                    configured.GetBoolean(),
                    Text(json.RootElement, "id"),
                    Text(json.RootElement, "host"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or IOException)
            {
                // A configured clock gets one retry below. Other candidates are
                // intentionally probed only once so a full LAN scan stays quick.
            }
        }

        return null;
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    // The saved clock by its name: Windows asks the network over mDNS for
    // <name>.local. Quick, and finds it even outside the /24 slices scanned.
    private static async Task<IReadOnlyList<string>> ResolveNameAsync(string host, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host)) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host.Trim() + ".local", AddressFamily.InterNetwork, timeout.Token).ConfigureAwait(false);
            return addresses.Select(address => address.ToString()).ToList();
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException or ArgumentException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return [];
        }
    }

    private static IReadOnlyList<DeskClockCandidate> BuildCandidates(
        AppConfig config, IReadOnlyList<string> byName, bool scanLocalNetwork)
    {
        var results = new Dictionary<string, DeskClockCandidate>(StringComparer.OrdinalIgnoreCase);

        if (TryNormalizeConfiguredAddress(config.Esp32Ip, config.Esp32Port, out var configured))
            Add(configured.Address, configured.Port, isConfigured: true);
        foreach (var address in byName)
            Add(address, config.Esp32Port is > 0 and <= 65535 ? config.Esp32Port : 80, isConfigured: false);

        if (!scanLocalNetwork) return results.Values.ToList();

        // Provisioning belongs to discovery, not refreshing already paired devices.
        Add("192.168.4.1", 80, isConfigured: false);

        try
        {
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            var networks = NetworkInterface.GetAllNetworkInterfaces()
                .OrderByDescending(network => network.GetIPProperties().GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork));

            foreach (var network in networks)
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(unicast.Address))
                    {
                        continue;
                    }

                    var bytes = unicast.Address.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254) continue;

                    var prefix = $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
                    if (!prefixes.Add(prefix)) continue;
                    if (prefixes.Count > 3) break;

                    // A full /16 or VPN range would be too invasive. The firmware is a
                    // LAN device, so scan only the host's current /24 slice (254 hosts).
                    for (var host = 1; host <= 254; host++)
                    {
                        if (host == bytes[3]) continue;
                        Add($"{prefix}.{host}", 80, isConfigured: false);
                    }
                }

                if (prefixes.Count >= 3) break;
            }
        }
        catch (NetworkInformationException)
        {
            // The saved address and AP fallback remain available.
        }

        return results.Values.ToList();

        void Add(string address, int port, bool isConfigured)
        {
            if (string.IsNullOrWhiteSpace(address) || port is < 1 or > 65535) return;
            var key = $"{address}:{port}";
            if (results.TryGetValue(key, out var existing))
                results[key] = existing with { IsConfiguredAddress = existing.IsConfiguredAddress || isConfigured };
            else
                results[key] = new DeskClockCandidate(address, port, isConfigured);
        }
    }

    internal static bool TryNormalizeConfiguredAddress(string rawAddress, int fallbackPort, out DeskClockCandidate candidate)
    {
        candidate = default;
        if (string.IsNullOrWhiteSpace(rawAddress)) return false;

        var value = rawAddress.Trim();
        var withScheme = value.Contains("://", StringComparison.Ordinal) ? value : $"http://{value}";
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;

        var port = uri.IsDefaultPort ? fallbackPort : uri.Port;
        if (port is < 1 or > 65535) port = 80;
        candidate = new DeskClockCandidate(uri.Host, port, IsConfiguredAddress: true);
        return true;
    }

    private static uint ParseSortableAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var parsed)) return uint.MaxValue;
        var bytes = parsed.GetAddressBytes();
        return bytes.Length == 4
            ? ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3]
            : uint.MaxValue;
    }

    public void Dispose() => _httpClient.Dispose();

    internal readonly record struct DeskClockCandidate(string Address, int Port, bool IsConfiguredAddress);
}

public sealed record DeskClockEndpoint(
    string Address,
    int Port,
    bool IsConfiguredAddress,
    bool HasAccount,
    string? DeviceId = null,
    string? HostName = null)
{
    public string Id => $"clock:{Address.Trim().ToLowerInvariant()}:{Port}";
}
