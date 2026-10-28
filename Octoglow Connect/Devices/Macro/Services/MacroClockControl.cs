namespace OctoglowSender.Devices.Macro.Services;

/// <summary>Allowlisted host actions; never accepts a URL or credentials from a USB event/backup.</summary>
internal static class MacroClockControl
{
    internal static readonly (string Id, string Ro, string En)[] Commands = [
        ("screen-on", "Pornește ecranul", "Turn screen on"),
        ("screen-off", "Oprește ecranul", "Turn screen off"),
        ("screen-toggle", "Comută ecranul", "Toggle screen"),
        ("buzzer-on", "Pornește buzzerul", "Unmute buzzer"),
        ("buzzer-off", "Oprește buzzerul", "Mute buzzer"),
        ("buzzer-toggle", "Comută buzzerul", "Toggle buzzer"),
    ];
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static string Label(string command)
    {
        var item = Commands.FirstOrDefault(c => c.Id == command);
        return (AppStrings.IsEnglish ? item.En : item.Ro) ?? command;
    }
    internal static IReadOnlyList<DeviceDescriptor> Devices => App.DeviceHub?.Devices
        .Where(d => d.Kind == DeviceKind.DeskClock).ToArray() ?? [];

    internal static async Task ExecuteAsync(MacroHostApplication entry)
    {
        // Drop excess presses rather than retaining stale commands for an offline clock.
        if (!MacroHostStore.AllowOtherOctoglowDevices || !await Gate.WaitAsync(0)) return;
        try
        {
            if (!Commands.Any(c => c.Id == entry.ClockCommand)) return;
            var device = Devices.FirstOrDefault(d => d.Id == entry.ClockDeviceId);
            if (device is null) throw new InvalidOperationException(AppStrings.IsEnglish ?
                "The assigned clock is no longer saved in Connect. Select it again in the key editor." :
                "Ceasul atribuit nu mai este salvat în Connect. Selectează-l din nou în configurarea tastei.");
            // Connect currently has one signed-in Clock account. Never reuse its password on another endpoint.
            var config = ConfigStore.Load();
            if (!DeskClockDiscoveryService.TryNormalizeConfiguredAddress(config.Esp32Ip, config.Esp32Port, out var saved) ||
                DeviceDescriptor.DeskClock(saved.Address, saved.Port, false, "").Id != device.Id)
                throw new InvalidOperationException(AppStrings.IsEnglish ? "Sign in to this clock in Connect first." : "Conectează mai întâi acest ceas în Connect.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var command = entry.ClockCommand;
            if (command.EndsWith("toggle", StringComparison.Ordinal))
            {
                var state = await ClockDeviceApi.GetConfiguredStateAsync(device, timeout.Token);
                command = command.StartsWith("screen", StringComparison.Ordinal)
                    ? (state.Bool("sleeping") ? "screen-on" : "screen-off")
                    : (state.Bool("buzzer") ? "buzzer-off" : "buzzer-on");
            }
            if (!MacroHostStore.AllowOtherOctoglowDevices) return;
            var screen = command.StartsWith("screen", StringComparison.Ordinal);
            await ClockDeviceApi.PostConfiguredAsync(device, screen ? "/power" : "/settings",
                new Dictionary<string, string> { [screen ? "action" : "buzzer"] = screen
                    ? (command == "screen-on" ? "wake" : "sleep") : (command == "buzzer-on" ? "1" : "0") }, timeout.Token);
        }
        catch (Exception error) { AppLog.Write($"[MACRO CLOCK] {Label(entry.ClockCommand)}: {error.Message}"); }
        finally { Gate.Release(); }
    }
}
