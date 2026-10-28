using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OctoglowSender;

internal sealed class ClockDeviceState
{
    private readonly JsonElement _root;

    public ClockDeviceState(JsonElement root) => _root = root.Clone();

    // Whether the clock sent the field at all - how a feature newer than its
    // firmware is told apart from one that is merely switched off.
    public bool Has(string name) => _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out _);

    public bool Bool(string name, bool fallback = false) =>
        _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;

    public int Int(string name, int fallback = 0) =>
        _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : fallback;

    public double Double(string name, double fallback = 0) =>
        _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)
            ? result
            : fallback;

    public string String(string name, string fallback = "") =>
        _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    // The objects of an array field, such as the clock's alarms.
    public IReadOnlyList<ClockDeviceState> Objects(string name)
    {
        if (_root.ValueKind != JsonValueKind.Object ||
            !_root.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
            return [];
        return values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new ClockDeviceState(value))
            .ToList();
    }

    // The objects of a response that is itself an array, such as a location search.
    public IReadOnlyList<ClockDeviceState> Items()
    {
        if (_root.ValueKind != JsonValueKind.Array) return [];
        return _root.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new ClockDeviceState(value))
            .ToList();
    }

    public IReadOnlyList<ClockTileState> Tiles()
    {
        if (_root.ValueKind != JsonValueKind.Object ||
            !_root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<ClockTileState>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var tileId)) continue;
            var enabled = item.TryGetProperty("enabled", out var enabledValue) &&
                enabledValue.ValueKind == JsonValueKind.True;
            var duration = item.TryGetProperty("dur", out var durationValue) &&
                durationValue.ValueKind == JsonValueKind.Number && durationValue.TryGetInt32(out var parsed)
                ? parsed
                : 8;
            result.Add(new ClockTileState(tileId, enabled, Math.Clamp(duration, 1, 3600)));
        }
        return result;
    }
}

internal sealed record ClockTileState(int Id, bool Enabled, int Duration);

