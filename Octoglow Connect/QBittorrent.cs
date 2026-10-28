using System.Net;
using System.Text.Json;

namespace OctoglowSender;

// QBITTORRENT
//
// Octoglow Connect reads qBittorrent's WebUI API - the "Web User Interface
// (Remote control)" in qBittorrent's options - and nothing else: the torrents
// being downloaded, to show one torrent's progress on the clock. It never
// starts, stops, adds or removes a torrent, and never touches the downloaded
// files. It signs in with an API key (qBittorrent 5.2 and newer, sent as
// "Authorization: Bearer") or with the WebUI user name and password (every
// version: POST /api/v2/auth/login, then the session cookie). The key and the
// password live encrypted in the app's settings; neither is ever logged.

public enum QbitConnectionState
{
    /// <summary>The backend isn't running, or the qBittorrent tile is off on the clock.</summary>
    Off,
    /// <summary>The clock's firmware has no qBittorrent tile.</summary>
    ClockUnsupported,
    Connecting,
    Connected,
    /// <summary>Nothing answers: qBittorrent is closed, or its WebUI is off.</summary>
    Unavailable,
    /// <summary>Something answers at that address, but not the qBittorrent WebUI API.</summary>
    NotQbittorrent,
    /// <summary>qBittorrent refused the key or the user name and password (or wants them and none are set).</summary>
    AuthFailed,
    /// <summary>qBittorrent banned this computer's address after too many failed sign-ins.</summary>
    Banned,
}

/// <summary>What the qBittorrent loop last saw. Progress is the shown torrent's; Downloads counts the torrents moving data.</summary>
public sealed record QbitSnapshot(QbitConnectionState State, bool Downloading, int Progress, int Downloads)
{
    public static readonly QbitSnapshot Off = new(QbitConnectionState.Off, false, 0, 0);
}

/// <summary>What the qBittorrent loop last saw, for the pages that show it. Only the backend writes it.</summary>
public static class QBittorrentStatus
{
    private static readonly object Gate = new();
    private static QbitSnapshot _current = QbitSnapshot.Off;

    public static QbitSnapshot Current
    {
        get
        {
            lock (Gate) return _current;
        }
    }

    public static void Set(QbitSnapshot next)
    {
        lock (Gate)
        {
            if (_current == next) return;
            _current = next;
        }
    }
}

/// <summary>One torrent, as much of it as the tile needs. Progress is 0-1, DownloadSpeed bytes/s, LastActivity Unix seconds.</summary>
internal sealed record QbitTorrent(string Hash, string State, double Progress, long DownloadSpeed, long LastActivity);

// WHICH TORRENT
//
// With several torrents downloading, the clock shows one, chosen the same way
// every time: the one downloading fastest right now. Once shown it stays
// until it finishes, is stopped, queued or checked, or has moved no data for
// StallGrace - then the fastest one again. Ties go to the most recent
// activity, then to the hash, so two equal torrents never take turns. Only
// torrents qBittorrent calls "downloading" or "forcedDL" (data coming in) are
// in progress for the clock: a queued, stalled, stopped or checking one isn't,
// and one still fetching metadata has no progress yet. The percent is that
// one torrent's own - never an average over several, a number no torrent has.
internal sealed class QbitTorrentPicker
{
    /// <summary>How long a shown torrent may move no data before another one moving data takes its place.</summary>
    public static readonly TimeSpan StallGrace = TimeSpan.FromSeconds(20);

    private string? _shown;
    private DateTime _lastMoving;

    public QbitTorrent? Pick(IReadOnlyList<QbitTorrent> torrents, DateTime now)
    {
        var current = _shown is null ? null : torrents.FirstOrDefault(torrent => torrent.Hash == _shown);
        if (current is not null && current.Progress < 1 && current.State is "downloading" or "forcedDL" or "stalledDL")
        {
            if (IsMoving(current)) _lastMoving = now;
            if (now - _lastMoving < StallGrace) return current;
        }
        var best = torrents
            .Where(torrent => IsMoving(torrent) && torrent.Progress < 1)
            .OrderByDescending(torrent => torrent.DownloadSpeed)
            .ThenByDescending(torrent => torrent.LastActivity)
            .ThenBy(torrent => torrent.Hash, StringComparer.Ordinal)
            .FirstOrDefault();
        _shown = best?.Hash;
        if (best is not null) _lastMoving = now;
        return best;
    }

    public void Forget() => _shown = null;

    public static bool IsMoving(QbitTorrent torrent) => torrent.State is "downloading" or "forcedDL";

    /// <summary>Whole percent, rounded down: 99.9% is not done, and only a finished torrent shows 100.</summary>
    public static int Percent(QbitTorrent torrent) =>
        double.IsFinite(torrent.Progress) ? Math.Clamp((int)Math.Floor(torrent.Progress * 100), 0, 100) : 0;
}

