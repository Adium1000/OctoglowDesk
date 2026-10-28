using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OctoglowSender.Devices.Macro.Models;

namespace OctoglowSender.Devices.Macro.Services;

internal sealed class MacroBackupDocument
{
    public string Format { get; set; } = "octoglow-macro-backup";
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string FirmwareVersion { get; set; } = "";
    public string DeviceKey { get; set; } = "";
    public double PreviewFraction { get; set; }
    public List<string> DeviceConfiguration { get; set; } = [];
    public MacroHostSettings Host { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MacroBackupDocument))]
internal sealed partial class MacroBackupJson : JsonSerializerContext { }

internal static class MacroBackup
{
    internal const int MaxBytes = 2 * 1024 * 1024;
    internal static string Encode(MacroBackupDocument backup) =>
        JsonSerializer.Serialize(backup, MacroBackupJson.Default.MacroBackupDocument);

    internal static MacroBackupDocument Decode(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("Backup is too large.");
        var backup = JsonSerializer.Deserialize(text, MacroBackupJson.Default.MacroBackupDocument)
            ?? throw new InvalidDataException("Empty backup.");
        if (backup.Format != "octoglow-macro-backup" || backup.Version != 1 || backup.DeviceConfiguration is null ||
            backup.DeviceConfiguration.Count > 256 || backup.DeviceConfiguration.Any(l => l is null || l.Length > 256) ||
            backup.DeviceKey is null || backup.DeviceKey.Length > 256 ||
            !double.IsFinite(backup.PreviewFraction) || backup.PreviewFraction is < 0 or > 0.85)
            throw new InvalidDataException("Unsupported or malformed Macro backup.");
        var model = MacroConfigurationParser.Parse(backup.DeviceConfiguration);
        ValidateHost(backup.Host);
        // Use the same name validation as the firmware before any USB write.
        var names = new HashSet<string>(["Classic", "Clasic", "Soft", "Delicat", "Bright", "Luminos", "Arcade", "Low", "Grav"], StringComparer.OrdinalIgnoreCase);
        for (var pack = 1; pack <= 5; pack++)
        {
            var name = model.GetCustomToneName(pack);
            if (name.Length != 0 && (!ValidName(name, 24) || !names.Add(name)))
                throw new InvalidDataException("Invalid or duplicate custom tone name.");
        }
        for (var tab = 1; tab <= 5; tab++)
            if (model.GetTabName(tab).Length != 0 && !ValidName(model.GetTabName(tab), 24))
                throw new InvalidDataException("Invalid tab name.");
        return backup;
    }

    private static bool ValidName(string name, int bytes) => name.Trim() == name &&
        !name.Any(char.IsControl) && Encoding.UTF8.GetByteCount(name) <= bytes;

