using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace OctoglowSender;

// THE CLOCK'S SESSION
//
// After five wrong passwords the clock closes its login until it restarts
// (firmware "LOGIN LOCK"), while a session already signed in keeps working. So
// the app holds on to its session: one cookie (sc_tok=...) for everything that
// talks to the clock - the background loops, the settings pages, the web panel -
// saved between runs, encrypted for this Windows user, per clock address and
// user. It signs in only when it has no session or the clock refused the one it
// had; one sign-in at a time, so the loops that all hit a 401 together don't
// each spend a try; and never again with a password the clock has turned down,
// or they would use up the five tries between them in seconds. /whoami renews a
// session near its end (the clock sends a new cookie), so one in use never has
// to give the password again.
internal static class ClockSession
{
    public enum Result { Ok, Rejected, Locked, Unreachable, NoCredentials }

    // Settable only so the off-device tests keep away from the real file.
    internal static string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OctoglowSender", "session.dat");
    // While the clock is closed, try again at most this often: a try then is not
    // counted, and the first to get through after its restart opens the session.
    private static readonly TimeSpan LockedRetry = TimeSpan.FromSeconds(60);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object Sync = new();
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(6),
    };
    private static Dictionary<string, string>? _cookies;
    private static string? _rejected;           // key + password the clock turned down
    private static readonly Dictionary<string, DateTime> LockedAt = new();

    public const string LockedMessageRo = "Ceasul a blocat logările după prea multe parole greșite. Repornește ceasul ca să le redeschizi.";
    public const string LockedMessageEn = "The clock has blocked sign-ins after too many wrong passwords. Restart the clock to open them again.";
    public static string LockedMessage => AppStrings.IsEnglish ? LockedMessageEn : LockedMessageRo;

    public static string Key(string address, int port, string user) =>
        $"{address.Trim().ToLowerInvariant()}:{port}|{user}";

    public static string? Current(string key)
    {
        lock (Sync) return Cookies().TryGetValue(key, out var cookie) ? cookie : null;
    }

    public static bool IsLocked(string key)
    {
        lock (Sync) return LockedAt.TryGetValue(key, out var at) && DateTime.UtcNow - at < LockedRetry;
    }

    // The session to use, signing in only when needed. failedCookie is the one
    // the clock just refused (null when nothing was refused): if another caller
    // has replaced it meanwhile, that one is used; a saved session is first
    // checked (and renewed) with /whoami.
    public static async Task<(Result Result, string? Cookie)> SignInAsync(
        string baseUrl, string key, string user, string password, string? failedCookie, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = Current(key);
            if (current is not null && failedCookie is not null && current != failedCookie)
                return (Result.Ok, current);
            if (current is not null && failedCookie is null)
            {
                var checkedCookie = await WhoamiAsync(baseUrl, key, current, token).ConfigureAwait(false);
                if (checkedCookie is not null) return (Result.Ok, checkedCookie);
            }
            if (current is not null) Forget(key, current);

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password)) return (Result.NoCredentials, null);
            var attempt = key + "\n" + password;
            lock (Sync)
            {
                if (_rejected == attempt) return (Result.Rejected, null);
                if (LockedAt.TryGetValue(key, out var at) && DateTime.UtcNow - at < LockedRetry) return (Result.Locked, null);
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["user"] = user, ["pass"] = password });
            HttpResponseMessage response;
            try
            {
                response = await Http.PostAsync($"{baseUrl.TrimEnd('/')}/login", content, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpRequestException || (error is TaskCanceledException && !token.IsCancellationRequested))
            {
                return (Result.Unreachable, null);
            }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    lock (Sync) _rejected = attempt;
                    return (Result.Rejected, null);
                }
                if ((int)response.StatusCode == 429)
                {
                    lock (Sync) LockedAt[key] = DateTime.UtcNow;
                    return (Result.Locked, null);
                }
                if (!response.IsSuccessStatusCode) return (Result.Unreachable, null);
                lock (Sync) LockedAt.Remove(key);
                var cookie = CookieFrom(response);
                if (cookie is null) return (Result.Unreachable, null);
                Remember(key, cookie);
                return (Result.Ok, cookie);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    // Keeps a session in use alive: /whoami answers with a new cookie when the
    // one it got is near its end. Null when the clock no longer takes it.
    public static async Task<string?> RenewAsync(string baseUrl, string key, CancellationToken token)
    {
        var current = Current(key);
        if (current is null) return null;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await WhoamiAsync(baseUrl, key, current, token).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    // A cookie the clock set when the user signed in by hand (checking an account).
    public static void Remember(string key, HttpResponseMessage response)
    {
        var cookie = CookieFrom(response);
        if (cookie is not null) Remember(key, cookie);
    }

    // Details the user has just typed in again may be tried once more.
    public static void ClearRefusal()
    {
        lock (Sync) _rejected = null;
    }

    private static async Task<string?> WhoamiAsync(string baseUrl, string key, string cookie, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/whoami");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
            if (!response.IsSuccessStatusCode) return cookie;   // busy, not a refusal
            var renewed = CookieFrom(response);
            if (renewed is not null && renewed != cookie)
            {
                Remember(key, renewed);
                return renewed;
            }
            return cookie;
        }
        catch (Exception error) when (error is HttpRequestException || (error is TaskCanceledException && !token.IsCancellationRequested))
        {
            return cookie;   // not reachable just now: nothing says it stopped working
        }
    }

    private static string? CookieFrom(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var value in values)
        {
            // Only name=value goes back in a Cookie header, not Path or HttpOnly.
            var pair = value.Split(';', 2)[0].Trim();
            if (pair.StartsWith("sc_tok=", StringComparison.Ordinal) && pair.Length > "sc_tok=".Length) return pair;
        }
        return null;
    }

    private static void Remember(string key, string cookie)
    {
        lock (Sync)
        {
            var cookies = Cookies();
            if (cookies.TryGetValue(key, out var old) && old == cookie) return;
            cookies[key] = cookie;
            Save(cookies);
        }
    }

    // The same clock at a new address: its session goes with it.
    public static void Move(string fromKey, string toKey)
    {
        lock (Sync)
        {
            var cookies = Cookies();
            if (fromKey == toKey || !cookies.Remove(fromKey, out var cookie)) return;
            cookies[toKey] = cookie;
            Save(cookies);
        }
    }

    public static void Forget(string key, string cookie)
    {
        lock (Sync)
        {
            var cookies = Cookies();
            if (!cookies.TryGetValue(key, out var old) || old != cookie) return;
            cookies.Remove(key);
            Save(cookies);
        }
    }

    // One "key<TAB>cookie" per line, protected for this Windows user.
    private static Dictionary<string, string> Cookies()
    {
        if (_cookies is not null) return _cookies;
        _cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(FilePath))
            {
                var text = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser));
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var tab = line.IndexOf('\t');
                    if (tab > 0) _cookies[line[..tab]] = line[(tab + 1)..];
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Another PC or user, or a damaged file: sign in again.
        }
        return _cookies;
    }

    private static void Save(Dictionary<string, string> cookies)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var text = string.Join('\n', cookies.Select(pair => pair.Key + "\t" + pair.Value));
            File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Kept in memory for this run.
        }
    }
}