internal sealed class QbitAuthException : Exception
{
    public QbitAuthException(bool banned) : base(banned ? "qBittorrent banned this address." : "qBittorrent refused the sign-in.") => Banned = banned;
    public bool Banned { get; }
}

internal sealed class QbitNotQbittorrentException : Exception
{
    public QbitNotQbittorrentException() : base("The address does not answer as the qBittorrent WebUI API.") { }
}

/// <summary>A signed-in link to one qBittorrent WebUI. Plain HTTP, straight to the address given, no proxy.</summary>
internal sealed class QBittorrentClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);
    // A long list of torrents is still far below this; anything bigger isn't qBittorrent.
    private const int MaxResponseBytes = 16 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly string? _apiKey;
    // "name=value" of the session cookie qBittorrent set at login, as it set it:
    // "SID" in most versions, a port-specific name in some.
    private string? _session;
    private bool _disposed;

    private QBittorrentClient(Uri baseUri, string? apiKey)
    {
        _base = baseUri;
        _apiKey = apiKey;
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = MaxResponseBytes };
    }

    public static Uri BaseUri(string host, int port) => new UriBuilder(Uri.UriSchemeHttp, host, port, "/").Uri;

    /// <summary>
    /// Signs in and checks the answer is qBittorrent's. With an API key, the key is
    /// tried on a harmless read; with no user name, the WebUI's own "bypass
    /// authentication on localhost" is relied on. Throws QbitAuthException,
    /// QbitNotQbittorrentException, or a network error when nothing answers.
    /// </summary>
    public static async Task<QBittorrentClient> ConnectAsync(string host, int port, bool useApiKey, string apiKey,
        string username, string password, CancellationToken token)
    {
        var client = new QBittorrentClient(BaseUri(host, port), useApiKey ? apiKey.Trim() : null);
        try
        {
            if (useApiKey && string.IsNullOrEmpty(client._apiKey)) throw new QbitAuthException(false);
            if (!useApiKey && username.Length > 0) await client.LoginAsync(username, password, token);
            using var check = await client.SendAsync(HttpMethod.Get, "api/v2/app/webapiVersion", null, token);
            ThrowIfRefused(check);
            var version = (await check.Content.ReadAsStringAsync(token)).Trim();
            // "2.11.4" and the like; a web page or anything else is not the API.
            if (version.Length is 0 or > 20 || !char.IsDigit(version[0])) throw new QbitNotQbittorrentException();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task LoginAsync(string username, string password, CancellationToken token)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
        });
        using var response = await SendAsync(HttpMethod.Post, "api/v2/auth/login", form, token);
        // 403: this address is banned for too many failed attempts. 401, or 200
        // with "Fails.": wrong user name or password.
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new QbitAuthException(true);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new QbitAuthException(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new QbitNotQbittorrentException();
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadAsStringAsync(token)).Trim();
        if (body.Equals("Fails.", StringComparison.OrdinalIgnoreCase)) throw new QbitAuthException(false);
        if (!body.Equals("Ok.", StringComparison.OrdinalIgnoreCase)) throw new QbitNotQbittorrentException();
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var first = cookies.FirstOrDefault()?.Split(';', 2)[0].Trim();
            if (!string.IsNullOrEmpty(first) && first.Contains('=')) _session = first;
        }
        // "Ok." without a cookie: qBittorrent lets this address in without one.
    }

    /// <summary>The torrents qBittorrent lists as downloading - its "downloading" filter, which leaves seeding ones out.</summary>
    public async Task<IReadOnlyList<QbitTorrent>> GetDownloadingAsync(CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v2/torrents/info?filter=downloading", null, token);
        ThrowIfRefused(response);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (json.RootElement.ValueKind != JsonValueKind.Array) throw new QbitNotQbittorrentException();
        var torrents = new List<QbitTorrent>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var hash = Text(item, "hash");
            if (hash.Length == 0) continue;
            torrents.Add(new QbitTorrent(hash, Text(item, "state"), Number(item, "progress"),
                (long)Number(item, "dlspeed"), (long)Number(item, "last_activity")));
        }
        return torrents;
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static double Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;

    // 401 or 403 on anything but the login: the key is wrong or revoked, or the
    // session ran out. 404: not the qBittorrent API.
    private static void ThrowIfRefused(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new QbitAuthException(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new QbitNotQbittorrentException();
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(_base, path)) { Content = content };
        // qBittorrent refuses a request whose Referer names another site; this one names itself.
        request.Headers.Referrer = _base;
        if (_apiKey is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
        if (_session is not null) request.Headers.TryAddWithoutValidation("Cookie", _session);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new HttpRequestException("qBittorrent did not answer in time.");
        }
    }

    /// <summary>Ends a password session, so none is left open in qBittorrent; never throws.</summary>
    public async Task CloseAsync()
    {
        try
        {
            if (_session is not null && !_disposed)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                using var response = await SendAsync(HttpMethod.Post, "api/v2/auth/logout", null, timeout.Token);
            }
        }
        catch
        {
            // qBittorrent is gone already; its session ends on its own.
        }
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