    internal static void ValidateHost(MacroHostSettings host)
    {
        if (host is null || host.Applications is null || host.Applications.Count > 4096 ||
            host.TabAppearances is null || host.TabAppearances.Count > 5 || host.ToneNames is null || host.ToneNames.Count > 128 ||
            host.LastTabCount is < 1 or > 5 || host.NextApplicationId is < 1 or > 65536)
            throw new InvalidDataException("Invalid host settings.");
        var ids = new HashSet<int>();
        foreach (var app in host.Applications)
        {
            if (app is null || app.Id is < 1 or > 65535 || !ids.Add(app.Id) || app.Id >= host.NextApplicationId ||
                app.Name is null || app.Name.Length > 256 || app.Path is null || app.Path.Length > 32767 ||
                app.ClockDeviceId is null || app.ClockDeviceId.Length > 256 || app.ClockCommand is null)
                throw new InvalidDataException("Invalid host action.");
            if (app.IsClock)
            {
                if (app.Path.Length != 0 || !app.ClockDeviceId.StartsWith("clock:", StringComparison.Ordinal) ||
                    !MacroClockControl.Commands.Any(c => c.Id == app.ClockCommand))
                    throw new InvalidDataException("Unsupported Clock action.");
            }
            else if (!Path.IsPathFullyQualified(app.Path) || app.ClockCommand.Length != 0 ||
                !new[] { ".exe", ".lnk" }.Contains(Path.GetExtension(app.Path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Only absolute .exe and .lnk application paths are supported.");
        }
        foreach (var tab in host.TabAppearances)
            if (tab is null || tab.Name is null || !ValidName(tab.Name, 24) || tab.Icon is < 0 or > 9)
                throw new InvalidDataException("Invalid local tab appearance.");
        foreach (var pair in host.ToneNames)
            if (pair.Key.Length > 256 || pair.Value is null || pair.Value.Length != 5 ||
                pair.Value.Any(n => n is not null && !ValidName(n, 24)))
                throw new InvalidDataException("Invalid local tone names.");
    }

    // Reconstruct commands from validated values, never execute lines from a file.
    internal static MacroConfiguration PrepareRestore(MacroBackupDocument backup)
    {
        var config = MacroConfigurationParser.Parse(backup.DeviceConfiguration);
        // Older boards kept names/icons only on the host. Preserve their effective appearance.
        if (!config.SupportsTabPersonalization || backup.Host.PendingTabAppearanceSync)
            for (var i = 0; i < backup.Host.TabAppearances.Count; i++)
                config.SetTabIdentity(i + 1, backup.Host.TabAppearances[i].Name, backup.Host.TabAppearances[i].Icon);
        if (!config.SupportsNamedTonePacks)
        {
            backup.Host.ToneNames.TryGetValue(backup.DeviceKey, out var names);
            for (var pack = 1; pack <= 5; pack++)
            {
                var name = names?[pack - 1];
                var used = config.TonePack == pack + 4 || Enumerable.Range(1, 5).Any(t => config.GetTabTonePack(t) == pack + 4) ||
                    Enumerable.Range(0, 18).Any(t => config.GetCustomTone(pack, t) != 0);
                config.SetCustomToneName(pack, name == "" && !used ? "" : string.IsNullOrEmpty(name) ? $"Custom {pack}" : name);
            }
        }
        return config;
    }

    internal static IReadOnlyList<string> RestoreCommands(MacroConfiguration c)
    {
        static string Hex(string s) => s.Length == 0 ? "-" : Convert.ToHexString(Encoding.UTF8.GetBytes(s));
        // LedEffect is a legacy cache of the active tab; old firmware can report a stale value.
        var result = new List<string> { "BUZZER OFF", "SETTABCOUNT 5", $"ACTIVE {c.ActiveTab}", $"LED {c.GetTabEffect(c.ActiveTab)}", $"TONEPACK {c.TonePack}" };
        for (var pack = 1; pack <= 5; pack++) result.Add($"DELPACK {pack}");
        for (var pack = 1; pack <= 5; pack++)
        {
            if (c.GetCustomToneName(pack).Length != 0) result.Add($"PACKNAME {pack} {Hex(c.GetCustomToneName(pack))}");
            for (var tone = 0; tone < 18; tone++) result.Add($"TONESET {pack} {tone} {c.GetCustomTone(pack, tone)}");
        }
        // DELPACK can reset the global selection. Restore it after clearing all packs.
        result.Add($"TONEPACK {c.TonePack}");
        for (var tab = 1; tab <= 5; tab++)
        {
            var rgb = c.GetTabColor(tab);
            result.Add($"TABRGB {tab} {rgb.Red} {rgb.Green} {rgb.Blue}");
            result.Add($"EXTFX {tab} {c.GetTabEffect(tab)}");
            result.Add($"RGBFX {tab} {c.GetTabRgbEffect(tab)}");
            result.Add($"TABMETA {tab} {c.GetTabIcon(tab)} {Hex(c.GetTabName(tab))}");
            result.Add($"TABTONE {tab} {c.GetTabTonePack(tab)}");
            foreach (var slot in MacroConfiguration.Slots)
            {
                var a = c.Get(tab, slot);
                result.Add($"SETMAP {tab} {slot} {(int)a.Type} {a.Modifiers} {a.Key1} {a.Key2} {a.Value}");
            }
        }
        result.AddRange([$"SETTABCOUNT {c.TabCount}", $"ACTIVE {c.ActiveTab}",
            $"LEDMODE {(int)c.LedOutputMode}", $"RGBBRIGHT {c.AddressableBrightness}", $"EXTBRIGHT {c.LedBrightness}",
            $"LIGHT {(c.LightingEnabled ? "ON" : "OFF")}", $"BUZZERVOL {c.BuzzerVolume}", $"BUZZER {(c.BuzzerEnabled ? "ON" : "OFF")}"]);
        return result;
    }
}
