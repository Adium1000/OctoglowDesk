using System.Text.Json;
using System.Text.Json.Serialization;

namespace OctoglowSender.Devices.Macro.Services;

internal sealed class MacroHostApplication
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string ClockDeviceId { get; set; } = "";
    public string ClockCommand { get; set; } = "";
    [JsonIgnore] public bool IsClock => !string.IsNullOrEmpty(ClockDeviceId);
    internal MacroHostApplication Clone() => new() { Id = Id, Name = Name, Path = Path,
        ClockDeviceId = ClockDeviceId, ClockCommand = ClockCommand };
}

internal sealed class MacroHostTabAppearance
{
    public string Name { get; set; } = "";
    public int Icon { get; set; }
}

internal sealed class MacroHostSettings
{
    public int NextApplicationId { get; set; } = 1;
    public List<MacroHostApplication> Applications { get; set; } = [];
    public bool AllowOtherOctoglowDevices { get; set; }
    public int LastTabCount { get; set; } = 1;
    public List<MacroHostTabAppearance> TabAppearances { get; set; } = [];
    public bool PendingTabAppearanceSync { get; set; }
    // Per USB identity. Null = no override; empty = a deleted, cleared preset.
    public Dictionary<string, string?[]> ToneNames { get; set; } = [];
}

