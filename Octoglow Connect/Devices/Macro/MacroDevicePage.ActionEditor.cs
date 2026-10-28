using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OctoglowSender.Devices.Macro.Models;
using OctoglowSender.Devices.Macro.Services;

namespace OctoglowSender.Devices.Macro;

public sealed partial class MacroDevicePage
{
    private static (string Label, int Value)[] KeyChoices(bool allowNone)
    {
        var choices = new List<(string, int)>();
        if (allowNone) choices.Add((M("Fără", "None"), 0));
        for (var i = 0; i < 26; i++) choices.Add((((char)('A' + i)).ToString(), 4 + i));
        for (var i = 1; i <= 9; i++) choices.Add((i.ToString(), 29 + i));
        choices.Add(("0", 39));
        choices.AddRange([
            ("Enter", 40), ("Escape", 41), ("Backspace", 42), ("Tab", 43),
            ("Space", 44), ("-", 45), ("=", 46), ("F1", 58), ("F2", 59),
            ("F3", 60), ("F4", 61), ("F5", 62), ("F6", 63), ("F7", 64),
            ("F8", 65), ("F9", 66), ("F10", 67), ("F11", 68), ("F12", 69),
            ("Print Screen", 70), ("Pause", 72), ("Insert", 73), ("Home", 74),
            ("Page Up", 75), ("Delete", 76), ("End", 77), ("Page Down", 78),
            ("Right", 79), ("Left", 80), ("Down", 81), ("Up", 82),
            ("F13", 104), ("F14", 105), ("F15", 106), ("F16", 107),
            ("F17", 108), ("F18", 109), ("F19", 110), ("F20", 111),
            ("F21", 112), ("F22", 113), ("F23", 114), ("F24", 115),
        ]);
        return choices.ToArray();
    }

    private static (string Label, int Value)[] MediaChoices() => [
        (M("Redare / Pauză", "Play / Pause"), 0x00CD),
        (M("Piesa următoare", "Next track"), 0x00B5),
        (M("Piesa anterioară", "Previous track"), 0x00B6),
        (M("Oprește", "Stop"), 0x00B7),
        (M("Fără sunet", "Mute"), 0x00E2),
        (M("Volum +", "Volume +"), 0x00E9),
        (M("Volum -", "Volume -"), 0x00EA),
        (M("Luminozitate ecran +", "Screen brightness +"), 0x006F),
        (M("Luminozitate ecran -", "Screen brightness -"), 0x0070),
        (M("Calculator", "Calculator"), 0x0192),
        (M("E-mail", "Email"), 0x018A),
        (M("Browser", "Browser"), 0x0194),
        (M("Căutare web", "Web search"), 0x0221),
        (M("Pagina de pornire", "Web home"), 0x0223),
        (M("Înapoi în browser", "Browser back"), 0x0224),
        (M("Înainte în browser", "Browser forward"), 0x0225),
        (M("Reîmprospătează browserul", "Browser refresh"), 0x0227),
    ];

    private static StackPanel EditorField(string label, FrameworkElement control)
    {
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        field.Children.Add(control);
        return field;
    }

