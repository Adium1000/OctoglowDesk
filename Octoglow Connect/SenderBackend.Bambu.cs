using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OctoglowSender;

internal enum BambuConnectionState { Off, StudioClosed, NoProgress, Connected, ClockUnsupported }
internal sealed record BambuSnapshot(BambuConnectionState Connection, string PrinterState, int Progress, bool HasError)
{
    public static readonly BambuSnapshot Off = new(BambuConnectionState.Off, "", 0, false);
}

internal static class BambuStatus
{
    private static BambuSnapshot _current = BambuSnapshot.Off;
    public static BambuSnapshot Current => Volatile.Read(ref _current);
    public static void Set(BambuSnapshot snapshot) => Volatile.Write(ref _current, snapshot);
}

public partial class SenderBackend
{
    private enum BambuClockRead { Wanted, NotWanted, Unsupported, Failed }
    private enum BambuSendResult { Sent, Disabled, Failed }
    private sealed record BambuSent(bool Active, int Progress, int Kind);

    private async Task BambuLoopAsync(CancellationToken token)
    {
        var clockWanted = false;
        var clockUnsupported = false;
        var nextClockRead = DateTime.MinValue;
        BambuSent? sent = null;
        var sentAt = DateTime.MinValue;
        var lastProgress = -1;
        var finishAlertUntil = DateTime.MinValue;

        async Task SendIdleAsync(CancellationToken sendToken)
        {
            if (sent is not { Active: true }) return;
            if (await SendBambuStatusAsync(new BambuSent(false, 0, 0), sendToken) != BambuSendResult.Failed)
                sent = new BambuSent(false, 0, 0);
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                if (now >= nextClockRead)
                {
                    var read = await ReadBambuClockAsync(token);
                    if (read is BambuClockRead.Wanted or BambuClockRead.NotWanted)
                    {
                        clockWanted = read == BambuClockRead.Wanted;
                        clockUnsupported = false;
                        nextClockRead = now.AddSeconds(3);
                    }
                    else if (read == BambuClockRead.Unsupported)
                    {
                        clockWanted = false;
                        clockUnsupported = true;
                        nextClockRead = now.AddSeconds(30);
                    }
                    else nextClockRead = now.AddSeconds(3);
                }

                if (!clockWanted)
                {
                    await SendIdleAsync(token);
                    BambuStatus.Set(BambuSnapshot.Off with
                    {
                        Connection = clockUnsupported ? BambuConnectionState.ClockUnsupported : BambuConnectionState.Off
                    });
                    lastProgress = -1;
                    finishAlertUntil = DateTime.MinValue;
                    await Task.Delay(1000, token);
                    continue;
                }

                BambuStudioWindowReader.Reading reading;
                try { reading = BambuStudioWindowReader.Read(); }
                catch { reading = new BambuStudioWindowReader.Reading(true, false, 0); }

                if (!reading.StudioRunning || !reading.HasProgress)
                {
                    await SendIdleAsync(token);
                    BambuStatus.Set(BambuSnapshot.Off with
                    {
                        Connection = reading.StudioRunning ? BambuConnectionState.NoProgress : BambuConnectionState.StudioClosed
                    });
                    lastProgress = -1;
                    finishAlertUntil = DateTime.MinValue;
                    await Task.Delay(1000, token);
                    continue;
                }

                var progress = reading.Progress;
                if (progress == 100 && lastProgress is >= 0 and < 100)
                    finishAlertUntil = now.AddSeconds(12);
                lastProgress = progress;

                var finished = progress == 100;
                var active = !finished || now < finishAlertUntil;
                var next = new BambuSent(active, progress, finished ? 2 : 0);
                BambuStatus.Set(new BambuSnapshot(BambuConnectionState.Connected,
                    finished ? "FINISH" : "RUNNING", progress, false));

                if (sent is null || sent != next || active && now - sentAt >= TimeSpan.FromSeconds(2))
                {
                    var result = await SendBambuStatusAsync(next, token);
                    if (result == BambuSendResult.Sent)
                    {
                        sent = next;
                        sentAt = now;
                    }
                    else if (result == BambuSendResult.Disabled)
                    {
                        clockWanted = false;
                        sent = new BambuSent(false, 0, 0);
                        nextClockRead = now.AddSeconds(3);
                    }
                }

                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendIdleAsync(timeout.Token);
            }
            catch { /* The firmware also expires the tile after six seconds. */ }
            BambuStatus.Set(BambuSnapshot.Off);
        }
    }

    private async Task<BambuClockRead> ReadBambuClockAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/bambustate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return BambuClockRead.Unsupported;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return BambuClockRead.Failed;
            }
            if (!response.IsSuccessStatusCode) return BambuClockRead.Failed;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return json.RootElement.TryGetProperty("wanted", out var wanted) && wanted.ValueKind == JsonValueKind.True
                ? BambuClockRead.Wanted : BambuClockRead.NotWanted;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return BambuClockRead.Failed; }
    }

    private async Task<BambuSendResult> SendBambuStatusAsync(BambuSent status, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/bambustatus")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["active"] = status.Active ? "1" : "0",
                    ["progress"] = status.Progress.ToString(CultureInfo.InvariantCulture),
                    ["kind"] = status.Kind.ToString(CultureInfo.InvariantCulture)
                })
            };
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return BambuSendResult.Failed;
            }
            if (!response.IsSuccessStatusCode) return BambuSendResult.Failed;
            return (await response.Content.ReadAsStringAsync(timeout.Token)).Trim() == "DISABLED"
                ? BambuSendResult.Disabled : BambuSendResult.Sent;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return BambuSendResult.Failed; }
    }
}
