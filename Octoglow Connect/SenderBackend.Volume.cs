using System.Globalization;
using System.Threading.Channels;

namespace OctoglowSender;

public partial class SenderBackend
{
    private async Task VolumeLoopAsync(CancellationToken token)
    {
        // A bounded latest-value mailbox prevents a slow clock from replaying
        // old slider positions. Reads continue while a request is in flight.
        var changes = Channel.CreateBounded<(AudioCapture.MasterVolume Value, long At)>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var reader = Task.Run(async () =>
        {
            AudioCapture.MasterVolume? previous = null;
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    var current = AudioCapture.ReadMasterVolume(previous);
                    if (current is { } value && previous is { } before &&
                        value.Endpoint == before.Endpoint &&
                        (value.Percent != before.Percent || value.Muted != before.Muted))
                        changes.Writer.TryWrite((value, Environment.TickCount64));
                    // Startup, unplug/replug and default-device changes establish
                    // a baseline, rather than pretending the user moved a slider.
                    previous = current;
                    await Task.Delay(100, lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally { changes.Writer.TryComplete(); }
        }, lifetime.Token);
        try
        {
            await foreach (var change in changes.Reader.ReadAllAsync(token))
            {
                if (Environment.TickCount64 - change.At > 1000) continue;
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(1));
                    var value = change.Value;
                    using var response = await PostAuthenticatedAsync("/volumestatus", new()
                    {
                        ["pct"] = value.Percent.ToString(CultureInfo.InvariantCulture),
                        ["mute"] = value.Muted ? "1" : "0",
                        ["source"] = value.Source.ToString(CultureInfo.InvariantCulture),
                    }, deadline.Token);
                    if ((int)response.StatusCode == 404) break; // older firmware: stop until reconnect
                    if ((int)response.StatusCode == 401)
                        await LoginAsync(token, retries: 1, delaySeconds: 1);
                    // Never retry a stale transient event. The next change uses
                    // the renewed session; unlike state, it is not a keepalive.
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception) { /* next change recovers; no idle network traffic */ }
            }
        }
        finally
        {
            lifetime.Cancel();
            try { await reader; } catch (OperationCanceledException) { }
        }
    }
}