    private async Task ShowActionEditorAsync(int tab, string slot, string label)
    {
        var original = _configuration!.Get(tab, slot);
        var originalHost = original.Type == MacroActionType.Application ? MacroHostStore.Find(original.Value) : null;
        var currentKind = original.Type switch
        {
            MacroActionType.Key when original.Modifiers != 0 || original.Key2 != 0 => "shortcut",
            MacroActionType.Key => "key",
            MacroActionType.Media => "media",
            MacroActionType.Scroll => "scroll",
            MacroActionType.NextTab => "next",
            MacroActionType.PreviousTab => "previous",
            MacroActionType.GoToTab => "goto",
            MacroActionType.Application => originalHost?.IsClock == true ? "clock" : "application",
            _ => "none",
        };
        var type = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in new (string Label, string Kind)[]
        {
            (M("Fără acțiune", "No action"), "none"),
            (M("Tastă", "Key"), "key"),
            (M("Scurtătură de taste", "Keyboard shortcut"), "shortcut"),
            (M("Multimedia", "Media"), "media"),
            (M("Scroll", "Scroll"), "scroll"),
            (M("Tabul următor", "Next tab"), "next"),
            (M("Tabul anterior", "Previous tab"), "previous"),
            (M("Mergi la un tab", "Go to tab"), "goto"),
            (M("Deschide o aplicație", "Open an application"), "application"),
        })
            type.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Kind });
        if (MacroHostStore.AllowOtherOctoglowDevices || currentKind == "clock")
            type.Items.Add(new ComboBoxItem { Content = M("Controlează un dispozitiv Octoglow", "Control an Octoglow device"), Tag = "clock" });
        type.SelectedItem = type.Items.OfType<ComboBoxItem>()
            .First(item => (string)item.Tag == currentKind);

        var fields = new StackPanel { Spacing = 14, Margin = new Thickness(0, 12, 0, 0) };
        var error = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error };
        ComboBox? key1 = null, key2 = null, media = null, scroll = null, target = null, appPicker = null;
        ComboBox? clockPicker = null, clockCommand = null;
        CheckBox? ctrl = null, shift = null, alt = null, win = null;
        void BuildFields()
        {
            fields.Children.Clear();
            key1 = key2 = media = scroll = target = appPicker = null;
            clockPicker = clockCommand = null;
            ctrl = shift = alt = win = null;
            var selected = (type.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
            if (selected is "key" or "shortcut")
            {
                key1 = Choices(KeyChoices(false), original.Key1);
                fields.Children.Add(EditorField(M("Tastă", "Key"), key1));
                if (selected == "shortcut")
                {
                    var modifiers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    ctrl = new CheckBox { Content = "Ctrl", IsChecked = (original.Modifiers & 1) != 0 };
                    shift = new CheckBox { Content = "Shift", IsChecked = (original.Modifiers & 2) != 0 };
                    alt = new CheckBox { Content = "Alt", IsChecked = (original.Modifiers & 4) != 0 };
                    win = new CheckBox { Content = "Win", IsChecked = (original.Modifiers & 8) != 0 };
                    modifiers.Children.Add(ctrl); modifiers.Children.Add(shift);
                    modifiers.Children.Add(alt); modifiers.Children.Add(win);
                    fields.Children.Add(EditorField(M("Modificatori", "Modifiers"), modifiers));
                    key2 = Choices(KeyChoices(true), original.Key2);
                    fields.Children.Add(EditorField(M("A doua tastă, opțional", "Second key, optional"), key2));
                }
            }
            else if (selected == "media")
            {
                media = Choices(MediaChoices(), original.Value);
                fields.Children.Add(EditorField(M("Acțiune multimedia", "Media action"), media));
                fields.Children.Add(Explanation(M("Luminozitatea ecranului depinde de suportul Windows și al monitorului.",
                    "Screen brightness depends on Windows and monitor support.")));
            }
            else if (selected == "scroll")
            {
                scroll = Choices([(M("Sus", "Up"), 1), (M("Jos", "Down"), 2)], original.Key1);
                fields.Children.Add(EditorField(M("Direcție", "Direction"), scroll));
            }
            else if (selected == "goto")
            {
                target = Choices(Enumerable.Range(1, _configuration!.TabCount)
                    .Select(index => ($"Tab {index}", index)).ToArray(), original.Key1);
                fields.Children.Add(EditorField(M("Tab de destinație", "Destination tab"), target));
            }
            else if (selected == "clock")
            {
                if (!MacroHostStore.AllowOtherOctoglowDevices)
                {
                    fields.Children.Add(Explanation(M("Activează integrarea cu dispozitive Octoglow pentru a configura această acțiune.",
                        "Enable the Octoglow devices integration to configure this action.")));
                    return;
                }
                clockPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var device in MacroClockControl.Devices)
                    clockPicker.Items.Add(new ComboBoxItem { Content = $"{device.DisplayName} · {device.Address}", Tag = device });
                clockPicker.SelectedItem = clockPicker.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => ((DeviceDescriptor)item.Tag).Id == originalHost?.ClockDeviceId);
                if (clockPicker.SelectedItem is null && originalHost?.IsClock != true && clockPicker.Items.Count == 1)
                    clockPicker.SelectedIndex = 0;
                clockCommand = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var command in MacroClockControl.Commands)
                    clockCommand.Items.Add(new ComboBoxItem { Content = MacroClockControl.Label(command.Id), Tag = command.Id });
                clockCommand.SelectedItem = clockCommand.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (string)item.Tag == originalHost?.ClockCommand);
                if (clockCommand.SelectedItem is null) clockCommand.SelectedIndex = 0;
                fields.Children.Add(EditorField(M("Dispozitiv", "Device"), clockPicker));
                fields.Children.Add(EditorField(M("Comandă", "Command"), clockCommand));
                fields.Children.Add(Explanation(M("Connect trebuie să ruleze, iar ceasul să fie conectat la rețea. Dacă lista este goală, adaugă întâi ceasul în Connect. Oprirea ecranului folosește modul Somn; o notificare îl poate trezi.",
                    "Connect must be running and the clock reachable on the network. If the list is empty, add the clock to Connect first. Screen off uses Sleep; a notification can wake it.")));
            }
            else if (selected == "application")
            {
                var apps = MacroHostStore.Applications;
                if (apps.Count == 0)
                    fields.Children.Add(Explanation(M("Adaugă mai întâi o aplicație în secțiunea Aplicații.",
                        "Add an application in the Applications section first.")));
                else
                {
                    appPicker = Choices(apps.Select(app => ($"{app.Name}  ·  #{app.Id}", app.Id)).ToArray(), original.Value);
                    fields.Children.Add(EditorField(M("Aplicație", "Application"), appPicker));
                    fields.Children.Add(Explanation(M("Calea rămâne în Connect; firmware-ul reține doar ID-ul.",
                        "The path stays in Connect; firmware stores only the ID.")));
                }
            }
        }
        BuildFields();
        type.SelectionChanged += (_, _) => BuildFields();
        var body = new StackPanel { MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Stretch, Spacing = 8 };
        body.Children.Add(EditorField(M("Acțiune", "Action"), type));
        body.Children.Add(fields);
        body.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 12,
                Children = { new FontIcon { Glyph = "\uE7F4", FontSize = 22 },
                    new TextBlock { Text = M($"Configurează {label} · Tab {tab}", $"Configure {label} · Tab {tab}"),
                        VerticalAlignment = VerticalAlignment.Center } },
            },
            Content = new ScrollViewer { Content = body, MaxHeight = 430 },
            PrimaryButtonText = M("Salvează", "Save"),
            CloseButtonText = M("Anulează", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 504;
        var saved = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                var selected = (type.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
                MacroAction ClockAction()
                {
                    if (clockPicker?.SelectedItem is not ComboBoxItem { Tag: DeviceDescriptor device } ||
                        clockCommand?.SelectedItem is not ComboBoxItem { Tag: string command })
                        throw new InvalidOperationException(M("Alege un ceas și o comandă.", "Choose a clock and a command."));
                    return new MacroAction(MacroActionType.Application, Value: MacroHostStore.AddClock(device, command).Id);
                }
                var action = selected switch
                {
                    "key" => new MacroAction(MacroActionType.Key, Key1: Selected(key1!)),
                    "shortcut" => new MacroAction(MacroActionType.Key,
                        (ctrl!.IsChecked == true ? 1 : 0) | (shift!.IsChecked == true ? 2 : 0) |
                        (alt!.IsChecked == true ? 4 : 0) | (win!.IsChecked == true ? 8 : 0),
                        Selected(key1!), Selected(key2!)),
                    "media" => new MacroAction(MacroActionType.Media, Value: Selected(media!)),
                    "scroll" => new MacroAction(MacroActionType.Scroll, Key1: Selected(scroll!)),
                    "next" => new MacroAction(MacroActionType.NextTab),
                    "previous" => new MacroAction(MacroActionType.PreviousTab),
                    "goto" => new MacroAction(MacroActionType.GoToTab, Key1: Selected(target!)),
                    "application" when appPicker is not null =>
                        new MacroAction(MacroActionType.Application, Value: Selected(appPicker)),
                    "clock" => ClockAction(),
                    "application" => throw new InvalidOperationException(M("Adaugă mai întâi o aplicație.", "Add an application first.")),
                    _ => new MacroAction(MacroActionType.None),
                };
                if (_configuration is { TabCount: > 1 } config &&
                    MacroConfiguration.Slots.Take(8).Contains(slot) &&
                    !IsTabSwitch(action, tab) && !HasTabSwitch(config, tab, slot))
                {
                    error.Title = M("Este necesară o tastă de schimbare", "A tab-switch key is required");
                    error.Message = M("Configurează mai întâi altă tastă să schimbe tabul.",
                        "Configure another key to switch tabs first.");
                    error.IsOpen = true;
                    return;
                }
                dialog.IsPrimaryButtonEnabled = false;
                await MacroSerialBus.Instance.SaveMappingAsync(tab, slot, action);
                _configuration?.Set(tab, slot, action);
                saved = true;
                args.Cancel = false;
            }
            catch (Exception exception)
            {
                error.Title = M("Nu s-a putut salva", "Could not save");
                error.Message = exception.Message;
                error.IsOpen = true;
            }
            finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        await dialog.ShowAsync();
        if (saved && _isLoaded) RenderMacroSection();
    }
}
