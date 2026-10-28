using System.Globalization;
using System.Text;
using OctoglowSender.Devices.Macro.Models;

namespace OctoglowSender.Devices.Macro.Services;

internal static class MacroConfigurationParser
{
    internal static MacroConfiguration Parse(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2 || lines[^1] != "END")
            throw new InvalidDataException("The Macro configuration is incomplete.");
        var header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!((header.Length == 6 && header[1] == "4") ||
              (header.Length == 7 && header[1] == "5") ||
              (header.Length == 8 && header[1] == "6") ||
              (header.Length == 11 && header[1] == "7") ||
              (header.Length == 12 && header[1] is "8" or "9" or "10" or "11" or "12")) ||
            header[0] != "CFG" || header[5] is not ("0" or "1") ||
            !int.TryParse(header[2], out var count) || count is < 1 or > MacroConfiguration.MaxTabs ||
            !int.TryParse(header[3], out var active) || active is < 1 or > MacroConfiguration.MaxTabs || active > count ||
            !int.TryParse(header[4], out var led) || led is < 0 or > 7 ||
            (header.Length >= 7 && (!int.TryParse(header[6], out var volume) || volume is < 0 or > 100)) ||
            (header.Length >= 8 && (!int.TryParse(header[7], out var mode) || mode is < 0 or > 2)) ||
            (header.Length >= 11 &&
                (header[8] is not ("0" or "1") ||
                 !int.TryParse(header[9], out var pack) || pack is < 0 or > 9 ||
                 header[10] is not ("0" or "1"))) ||
            (header.Length == 12 && (!int.TryParse(header[11], out var brightness) || brightness is < 0 or > 100)))
            throw new InvalidDataException("The Macro returned an invalid configuration header.");
        var config = new MacroConfiguration
        {
            TabCount = count, ActiveTab = active, LedEffect = led, BuzzerEnabled = header[5] == "1",
            BuzzerVolume = header.Length >= 7 ? int.Parse(header[6], CultureInfo.InvariantCulture) : 100,
            LedOutputMode = header.Length >= 8 ?
                (MacroLedOutputMode)int.Parse(header[7], CultureInfo.InvariantCulture) : MacroLedOutputMode.Combined,
            LightingEnabled = header.Length >= 11 ? header[8] == "1" : true,
            TonePack = header.Length >= 11 ? int.Parse(header[9], CultureInfo.InvariantCulture) : 0,
            HasEncoder = header.Length >= 11 ? header[10] == "1" : true,
            LedBrightness = header.Length == 12 ? int.Parse(header[11], CultureInfo.InvariantCulture) : 100,
            SupportsTabPersonalization = header.Length == 12,
            SupportsNamedTonePacks = header[1] is "10" or "11" or "12",
            SupportsSplitLighting = header[1] is "11" or "12",
        };
        var maps = 0;
        var mapKeys = new HashSet<string>(StringComparer.Ordinal);
        var colors = new HashSet<int>();
        var effects = new HashSet<int>();
        var tones = new HashSet<int>();
        var metadata = new HashSet<int>();
        var toneNames = new HashSet<int>();
        var rgbEffects = new HashSet<int>();
        var rgbBrightnessSeen = false;
        foreach (var line in lines.Skip(1))
        {
            if (line == "END")
            {
                if (maps != 50 || lines.IndexOfEnd() != lines.Count - 1)
                    throw new InvalidDataException("Unexpected end of configuration.");
                break;
            }
            var cells = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length > 0 && cells[0] == "RGBBRIGHT")
            {
                if (!config.SupportsSplitLighting || rgbBrightnessSeen || cells.Length != 2 ||
                    !int.TryParse(cells[1], out var rgbBrightness) || rgbBrightness is < 0 or > 100)
                    throw new InvalidDataException("Invalid addressable LED brightness.");
                config.AddressableBrightness = rgbBrightness;
                rgbBrightnessSeen = true;
                continue;
            }
            if (cells.Length > 0 && cells[0] == "RGBFX")
            {
                if (!config.SupportsSplitLighting || cells.Length != 3 ||
                    !int.TryParse(cells[1], out var rgbTab) || rgbTab is < 1 or > MacroConfiguration.MaxTabs ||
                    !int.TryParse(cells[2], out var rgbEffect) || rgbEffect is < 0 or > 7 || !rgbEffects.Add(rgbTab))
                    throw new InvalidDataException("Invalid addressable LED effect.");
                config.SetTabRgbEffect(rgbTab, rgbEffect);
                continue;
            }
            if (cells.Length > 0 && cells[0] == "PACK")
            {
                if (!config.SupportsNamedTonePacks || cells.Length != 3 ||
                    !int.TryParse(cells[1], out var namedPack) || namedPack is < 1 or > 5 || !toneNames.Add(namedPack))
                    throw new InvalidDataException("Invalid custom tone metadata.");
                try
                {
                    var bytes = cells[2] == "-" ? [] : Convert.FromHexString(cells[2]);
                    if (bytes.Length > 24 || bytes.Any(b => b < 32 || b == 127))
                        throw new InvalidDataException("Invalid custom tone name.");
                    config.SetCustomToneName(namedPack, new UTF8Encoding(false, true).GetString(bytes));
                }
                catch (Exception error) when (error is FormatException or DecoderFallbackException)
                { throw new InvalidDataException("Invalid custom tone name encoding.", error); }
                continue;
            }
            if (cells.Length > 0 && cells[0] == "COLOR")
            {
                if (header.Length < 8 || cells.Length != 5 ||
                    !int.TryParse(cells[1], out var colorTab) || colorTab is < 1 or > MacroConfiguration.MaxTabs ||
                    !byte.TryParse(cells[2], out var red) || !byte.TryParse(cells[3], out var green) ||
                    !byte.TryParse(cells[4], out var blue) || !colors.Add(colorTab))
                    throw new InvalidDataException("The Macro returned an invalid tab color.");
                config.SetTabColor(colorTab, new MacroRgbColor(red, green, blue));
                continue;
            }
            if (cells.Length > 0 && cells[0] == "FX")
            {
                if (header.Length < 11 || cells.Length != 3 ||
                    !int.TryParse(cells[1], out var effectTab) || effectTab is < 1 or > MacroConfiguration.MaxTabs ||
                    !int.TryParse(cells[2], out var effect) || effect is < 0 or > 7 || !effects.Add(effectTab))
                    throw new InvalidDataException("The Macro returned an invalid tab effect.");
                config.SetTabEffect(effectTab, effect);
                continue;
            }
            if (cells.Length > 0 && cells[0] == "TONE")
            {
                if (header.Length < 11 || cells.Length != 4 ||
                    !int.TryParse(cells[1], out var tonePack) || tonePack is < 1 or > MacroConfiguration.CustomTonePacks ||
                    !int.TryParse(cells[2], out var toneEvent) || toneEvent is < 0 or >= MacroConfiguration.ToneEvents ||
                    !int.TryParse(cells[3], out var frequency) || frequency is < 0 or > 8000 ||
                    !tones.Add((tonePack - 1) * MacroConfiguration.ToneEvents + toneEvent))
                    throw new InvalidDataException("The Macro returned an invalid custom tone.");
                config.SetCustomTone(tonePack, toneEvent, frequency);
                continue;
            }
            if (cells.Length > 0 && cells[0] == "META")
            {
                if (header.Length != 12 || cells.Length != 5 ||
                    !int.TryParse(cells[1], out var metaTab) || metaTab is < 1 or > MacroConfiguration.MaxTabs ||
                    !int.TryParse(cells[2], out var tabPack) || tabPack is < 0 or > 9 ||
                    !int.TryParse(cells[3], out var icon) || icon is < 0 or > 9 ||
                    !metadata.Add(metaTab))
                    throw new InvalidDataException("The Macro returned invalid tab details.");
                byte[] nameBytes;
                try { nameBytes = cells[4] == "-" ? [] : Convert.FromHexString(cells[4]); }
                catch (FormatException error) { throw new InvalidDataException("The Macro returned an invalid tab name.", error); }
                if (nameBytes.Length > 24 || nameBytes.Contains((byte)0))
                    throw new InvalidDataException("The Macro returned an invalid tab name.");
                string name;
                try { name = new UTF8Encoding(false, true).GetString(nameBytes); }
                catch (DecoderFallbackException error) { throw new InvalidDataException("The Macro returned invalid UTF-8.", error); }
                config.SetTabTonePack(metaTab, tabPack);
                config.SetTabIdentity(metaTab, name, icon);
                continue;
            }
            if (cells.Length != 8 || cells[0] != "MAP" ||
                !int.TryParse(cells[1], out var tab) || tab is < 1 or > MacroConfiguration.MaxTabs ||
                !MacroConfiguration.Slots.Contains(cells[2]) ||
                !int.TryParse(cells[3], out var type) || type is < 0 or > 7 ||
                !int.TryParse(cells[4], out var modifiers) || modifiers is < 0 or > 255 ||
                !int.TryParse(cells[5], out var key1) || key1 is < 0 or > 255 ||
                !int.TryParse(cells[6], out var key2) || key2 is < 0 or > 255 ||
                !int.TryParse(cells[7], out var value) || value is < 0 or > 65535)
                throw new InvalidDataException("The Macro returned an invalid mapping.");
            config.Set(tab, cells[2], new MacroAction((MacroActionType)type, modifiers, key1, key2, value));
            if (!mapKeys.Add($"{tab}:{cells[2]}") ||
                (type == (int)MacroActionType.GoToTab && key1 is < 1 or > 5) ||
                (type == (int)MacroActionType.Scroll && key1 is not (1 or 2)) ||
                (type == (int)MacroActionType.Application && value == 0))
                throw new InvalidDataException("Invalid or duplicate mapping.");
            maps++;
        }
        if ((config.SupportsNamedTonePacks && toneNames.Count != MacroConfiguration.CustomTonePacks) ||
            (config.SupportsSplitLighting && (!rgbBrightnessSeen || rgbEffects.Count != MacroConfiguration.MaxTabs)) ||
            maps != MacroConfiguration.MaxTabs * MacroConfiguration.Slots.Length ||
            (header.Length >= 8 && colors.Count != MacroConfiguration.MaxTabs) ||
            (header.Length >= 11 && (effects.Count != MacroConfiguration.MaxTabs ||
                tones.Count != MacroConfiguration.CustomTonePacks * MacroConfiguration.ToneEvents)) ||
            (header.Length == 12 && metadata.Count != MacroConfiguration.MaxTabs))
            throw new InvalidDataException("The Macro configuration is incomplete.");
        if (header.Length < 11)
            for (var tab = 1; tab <= MacroConfiguration.MaxTabs; tab++) config.SetTabEffect(tab, config.LedEffect);
        if (header.Length < 12)
            for (var tab = 1; tab <= MacroConfiguration.MaxTabs; tab++) config.SetTabTonePack(tab, config.TonePack);
        if (!config.SupportsSplitLighting)
        {
            config.AddressableBrightness = config.LedBrightness;
            for (var tab = 1; tab <= MacroConfiguration.MaxTabs; tab++) config.SetTabRgbEffect(tab, config.GetTabEffect(tab));
        }
        return config;
    }

    private static int IndexOfEnd(this IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++) if (lines[i] == "END") return i;
        return -1;
    }
}
