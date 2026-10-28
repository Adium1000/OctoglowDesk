using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using OctoglowSender.Devices.Macro.Services;
using OctoglowSender.Devices.Macro.Models;

namespace OctoglowSender.Devices.Macro;

public sealed partial class MacroDevicePage
{
    private bool _toneNameDialogOpen;
    private string _toneDeviceKey = "";

    private async Task LoadLocalToneNamesAsync(MacroConfiguration config)
    {
        _toneDeviceKey = "";
        if (!config.SupportsNamedTonePacks)
            for (var pack = 1; pack <= 5; pack++) config.SetCustomToneName(pack, $"Custom {pack}");
        // HID identity, not the changing COM number. Never share names between
        // unrelated USB devices or use a global fallback cache key.
        string? path;
        try { path = await Task.Run(() => MacropadIdentity.FindAll().Select(d => d.Path)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault()); }
        catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Warning, error.Message); return; }
        _toneDeviceKey = string.IsNullOrEmpty(path) ? "" : Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        if (string.IsNullOrEmpty(_toneDeviceKey)) return;
        var names = MacroHostStore.GetToneNames(_toneDeviceKey);
        for (var pack = 1; pack <= 5; pack++)
        {
            var name = names[pack - 1];
            if (name is null) continue;
            if (name.Length == 0)
            {
                // If another client reused a deleted slot, expose it again.
                var used = config.TonePack == pack + 4 || Enumerable.Range(1, 5).Any(t => config.GetTabTonePack(t) == pack + 4) ||
                    Enumerable.Range(0, MacroConfiguration.ToneEvents).Any(e => config.GetCustomTone(pack, e) != 0);
                if (used) { MacroHostStore.SetToneName(_toneDeviceKey, pack, null); continue; }
                config.SetCustomToneName(pack, "");
                continue;
            }
            if (!config.IsToneNameAvailable(pack, name)) continue;
            if (config.SupportsNamedTonePacks)
            {
                try
                {
                    if (config.GetCustomToneName(pack) != name)
                        await MacroSerialBus.Instance.SaveCustomToneNameAsync(pack, name);
                    MacroHostStore.SetToneName(_toneDeviceKey, pack, null);
                }
                catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Warning, error.Message); continue; }
            }
            config.SetCustomToneName(pack, name);
        }
    }

    private async Task DeleteCustomToneAsync(int pack)
    {
        if (_toneNameDialogOpen || _configuration is null) return;
        _toneNameDialogOpen = true;
        var config = _configuration;
        var deviceKey = _toneDeviceKey;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                Title = M($"Ștergi „{CustomToneDisplayName(pack)}”?", $"Delete “{CustomToneDisplayName(pack)}”?"),
                Content = M("Sunetele acestui preset vor fi șterse. Taburile care îl folosesc vor reveni la Clasic. Celelalte preseturi nu se schimbă.",
                    "This preset's sounds will be cleared. Tabs using it will return to Classic. Other presets are unchanged."),
                PrimaryButtonText = M("Șterge", "Delete"), CloseButtonText = M("Anulează", "Cancel"),
                DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (!ReferenceEquals(config, _configuration)) throw new IOException(M("Conexiunea s-a schimbat.", "The connection changed."));
            if (string.IsNullOrEmpty(deviceKey))
                throw new IOException(M("Identitatea USB nu este disponibilă. Reconectează Macro.", "USB identity unavailable. Reconnect Macro."));
            IntegrationContent.IsHitTestVisible = false;
            await MacroSerialBus.Instance.DeleteCustomToneAsync(pack);
            MacroHostStore.SetToneName(deviceKey, pack, "");
        }
        catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
        finally
        {
            IntegrationContent.IsHitTestVisible = true;
            _toneNameDialogOpen = false;
            await LoadMacroConfigurationAsync();
        }
    }

    private static Expander ToneSection(string title, string glyph, StackPanel content, bool expanded)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18 });
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        return new Expander { Header = header, Content = content, IsExpanded = expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    private string CustomToneDisplayName(int pack) =>
        string.IsNullOrEmpty(_configuration!.GetCustomToneName(pack))
            ? M($"Personalizat {pack}", $"Custom {pack}") : _configuration.GetCustomToneName(pack);

    private void RenderCustomTonePage()
    {
        var config = _configuration;
        if (config is null || !MacroSerialBus.Instance.IsConnected)
        {
            IntegrationContent.Children.Add(Explanation(M("Conectează Octoglow Macro pentru a edita tonurile.",
                "Connect Octoglow Macro to edit tones.")));
            return;
        }
        if (!config.SupportsNamedTonePacks)
            IntegrationContent.Children.Add(Explanation(M(
                "Pe acest firmware, numele se păstrează în Connect. Sunetele se salvează pe Macro; testarea directă necesită noul firmware 0.2.",
                "With this firmware, names are kept in Connect. Sounds are saved on Macro; direct previews require the new 0.2 firmware.")));
        if (!config.BuzzerEnabled || config.BuzzerVolume == 0)
            IntegrationContent.Children.Add(Explanation(M("Testarea sunetelor necesită Buzzer pornit și un volum peste 0%.",
                "Sound previews require Buzzer enabled and a volume above 0%.")));

        var packs = Enumerable.Range(1, 5).Where(pack => !string.IsNullOrEmpty(config.GetCustomToneName(pack))).ToArray();
        if (packs.Length == 0)
        {
            IntegrationContent.Children.Add(Explanation(M("Nu ai încă preseturi custom. Adaugă primul preset și alege sunetele pentru fiecare acțiune.",
                "No custom presets yet. Add your first preset and choose sounds for each action.")));
            var add = new Button { Content = M("Adaugă preset", "Add preset") };
            add.Click += async (_, _) => await EditCustomToneNameAsync(1, true);
            IntegrationContent.Children.Add(add);
            return;
        }
        if (!packs.Contains(_editingCustomTonePack)) _editingCustomTonePack = packs[0];
        var tabs = new TabView { IsAddTabButtonVisible = packs.Length < 5,
            TabWidthMode = TabViewWidthMode.SizeToContent,
            CanDragTabs = false, CanReorderTabs = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        tabs.Resources["TabViewItemHeaderBackgroundSelected"] = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        // Theme resources match the button-tab surface, never the programmable RGB color.
        foreach (var pack in packs)
        {
            var header = TabHeader(pack, CustomToneDisplayName(pack), 0, true,
                () => EditCustomToneNameAsync(pack, false), () => DeleteCustomToneAsync(pack), "\uE8D6");
            var content = (Border)XamlReader.Load(
                "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' CornerRadius='8' />");
            content.Padding = new Thickness(12, 2, 12, 12);
            if (pack == _editingCustomTonePack)
            {
                var rows = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
                rows.Children.Add(HeadingWithIcon("\uE8D6", M("Configurează sunetele", "Configure sounds")));
                RenderCustomTones(pack, rows);
                content.Child = rows;
            }
            tabs.TabItems.Add(new TabViewItem { Header = header, Tag = pack, IsClosable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(8, 8, 0, 0), Content = content });
        }
        tabs.SelectedIndex = Array.IndexOf(packs, _editingCustomTonePack);
        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedItem is not TabViewItem { Tag: int pack } || pack == _editingCustomTonePack) return;
            _editingCustomTonePack = pack;
            RenderMacroSection();
        };
        tabs.AddTabButtonClick += async (_, _) =>
        {
            var free = Enumerable.Range(1, 5).FirstOrDefault(pack => string.IsNullOrEmpty(config.GetCustomToneName(pack)));
            if (free != 0) await EditCustomToneNameAsync(free, true);
        };
        IntegrationContent.Children.Add(tabs);
    }

    private async Task EditCustomToneNameAsync(int pack, bool adding)
    {
        var config = _configuration;
        if (_toneNameDialogOpen || config is null) return;
        _toneNameDialogOpen = true;
        try
        {
            var name = new TextBox { Header = M("Nume preset", "Preset name"), MaxLength = 24,
                Text = adding ? "" : config.GetCustomToneName(pack), PlaceholderText = M("Ex.: Birou", "E.g. Desk") };
            var error = new InfoBar { IsOpen = false, Severity = InfoBarSeverity.Error, IsClosable = false };
            var body = new StackPanel { Spacing = 12, MinWidth = 280 };
            body.Children.Add(name);
            body.Children.Add(Explanation(M("Numele trebuie să fie unic și să încapă în 24 de octeți UTF-8.",
                "Use a unique name that fits within 24 UTF-8 bytes.")));
            body.Children.Add(error);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                Title = DialogHeading.Create("\uE8D6", adding ? M("Adaugă preset", "Add preset") : M("Redenumește presetul", "Rename preset")),
                Content = body, PrimaryButtonText = M("Aplică", "Apply"), CloseButtonText = M("Anulează", "Cancel") };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                args.Cancel = true;
                dialog.IsPrimaryButtonEnabled = false;
                try
                {
                    var value = name.Text.Trim().Normalize(NormalizationForm.FormC);
                    if (value.Length == 0 || value.Any(char.IsControl) || Encoding.UTF8.GetByteCount(value) > 24)
                        throw new ArgumentException(M("Introdu un nume valid, de cel mult 24 de octeți UTF-8.", "Enter a valid name of at most 24 UTF-8 bytes."));
                    if (!config.IsToneNameAvailable(pack, value))
                        throw new ArgumentException(M("Există deja un preset cu acest nume. Alege alt nume.", "A preset already uses this name. Choose another name."));
                    if (!ReferenceEquals(config, _configuration))
                        throw new InvalidOperationException(M("Conexiunea s-a schimbat. Redeschide editorul.", "The connection changed. Reopen the editor."));
                    if (!config.SupportsNamedTonePacks && string.IsNullOrEmpty(_toneDeviceKey))
                        throw new InvalidOperationException(M("Identitatea USB nu este disponibilă. Reconectează Macro.", "USB identity unavailable. Reconnect Macro."));
                    if (config.SupportsNamedTonePacks)
                        await MacroSerialBus.Instance.SaveCustomToneNameAsync(pack, value);
                    if (!string.IsNullOrEmpty(_toneDeviceKey))
                        MacroHostStore.SetToneName(_toneDeviceKey, pack, config.SupportsNamedTonePacks ? null : value);
                    config.SetCustomToneName(pack, value);
                    _editingCustomTonePack = pack;
                    args.Cancel = false;
                }
                catch (Exception exception) { error.Message = exception.Message; error.IsOpen = true; }
                finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) RenderMacroSection();
        }
        finally { _toneNameDialogOpen = false; }
    }
}
