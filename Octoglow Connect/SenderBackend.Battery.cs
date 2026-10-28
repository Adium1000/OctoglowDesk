using System.Globalization;
using System.Runtime.InteropServices;

namespace OctoglowSender;

public partial class SenderBackend
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private async Task BatteryLoopAsync(CancellationToken token)
    {
        var firmwareSupported = true;
        while (!token.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(firmwareSupported ? 15 : 60);
            try
            {
                // Windows can briefly report an unknown battery flag/percentage
                // while the power source changes. Do not turn that transient
                // reading into "no battery" on the clock.
                if (GetSystemPowerStatus(out var power) && power.BatteryFlag != 255 &&
                    ((power.BatteryFlag & 128) != 0 || power.BatteryLifePercent <= 100))
                {
                    var present = (power.BatteryFlag & 128) == 0;
                    var percent = present && power.BatteryLifePercent <= 100
                        ? (int)power.BatteryLifePercent : -1;
                    var payload = new Dictionary<string, string>
                    {
                        ["pct"] = percent.ToString(CultureInfo.InvariantCulture),
                        ["present"] = present ? "1" : "0",
                        ["ac"] = power.ACLineStatus == 1 ? "1" : "0",
                        ["charging"] = (power.BatteryFlag & 8) != 0 ? "1" : "0",
                    };
                    using var response = await PostAuthenticatedAsync("/batterystatus", payload, token);
                    if ((int)response.StatusCode == 401)
                    {
                        if (await LoginAsync(token, retries: 3, delaySeconds: 2))
                        {
                            using var retry = await PostAuthenticatedAsync("/batterystatus", payload, token);
                            if ((int)retry.StatusCode == 404) firmwareSupported = false;
                            else if (retry.IsSuccessStatusCode) firmwareSupported = true;
                        }
                    }
                    if ((int)response.StatusCode == 404)
                        firmwareSupported = false;
                    else if (response.IsSuccessStatusCode)
                        firmwareSupported = true;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Network outages are normal; other integrations already report
                // connection health. Never log each battery poll.
            }
            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) { break; }
        }
    }
}