// Only IDs travel to the firmware. Executable paths are never sent over USB.
internal static partial class MacroHostStore
{
    private static readonly object Gate = new();
    internal static string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OctoglowSender", "macro-host.json");
    private static MacroHostSettings? _settings;

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(MacroHostSettings))]
    private sealed partial class MacroHostJson : JsonSerializerContext { }

    private static MacroHostSettings Settings => _settings ??= Load();

    private static MacroHostSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), MacroHostJson.Default.MacroHostSettings)
                  ?? new MacroHostSettings()
                : new MacroHostSettings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MacroHostSettings();
        }
    }

    private static void Persist()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(Settings, MacroHostJson.Default.MacroHostSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    internal static IReadOnlyList<MacroHostApplication> Applications
    {
        get
        {
            lock (Gate)
                return Settings.Applications.Where(a => !a.IsClock).Select(a => a.Clone()).ToArray();
        }
    }

    internal static string?[] GetToneNames(string deviceKey)
    {
        lock (Gate)
        {
            var result = new string?[5];
            if (Settings.ToneNames.TryGetValue(deviceKey, out var saved))
                Array.Copy(saved, result, Math.Min(5, saved.Length));
            return result;
        }
    }

    internal static void SetToneName(string deviceKey, int pack, string? name)
    {
        if (string.IsNullOrEmpty(deviceKey) || pack is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(pack));
        lock (Gate)
        {
            var existed = Settings.ToneNames.TryGetValue(deviceKey, out var previous);
            var updated = GetToneNames(deviceKey);
            updated[pack - 1] = name;
            Settings.ToneNames[deviceKey] = updated;
            try { Persist(); }
            catch
            {
                if (existed) Settings.ToneNames[deviceKey] = previous!;
                else Settings.ToneNames.Remove(deviceKey);
                throw;
            }
        }
    }

    internal static bool AllowOtherOctoglowDevices
    {
        get { lock (Gate) return Settings.AllowOtherOctoglowDevices; }
        set
        {
            lock (Gate)
            {
                if (Settings.AllowOtherOctoglowDevices == value) return;
                var previous = Settings.AllowOtherOctoglowDevices;
                Settings.AllowOtherOctoglowDevices = value;
                try { Persist(); }
                catch { Settings.AllowOtherOctoglowDevices = previous; throw; }
            }
        }
    }

    internal static int LastTabCount
    {
        get { lock (Gate) return Math.Clamp(Settings.LastTabCount, 1, 5); }
    }

    internal static bool PendingTabAppearanceSync
    {
        get { lock (Gate) return Settings.PendingTabAppearanceSync; }
    }

    internal static IReadOnlyList<MacroHostTabAppearance> TabAppearances
    {
        get
        {
            lock (Gate)
                return Settings.TabAppearances.Select(tab => new MacroHostTabAppearance
                    { Name = tab.Name, Icon = tab.Icon }).ToArray();
        }
    }

    internal static void SetTabAppearance(int tab, string name, int icon, bool needsFirmwareSync)
    {
        if (tab is < 1 or > 5 || icon is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(tab));
        lock (Gate)
        {
            var settings = Settings;
            var previousCount = settings.LastTabCount;
            var previousTabs = settings.TabAppearances.Select(item => new MacroHostTabAppearance
                { Name = item.Name, Icon = item.Icon }).ToList();
            var previousPending = settings.PendingTabAppearanceSync;
            settings.LastTabCount = Math.Max(settings.LastTabCount, tab);
            while (settings.TabAppearances.Count < tab)
                settings.TabAppearances.Add(new MacroHostTabAppearance());
            settings.TabAppearances[tab - 1] = new MacroHostTabAppearance { Name = name, Icon = icon };
            settings.PendingTabAppearanceSync |= needsFirmwareSync;
            try { Persist(); }
            catch
            {
                settings.LastTabCount = previousCount;
                settings.TabAppearances = previousTabs;
                settings.PendingTabAppearanceSync = previousPending;
                throw;
            }
        }
    }

    internal static void RememberTabCount(int count)
    {
        lock (Gate)
        {
            var previous = Settings.LastTabCount;
            Settings.LastTabCount = Math.Clamp(count, 1, 5);
            try { Persist(); }
            catch { Settings.LastTabCount = previous; throw; }
        }
    }

    internal static void RemoveTabAppearance(int tab, int remainingCount)
    {
        lock (Gate)
        {
            var settings = Settings;
            var previousCount = settings.LastTabCount;
            var previousTabs = settings.TabAppearances.Select(item => new MacroHostTabAppearance
                { Name = item.Name, Icon = item.Icon }).ToList();
            if (tab >= 1 && tab <= settings.TabAppearances.Count)
                settings.TabAppearances.RemoveAt(tab - 1);
            settings.LastTabCount = Math.Clamp(remainingCount, 1, 5);
            try { Persist(); }
            catch { settings.TabAppearances = previousTabs; settings.LastTabCount = previousCount; throw; }
        }
    }

    internal static void MoveTabAppearance(int from, int to)
    {
        lock (Gate)
        {
            var settings = Settings;
            if (from < 1 || to < 1 || from > settings.LastTabCount || to > settings.LastTabCount)
                throw new ArgumentOutOfRangeException(nameof(from));
            if (from == to) return;
            var previous = settings.TabAppearances.Select(item => new MacroHostTabAppearance
                { Name = item.Name, Icon = item.Icon }).ToList();
            while (settings.TabAppearances.Count < settings.LastTabCount)
                settings.TabAppearances.Add(new MacroHostTabAppearance());
            var moved = settings.TabAppearances[from - 1];
            settings.TabAppearances.RemoveAt(from - 1);
            settings.TabAppearances.Insert(to - 1, moved);
            try { Persist(); }
            catch { settings.TabAppearances = previous; throw; }
        }
    }

    internal static void RememberFirmwareTabAppearances(Models.MacroConfiguration config)
    {
        lock (Gate)
        {
            var settings = Settings;
            settings.LastTabCount = config.TabCount;
            settings.TabAppearances = Enumerable.Range(1, config.TabCount)
                .Select(tab => new MacroHostTabAppearance
                    { Name = config.GetTabName(tab), Icon = config.GetTabIcon(tab) }).ToList();
            settings.PendingTabAppearanceSync = false;
            Persist();
        }
    }

    internal static MacroHostApplication? Find(int id)
    {
        lock (Gate)
        {
            var app = Settings.Applications.FirstOrDefault(a => a.Id == id);
            return app?.Clone();
        }
    }

    internal static MacroHostApplication Add(string name, string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) ||
            !new[] { ".exe", ".lnk" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an existing .exe or .lnk file.", nameof(path));
        lock (Gate)
        {
            // Never reuse a deleted ID: old firmware mappings must not start a
            // different application if their original host entry is removed.
            var next = Math.Max(Settings.NextApplicationId,
                Settings.Applications.Count == 0 ? 1 : Settings.Applications.Max(app => app.Id) + 1);
            if (next > ushort.MaxValue) throw new InvalidOperationException("No free application IDs remain.");
            var app = new MacroHostApplication { Id = next, Name = name.Trim(), Path = path };
            Settings.Applications.Add(app);
            Settings.NextApplicationId = next + 1;
            Persist();
            return app;
        }
    }

    internal static void Remove(int id)
    {
        lock (Gate)
        {
            Settings.Applications.RemoveAll(app => app.Id == id);
            Persist();
        }
    }
}