internal static class ClockDeviceApi
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static HttpClient? _client;
    private static string _sessionKey = "";
    // The app's one session with the clock (ClockSession), sent by hand: the
    // clock's login may be closed, so this never signs in on its own account.
    private static string _baseUrl = "";
    private static string _clockKey = "";
    private static string? _cookie;

    public static async Task<ClockDeviceState> GetStateAsync(DeviceDescriptor? device, CancellationToken token)
    {
        var json = await SendAsync(device, HttpMethod.Get, "/state", null, token);
        using var document = JsonDocument.Parse(json);
        return new ClockDeviceState(document.RootElement);
    }

    // Macro bindings must not send the configured account to a stale/different device.
    public static async Task<ClockDeviceState> GetConfiguredStateAsync(DeviceDescriptor device, CancellationToken token)
    {
        var json = await SendAsync(device, HttpMethod.Get, "/state", null, token, requireConfiguredTarget: true);
        using var document = JsonDocument.Parse(json);
        return new ClockDeviceState(document.RootElement);
    }

    public static Task PostConfiguredAsync(DeviceDescriptor device, string path,
        IEnumerable<KeyValuePair<string, string>> values, CancellationToken token) =>
        SendAsync(device, HttpMethod.Post, path, new FormUrlEncodedContent(values), token, requireConfiguredTarget: true);

    public static async Task<ClockDeviceState> GetJsonAsync(DeviceDescriptor? device, string path, CancellationToken token)
    {
        var json = await SendAsync(device, HttpMethod.Get, path, null, token);
        using var document = JsonDocument.Parse(json);
        return new ClockDeviceState(document.RootElement);
    }

    public static Task PostAsync(
        DeviceDescriptor? device,
        string path,
        IEnumerable<KeyValuePair<string, string>> values,
        CancellationToken token) =>
        SendAsync(device, HttpMethod.Post, path, new FormUrlEncodedContent(values), token);

    // The response exactly as the clock sent it - a backup keeps /state as it is.
    public static Task<string> GetTextAsync(DeviceDescriptor? device, string path, CancellationToken token) =>
        SendAsync(device, HttpMethod.Get, path, null, token);

    public static Task<string> PostJsonAsync(DeviceDescriptor? device, string path, string json, CancellationToken token) =>
        SendAsync(device, HttpMethod.Post, path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"), token);

    public static async Task UploadFirmwareAsync(
        DeviceDescriptor? device,
        Stream stream,
        string fileName,
        CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            var session = PrepareSession(device);
            await EnsureAuthenticatedAsync(session.Config, null, token);
            using var content = new MultipartFormDataContent();
            using var binary = new StreamContent(stream);
            binary.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(binary, "update", fileName);
            using var response = await SendCoreAsync(HttpMethod.Post, "/swupdateupload", content, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await EnsureAuthenticatedAsync(session.Config, TakeCookie(), token);
                throw new InvalidOperationException("Sesiunea a expirat. Selectează din nou fișierul pentru actualizare.");
            }
            await EnsureSuccessAsync(response, token);
        }
        finally
        {
            Gate.Release();
        }
    }

    // Tries an account against the clock without touching the session the app is
    // already using, so a wrong password can be reported before anything is saved.
    public static async Task<bool> TrySignInAsync(string address, int port, string user, string password, CancellationToken token)
    {
        if (!DeskClockDiscoveryService.TryNormalizeConfiguredAddress(address, port, out var endpoint))
            throw new InvalidOperationException("Adresa ceasului nu este validă.");

        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://{endpoint.Address}:{endpoint.Port}/"),
            Timeout = TimeSpan.FromSeconds(8),
        };
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["user"] = user,
            ["pass"] = password,
        });
        using var response = await client.PostAsync("login", content, token);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return false;
        if ((int)response.StatusCode == 429) throw new UnauthorizedAccessException(ClockSession.LockedMessage);
        await EnsureSuccessAsync(response, token);
        // Typed in just now: the session it opened is the app's from here on,
        // and details refused earlier may be tried again.
        ClockSession.ClearRefusal();
        ClockSession.Remember(ClockSession.Key(endpoint.Address, endpoint.Port, user), response);
        return true;
    }

    public static void ForgetSession()
    {
        _cookie = null;
        _sessionKey = "";
        // A request may still be inside Gate. PrepareSession replaces and
        // disposes the old client only when the next request owns that gate.
    }

    private static string? TakeCookie()
    {
        var failed = _cookie;
        _cookie = null;
        return failed;
    }

    private static async Task<string> SendAsync(
        DeviceDescriptor? device,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken token,
        bool requireConfiguredTarget = false)
    {
        await Gate.WaitAsync(token);
        try
        {
            var session = PrepareSession(device, requireConfiguredTarget);
            await EnsureAuthenticatedAsync(session.Config, null, token);
            using var retryContent = await CloneContentAsync(content, token);
            using var response = await SendCoreAsync(method, path, content, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await EnsureAuthenticatedAsync(session.Config, TakeCookie(), token);
                using var retry = await SendCoreAsync(method, path, retryContent, token);
                return await EnsureSuccessAsync(retry, token);
            }
            return await EnsureSuccessAsync(response, token);
        }
        finally
        {
            content?.Dispose();
            Gate.Release();
        }
    }

    private static (AppConfig Config, Uri BaseUri) PrepareSession(DeviceDescriptor? device, bool requireConfiguredTarget = false)
    {
        var config = ConfigStore.Load();
        if (requireConfiguredTarget && (device is null ||
            !DeskClockDiscoveryService.TryNormalizeConfiguredAddress(config.Esp32Ip, config.Esp32Port, out var saved) ||
            !string.Equals(device.Id, DeviceDescriptor.DeskClock(saved.Address, saved.Port, false, "").Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(AppStrings.IsEnglish ? "The configured clock has changed. Select the device again." :
                "Ceasul configurat s-a schimbat. Selectează din nou dispozitivul.");
        var address = !string.IsNullOrWhiteSpace(device?.Address) ? device.Address! : config.Esp32Ip;
        var port = device?.Port is > 0 and <= 65535 ? device.Port : config.Esp32Port;
        if (!DeskClockDiscoveryService.TryNormalizeConfiguredAddress(address, port, out var endpoint))
            throw new InvalidOperationException("Adresa ceasului nu este validă.");

        var baseUri = new Uri($"http://{endpoint.Address}:{endpoint.Port}/");
        var nextKey = $"{baseUri}|{config.ScUser}|{config.ScPass}";
        if (_client is not null && string.Equals(nextKey, _sessionKey, StringComparison.Ordinal))
            return (config, baseUri);

        _client?.Dispose();
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        _client = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(6),
        };
        _client.DefaultRequestHeaders.ConnectionClose = true;
        _sessionKey = nextKey;
        _baseUrl = $"http://{endpoint.Address}:{endpoint.Port}";
        _clockKey = ClockSession.Key(endpoint.Address, endpoint.Port, config.ScUser);
        _cookie = null;
        return (config, baseUri);
    }

    // failedCookie: the session the clock just refused, or null.
    private static async Task EnsureAuthenticatedAsync(AppConfig config, string? failedCookie, CancellationToken token)
    {
        if (_cookie is not null) return;
        var (result, cookie) = await ClockSession.SignInAsync(_baseUrl, _clockKey, config.ScUser, config.ScPass, failedCookie, token);
        switch (result)
        {
            case ClockSession.Result.Ok:
                _cookie = cookie;
                return;
            case ClockSession.Result.NoCredentials:
                throw new InvalidOperationException(AppStrings.IsEnglish
                    ? "Fill in the username and password in About, under Sign-in details."
                    : "Completează utilizatorul și parola în Despre, la Date de conectare.");
            case ClockSession.Result.Rejected:
                throw new UnauthorizedAccessException(AppStrings.IsEnglish
                    ? "The clock's username or password is wrong. Correct them in About, under Sign-in details."
                    : "Utilizatorul sau parola ceasului sunt incorecte. Corectează-le în Despre, la Date de conectare.");
            case ClockSession.Result.Locked:
                throw new UnauthorizedAccessException(ClockSession.LockedMessage);
            default:
                throw new HttpRequestException(AppStrings.IsEnglish ? "The clock isn't answering." : "Ceasul nu răspunde.");
        }
    }

    private static async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/')) { Content = content };
        if (_cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", _cookie);
        return await _client!.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
    }

    private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        if (response.IsSuccessStatusCode) return body;
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : ClockErrorText(body);
        throw new HttpRequestException($"Ceasul a răspuns cu {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }

    // {"ok":false,"err":"..."} from the clock reads as its message.
    private static string ClockErrorText(string body)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(body) is System.Text.Json.Nodes.JsonObject json &&
                json["err"] is System.Text.Json.Nodes.JsonValue err && err.TryGetValue<string>(out var text) && text.Length > 0)
                return text;
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return body;
    }

    private static async Task<HttpContent?> CloneContentAsync(HttpContent? content, CancellationToken token)
    {
        if (content is null) return null;
        var bytes = await content.ReadAsByteArrayAsync(token);
        var clone = new ByteArrayContent(bytes);
        foreach (var header in content.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
}
