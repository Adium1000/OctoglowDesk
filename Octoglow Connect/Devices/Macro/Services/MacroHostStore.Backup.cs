using System.Text.Json;

namespace OctoglowSender.Devices.Macro.Services;

internal static partial class MacroHostStore
{
    internal static MacroHostSettings Snapshot()
    {
        lock (Gate) return JsonSerializer.Deserialize(
            JsonSerializer.Serialize(Settings, MacroHostJson.Default.MacroHostSettings),
            MacroHostJson.Default.MacroHostSettings)!;
    }

    internal static void Restore(MacroHostSettings replacement)
    {
        MacroBackup.ValidateHost(replacement);
        lock (Gate)
        {
            var previous = Settings;
            // Deep copy: callers cannot mutate the live store after validation.
            _settings = JsonSerializer.Deserialize(JsonSerializer.Serialize(replacement,
                MacroHostJson.Default.MacroHostSettings), MacroHostJson.Default.MacroHostSettings)!;
            try { Persist(); }
            catch { _settings = previous; throw; }
        }
    }

    internal static MacroHostApplication AddClock(DeviceDescriptor device, string command)
    {
        if (!AllowOtherOctoglowDevices || device.Kind != DeviceKind.DeskClock ||
            !MacroClockControl.Commands.Any(item => item.Id == command))
            throw new InvalidOperationException(AppStrings.IsEnglish ? "Enable the Octoglow integration first." : "Activează mai întâi integrarea Octoglow.");
        lock (Gate)
        {
            var existing = Settings.Applications.FirstOrDefault(a => a.ClockDeviceId == device.Id && a.ClockCommand == command);
            if (existing is not null) return existing.Clone();
            var next = Math.Max(Settings.NextApplicationId, Settings.Applications.Select(a => a.Id).DefaultIfEmpty(0).Max() + 1);
            if (next > ushort.MaxValue) throw new InvalidOperationException("No free host action IDs remain.");
            var entry = new MacroHostApplication { Id = next, Name = device.DisplayName,
                ClockDeviceId = device.Id, ClockCommand = command };
            var previousNext = Settings.NextApplicationId;
            Settings.Applications.Add(entry);
            Settings.NextApplicationId = next + 1;
            try { Persist(); }
            catch { Settings.Applications.Remove(entry); Settings.NextApplicationId = previousNext; throw; }
            return entry.Clone();
        }
    }
}
