namespace OctoglowSender.Devices.Macro.Models;

// These numeric values are shared by the V4/V5/V6 serial protocols. Keep them stable.
internal enum MacroActionType
{
    None = 0,
    Key = 1,
    Media = 2,
    Scroll = 3,
    NextTab = 4,
    PreviousTab = 5,
    GoToTab = 6,
    Application = 7,
}

internal readonly record struct MacroAction(
    MacroActionType Type, int Modifiers = 0, int Key1 = 0, int Key2 = 0, int Value = 0);

internal readonly record struct MacroRgbColor(byte Red, byte Green, byte Blue)
{
    public static MacroRgbColor Default => new(36, 171, 255);
}

internal enum MacroLedOutputMode
{
    Combined = 0,
    AddressableOnly = 1,
    ExternalOnly = 2,
}

internal sealed class MacroConfiguration
{
    public const int MaxTabs = 5;
    public const int ToneEvents = 18;
    public const int CustomTonePacks = 5;
    public bool SupportsNamedTonePacks { get; set; }
    private readonly string[] _customToneNames = new string[CustomTonePacks];
    public string GetCustomToneName(int pack) => _customToneNames[CheckedTab(pack)] ?? "";
    public void SetCustomToneName(int pack, string name) => _customToneNames[CheckedTab(pack)] = name;
    public bool IsToneNameAvailable(int pack, string name)
    {
        var reserved = new[] { "Classic", "Clasic", "Soft", "Delicat", "Bright", "Luminos", "Arcade", "Low", "Grav" };
        return !reserved.Concat(Enumerable.Range(1, CustomTonePacks).Where(p => p != pack)
                .Select(GetCustomToneName))
            .Any(existing => string.Equals(existing.Normalize(System.Text.NormalizationForm.FormC),
                name.Normalize(System.Text.NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase));
    }
    public bool HasAddressableLighting => LightingEnabled && LedOutputMode != MacroLedOutputMode.ExternalOnly;
    public bool HasExternalLighting => LightingEnabled && LedOutputMode != MacroLedOutputMode.AddressableOnly;
    public static readonly string[] Slots = ["b1", "b2", "b3", "b4", "b5", "b6", "b7", "knob", "cw", "ccw"];

    public int TabCount { get; set; } = 1;
    public int ActiveTab { get; set; } = 1;
    public int LedEffect { get; set; }
    public bool LightingEnabled { get; set; } = true;
    public int TonePack { get; set; }
    public int LedBrightness { get; set; } = 100;
    public int AddressableBrightness { get; set; } = 100;
    public bool SupportsSplitLighting { get; set; }
    public bool SupportsTabPersonalization { get; set; }
    public bool HasEncoder { get; set; } = true;
    public MacroLedOutputMode LedOutputMode { get; set; } = MacroLedOutputMode.Combined;
    public bool BuzzerEnabled { get; set; } = true;
    public int BuzzerVolume { get; set; } = 100;
    private readonly MacroAction[,] _actions = new MacroAction[MaxTabs, 10];
    private readonly MacroRgbColor[] _tabColors =
        Enumerable.Repeat(MacroRgbColor.Default, MaxTabs).ToArray();
    private readonly int[] _tabEffects = new int[MaxTabs];
    private readonly int[] _tabRgbEffects = new int[MaxTabs];
    private readonly int[] _tabTonePacks = new int[MaxTabs];
    private readonly int[] _tabIcons = new int[MaxTabs];
    private readonly string[] _tabNames = new string[MaxTabs];
    private readonly int[,] _customTones = new int[CustomTonePacks, ToneEvents];

    public int GetTabTonePack(int tab) => _tabTonePacks[CheckedTab(tab)];
    public void SetTabTonePack(int tab, int pack)
    {
        if (pack is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(pack));
        _tabTonePacks[CheckedTab(tab)] = pack;
    }

    public int GetTabIcon(int tab) => _tabIcons[CheckedTab(tab)];
    public string GetTabName(int tab) => _tabNames[CheckedTab(tab)] ?? "";
    public string GetTabDisplayName(int tab) => string.IsNullOrWhiteSpace(GetTabName(tab))
        ? $"Tab {tab}" : GetTabName(tab);
    public void SetTabIdentity(int tab, string name, int icon)
    {
        if (icon is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(icon));
        _tabNames[CheckedTab(tab)] = name;
        _tabIcons[tab - 1] = icon;
    }

    private static int CheckedTab(int tab) => tab is >= 1 and <= MaxTabs
        ? tab - 1 : throw new ArgumentOutOfRangeException(nameof(tab));

    public void DeleteTab(int tab)
    {
        if (TabCount <= 1 || tab is < 1 || tab > TabCount) throw new ArgumentOutOfRangeException(nameof(tab));
        for (var index = tab - 1; index < TabCount - 1; index++)
        {
            for (var slot = 0; slot < Slots.Length; slot++) _actions[index, slot] = _actions[index + 1, slot];
            _tabColors[index] = _tabColors[index + 1];
            _tabEffects[index] = _tabEffects[index + 1];
            _tabRgbEffects[index] = _tabRgbEffects[index + 1];
            _tabTonePacks[index] = _tabTonePacks[index + 1];
            _tabIcons[index] = _tabIcons[index + 1];
            _tabNames[index] = _tabNames[index + 1];
        }
        var last = --TabCount;
        for (var slot = 0; slot < Slots.Length; slot++) _actions[last, slot] = default;
        _tabColors[last] = MacroRgbColor.Default;
        _tabEffects[last] = 0;
        _tabRgbEffects[last] = 0;
        _tabTonePacks[last] = 0;
        _tabIcons[last] = 0;
        _tabNames[last] = "";
        for (var index = 0; index < TabCount; index++)
            for (var slot = 0; slot < Slots.Length; slot++)
            {
                var action = _actions[index, slot];
                if (action.Type != MacroActionType.GoToTab) continue;
                _actions[index, slot] = action.Key1 == tab
                    ? new MacroAction(MacroActionType.NextTab)
                    : action.Key1 > tab ? action with { Key1 = action.Key1 - 1 } : action;
            }
        ActiveTab = ActiveTab > tab ? ActiveTab - 1 : Math.Min(ActiveTab, TabCount);
    }

    public int GetTabEffect(int tab) =>
        tab is >= 1 and <= MaxTabs ? _tabEffects[tab - 1] : throw new ArgumentOutOfRangeException(nameof(tab));

    public int GetTabRgbEffect(int tab) => _tabRgbEffects[CheckedTab(tab)];
    public void SetTabRgbEffect(int tab, int effect)
    {
        if (effect is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(effect));
        _tabRgbEffects[CheckedTab(tab)] = effect;
    }

    public void SetTabEffect(int tab, int effect)
    {
        if (tab is < 1 or > MaxTabs || effect is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(tab));
        _tabEffects[tab - 1] = effect;
    }

    public int GetCustomTone(int pack, int toneEvent) =>
        pack is >= 1 and <= CustomTonePacks && toneEvent is >= 0 and < ToneEvents
            ? _customTones[pack - 1, toneEvent] : throw new ArgumentOutOfRangeException(nameof(pack));

    public void SetCustomTone(int pack, int toneEvent, int frequency)
    {
        if (pack is < 1 or > CustomTonePacks || toneEvent is < 0 or >= ToneEvents || frequency is < 0 or > 8000)
            throw new ArgumentOutOfRangeException(nameof(pack));
        _customTones[pack - 1, toneEvent] = frequency;
    }

    public MacroRgbColor GetTabColor(int tab) =>
        tab is >= 1 and <= MaxTabs ? _tabColors[tab - 1] : throw new ArgumentOutOfRangeException(nameof(tab));

    public void SetTabColor(int tab, MacroRgbColor color)
    {
        if (tab is < 1 or > MaxTabs) throw new ArgumentOutOfRangeException(nameof(tab));
        _tabColors[tab - 1] = color;
    }

    public MacroAction Get(int tab, string slot)
    {
        var index = Array.IndexOf(Slots, slot);
        return tab is >= 1 and <= MaxTabs && index >= 0 ? _actions[tab - 1, index] : default;
    }

    public void Set(int tab, string slot, MacroAction action)
    {
        var index = Array.IndexOf(Slots, slot);
        if (tab is < 1 or > MaxTabs || index < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        _actions[tab - 1, index] = action;
    }
}
