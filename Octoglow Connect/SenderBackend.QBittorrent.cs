using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

public partial class SenderBackend
{
    // How often qBittorrent is asked, how often the clock hears from us while a
    // torrent is shown (the clock drops the tile after six seconds of silence),
    // and how often the clock's own qBittorrent switch is read.
    private static readonly TimeSpan QbitPollEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QbitKeepaliveEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QbitClockReadEvery = TimeSpan.FromSeconds(3);
    // A firmware without the tile is asked again rarely, in case it gets updated.
    private static readonly TimeSpan QbitClockUnsupportedReadEvery = TimeSpan.FromSeconds(30);
    // Something other than qBittorrent at that address won't turn into it soon.
    private static readonly TimeSpan QbitNotQbittorrentRetryEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] QbitBackoff =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
    ];

    private enum QbitClockRead { Wanted, NotWanted, Unsupported, Failed }

    private enum QbitSendResult { Sent, Disabled, Failed }

    // What was last sent to /qbitstatus.
    private sealed record QbitSent(bool Active, int Progress);

    private async Task QbitLoopAsync(CancellationToken token)
    {
        QBittorrentClient? qbit = null;
        var clockWanted = false;
        var clockUnsupported = false;
        var nextClockRead = DateTime.MinValue;
        var settingsVersion = -1;
        var retryAt = DateTime.MinValue;
        var failures = 0;
        QbitConnectionState? problem = null;
        // The last problem written to the log: the same one again - after
        // Reconnect with the same wrong password, say - is not written twice.
        QbitConnectionState? logged = null;
        var everConnected = false;
        var picker = new QbitTorrentPicker();
        QbitSent? sent = null;
        var sentAt = DateTime.MinValue;

        async Task DropQbitAsync()
        {
            if (qbit is null) return;
            await qbit.CloseAsync();
            qbit = null;
        }

        // Takes the tile off the clock at once rather than letting it time out.
        async Task SendIdleAsync(CancellationToken sendToken)
        {
            if (sent is not { Active: true }) return;
            var idle = new QbitSent(false, 0);
            if (await SendQbitStatusAsync(idle, sendToken) != QbitSendResult.Failed)
            {
                sent = idle;
                sentAt = DateTime.UtcNow;
            }
        }

        // Each problem is written once, when it starts; the Integrations page
        // shows it for as long as it lasts.
        void Problem(QbitConnectionState state, string message)
        {
            if (logged != state) _log("[QBIT] " + message);
            logged = state;
            problem = state;
            QBittorrentStatus.Set(QbitSnapshot.Off with { State = state });
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // 1. Does the clock want the tile at all?
                if (now >= nextClockRead)
                {
                    var read = await ReadQbitClockAsync(token);
                    switch (read)
                    {
                        case QbitClockRead.Wanted:
                        case QbitClockRead.NotWanted:
                            clockWanted = read == QbitClockRead.Wanted;
                            clockUnsupported = false;
                            nextClockRead = now + QbitClockReadEvery;
                            break;
                        case QbitClockRead.Unsupported:
                            if (!clockUnsupported)
                                _log("[QBIT] Firmware-ul ceasului nu are încă tile-ul qBittorrent. Actualizează firmware-ul ceasului.");
                            clockUnsupported = true;
                            clockWanted = false;
                            nextClockRead = now + QbitClockUnsupportedReadEvery;
                            break;
                        default:
                            // The clock didn't answer: keep what it said last.
                            nextClockRead = now + QbitClockReadEvery;
                            break;
                    }
                }

                // 2. New address, port or sign-in, or "Reconnect" was pressed.
                var version = ConfigStore.QbitSettingsVersion;
                if (version != settingsVersion)
                {
                    settingsVersion = version;
                    await DropQbitAsync();
                    retryAt = DateTime.MinValue;
                    failures = 0;
                    problem = null;
                    picker.Forget();
                }

                if (!clockWanted)
                {
                    // Switched off or out of the Tile Manager: qBittorrent is not asked anything.
                    await DropQbitAsync();
                    await SendIdleAsync(token);
                    picker.Forget();
                    QBittorrentStatus.Set(clockUnsupported
                        ? QbitSnapshot.Off with { State = QbitConnectionState.ClockUnsupported }
                        : QbitSnapshot.Off);
                    await Task.Delay(500, token);
                    continue;
                }

                // 3. Sign in when there is no session.
                if (qbit is null)
                {
                    // A refused sign-in is tried again only once something changes:
                    // qBittorrent bans an address after a few wrong ones in a row.
                    if (problem is QbitConnectionState.AuthFailed or QbitConnectionState.Banned || now < retryAt)
                    {
                        // Said again after the tile was off, which showed Off meanwhile.
                        if (problem is { } waiting) QBittorrentStatus.Set(QbitSnapshot.Off with { State = waiting });
                        await SendIdleAsync(token);
                        await Task.Delay(250, token);
                        continue;
                    }
                    if (problem is null)
                        QBittorrentStatus.Set(QbitSnapshot.Off with { State = QbitConnectionState.Connecting });
                    try
                    {
                        qbit = await QBittorrentClient.ConnectAsync(ConfigStore.QbitHost, ConfigStore.QbitPort, ConfigStore.QbitUseApiKey,
                            ConfigStore.QbitApiKey, ConfigStore.QbitUsername, ConfigStore.QbitPassword, token);
                        if (logged is not null || !everConnected)
                            _log(everConnected ? "[QBIT] Reconectat la qBittorrent." : "[QBIT] Conectat la qBittorrent.");
                        everConnected = true;
                        failures = 0;
                        problem = null;
                        logged = null;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (QbitAuthException error) when (error.Banned)
                    {
                        Problem(QbitConnectionState.Banned,
                            "qBittorrent a blocat temporar acest calculator după prea multe autentificări greșite.");
                    }
                    catch (QbitAuthException)
                    {
                        Problem(QbitConnectionState.AuthFailed,
                            "qBittorrent a respins autentificarea. Verifică datele din Integrări și apasă Reconectează.");
                    }
                    catch (QbitNotQbittorrentException)
                    {
                        Problem(QbitConnectionState.NotQbittorrent, "La adresa setată răspunde altceva decât WebUI-ul qBittorrent.");
                        retryAt = now + QbitNotQbittorrentRetryEvery;
                    }
                    catch (Exception)
                    {
                        Problem(QbitConnectionState.Unavailable, "qBittorrent nu răspunde (închis sau cu WebUI-ul oprit).");
                        retryAt = now + QbitBackoff[Math.Min(failures++, QbitBackoff.Length - 1)];
                    }
                    if (qbit is null)
                    {
                        picker.Forget();
                        await SendIdleAsync(token);
                        continue;
                    }
                }

                // 4. Ask qBittorrent for what it is downloading.
                IReadOnlyList<QbitTorrent> torrents;
                try
                {
                    torrents = await qbit.GetDownloadingAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (QbitAuthException)
                {
                    // The session ran out (qBittorrent restarted, or its timeout
                    // passed) or the key was changed there: one new sign-in, which
                    // says so itself if it is refused.
                    await DropQbitAsync();
                    retryAt = DateTime.UtcNow + QbitBackoff[0];
                    continue;
                }
                catch (QbitNotQbittorrentException)
                {
                    await DropQbitAsync();
                    Problem(QbitConnectionState.NotQbittorrent, "La adresa setată răspunde altceva decât WebUI-ul qBittorrent.");
                    retryAt = DateTime.UtcNow + QbitNotQbittorrentRetryEvery;
                    picker.Forget();
                    await SendIdleAsync(token);
                    continue;
                }
                catch (Exception)
                {
                    // qBittorrent closed or stopped answering: the tile goes at once.
                    await DropQbitAsync();
                    Problem(QbitConnectionState.Unavailable, "Conexiunea cu qBittorrent s-a întrerupt.");
                    failures = 0;
                    retryAt = DateTime.UtcNow + QbitBackoff[0];
                    picker.Forget();
                    await SendIdleAsync(token);
                    continue;
                }

                // 5. One torrent's percent, and a word to the clock only when that
                // changes, plus a keepalive while the tile is up.
                var chosen = picker.Pick(torrents, DateTime.UtcNow);
                var progress = chosen is null ? 0 : QbitTorrentPicker.Percent(chosen);
                QBittorrentStatus.Set(new QbitSnapshot(QbitConnectionState.Connected, chosen is not null, progress,
                    torrents.Count(QbitTorrentPicker.IsMoving)));
                var next = new QbitSent(chosen is not null, progress);
                var changed = sent is null ? next.Active : sent.Active != next.Active || (next.Active && sent.Progress != next.Progress);
                var keepalive = next.Active && DateTime.UtcNow - sentAt >= QbitKeepaliveEvery;
                if (changed || keepalive)
                {
                    switch (await SendQbitStatusAsync(next, token))
                    {
                        case QbitSendResult.Sent:
                            sent = next;
                            sentAt = DateTime.UtcNow;
                            break;
                        case QbitSendResult.Disabled:
                            // Switched off on the clock since it was last read: stop now
                            // rather than at the next read.
                            clockWanted = false;
                            sent = new QbitSent(false, 0);
                            sentAt = DateTime.UtcNow;
                            nextClockRead = DateTime.UtcNow + QbitClockReadEvery;
                            break;
                    }
                }

                await Task.Delay(QbitPollEvery, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            await DropQbitAsync();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendIdleAsync(timeout.Token);
            }
            catch
            {
                // The clock times the tile out by itself.
            }
            QBittorrentStatus.Set(QbitSnapshot.Off);
        }
    }

    // GET /qbitstate. "wanted" is whether the tile is switched on and in the
    // Tile Manager; a 404 is a firmware from before the tile.
    private async Task<QbitClockRead> ReadQbitClockAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/qbitstate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return QbitClockRead.Unsupported;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return QbitClockRead.Failed;
            }
            if (!response.IsSuccessStatusCode) return QbitClockRead.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var wanted = json.RootElement.TryGetProperty("wanted", out var w) && w.ValueKind == JsonValueKind.True;
            return wanted ? QbitClockRead.Wanted : QbitClockRead.NotWanted;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return QbitClockRead.Failed;
        }
    }

    // POST /qbitstatus: active, and the shown torrent's whole percent. Nothing
    // else about qBittorrent - no names, hashes, speeds or paths - is sent. The
    // clock answers DISABLED while it doesn't want the tile.
    private async Task<QbitSendResult> SendQbitStatusAsync(QbitSent status, CancellationToken token)
    {
        try
        {
            var form = status.Active
                ? new Dictionary<string, string> { ["active"] = "1", ["progress"] = status.Progress.ToString(CultureInfo.InvariantCulture) }
                : new Dictionary<string, string> { ["active"] = "0" };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/qbitstatus")
            {
                Content = new FormUrlEncodedContent(form)
            };
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return QbitSendResult.Failed;
            }
            if (!response.IsSuccessStatusCode) return QbitSendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? QbitSendResult.Disabled : QbitSendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return QbitSendResult.Failed;
        }
    }
}
