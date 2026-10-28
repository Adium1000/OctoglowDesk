using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using OctoglowSender.Devices.Macro.Models;
using OctoglowSender.Devices.Macro.Services;
using Windows.Storage.Pickers;

namespace OctoglowSender.Devices.Macro;

public sealed partial class MacroDevicePage
{
    private MacroConfiguration? _configuration;
    private bool _loadingConfiguration;
    private bool _tabMutationInProgress;
    private int _selectedTab = 1;
    private int _editingCustomTonePack = 1;
    private enum IntegrationPage { Overview, Devices, Applications, CustomTones }
    private IntegrationPage _integrationPage;
    private static string M(string ro, string en) => AppStrings.IsEnglish ? en : ro;

    private void OnSerialConnectionChanged(bool connected) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_isLoaded) return;
        UpdateConnectionStatus();
        if (connected) _ = LoadMacroConfigurationAsync();
        else { _configuration = null; ApplyHardwareVariant(); UpdateLedPreview(); RenderMacroSection(); }
    });

    private void OnSerialStatusChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_isLoaded) return;
        UpdateConnectionStatus();
        if (!MacroSerialBus.Instance.IsConnected) RenderMacroSection();
    });

    private void OnSerialTabChanged(int tab) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_configuration is null || tab is < 1 or > MacroConfiguration.MaxTabs) return;
        var changed = _selectedTab != tab;
        _configuration.ActiveTab = tab;
        _selectedTab = tab;
        UpdateLedPreview();
        if (changed && _isLoaded && MacroNavigation.SelectedItem == ButtonsItem) RenderMacroSection();
    });

    private void OnSerialInput(string slot) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_isLoaded) return;
        if (slot == "knob") SetKnobPressed(true);
        else if (slot is "cw" or "ccw") AnimateKnobRotate(slot == "cw" ? 1 : -1);
        else SetKeyPressed(slot, true);
        _ = ClearInputHighlightAsync(slot);
    });

    private async Task ClearInputHighlightAsync(string slot)
    {
        await Task.Delay(180);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded) return;
            if (slot == "knob") SetKnobPressed(false);
            else SetKeyPressed(slot, false);
        });
    }

    private async Task LoadMacroConfigurationAsync()
    {
        if (!_isLoaded || _loadingConfiguration) return;
        if (!MacroSerialBus.Instance.IsConnected)
        {
            _configuration = null;
            UpdateLedPreview();
            RenderMacroSection();
            return;
        }
        _loadingConfiguration = true;
        RenderMacroSection();
        try
        {
            _configuration = await MacroSerialBus.Instance.ReadConfigurationAsync();
            await LoadLocalToneNamesAsync(_configuration);
            var savedTabs = MacroHostStore.TabAppearances;
            if (!MacroSerialBus.Instance.SupportsV8 || MacroHostStore.PendingTabAppearanceSync)
                for (var tab = 1; tab <= Math.Min(_configuration.TabCount, savedTabs.Count); tab++)
                    _configuration.SetTabIdentity(tab, savedTabs[tab - 1].Name, savedTabs[tab - 1].Icon);
            if (MacroSerialBus.Instance.SupportsV8)
            {
                if (MacroHostStore.PendingTabAppearanceSync)
                {
                    try { await SyncTabAppearancesAsync(_configuration); }
                    catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Warning, error.Message); }
                }
                else MacroHostStore.RememberFirmwareTabAppearances(_configuration);
            }
            else MacroHostStore.RememberTabCount(_configuration.TabCount);
            _selectedTab = Math.Clamp(_configuration.ActiveTab, 1, _configuration.TabCount);
            ApplyHardwareVariant();
        }
        catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
        finally
        {
            _loadingConfiguration = false;
            if (_isLoaded)
            {
                UpdateLedPreview();
                RenderMacroSection();
            }
        }
    }

    private void MacroNavigation_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_isLoaded) return;
        var index = sender.SelectedItem == LedItem ? 1 : sender.SelectedItem == BuzzerItem ? 2 :
            sender.SelectedItem == IntegrationsItem ? 3 : 0;
        var previous = _macroSectionIndex;
        _macroSectionIndex = index;
        _integrationPage = IntegrationPage.Overview;
        MacroContentScroll.ChangeView(null, 0, null);
        RenderMacroSection();
        if (index != previous) AnimateMacroNavigation(index > previous);
    }

    private void RenderMacroSection()
    {
        if (!_isLoaded || ContentHost is null) return;
        if (_macroPanel is not null) { RebuildMacroPanel(); return; }
        ResetMacroNavigationMotion();
        ContentHost.Children.Clear();
        if (MacroNavigation.SelectedItem == IntegrationsItem)
        {
            RenderIntegrations();
            return;
        }
        if (!MacroSerialBus.Instance.IsConnected)
        {
            ContentHost.Children.Add(Explanation(M("Configurarea se conectează automat la Octoglow Macro. Tastele HID funcționează și fără Connect.",
                "Configuration connects to Octoglow Macro automatically. HID keys also work without Connect.")));
            if (MacroNavigation.SelectedItem == ButtonsItem) RenderOfflineTabs();
            return;
        }
        if (_configuration is null)
        {
            ContentHost.Children.Add(new ProgressRing { IsActive = true, Width = 28, Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 28) });
            return;
        }
        if (MacroNavigation.SelectedItem == LedItem) RenderLed();
        else if (MacroNavigation.SelectedItem == BuzzerItem)
        {
            if (_integrationPage == IntegrationPage.CustomTones)
            {
                ContentHost.Children.Add(CreateIntegrationHeader(IntegrationPage.CustomTones));
                RenderCustomTonePage();
            }
            else RenderBuzzer();
        }
        else RenderButtons();
    }

    private void ApplyHardwareVariant()
    {
        var hasEncoder = _configuration?.HasEncoder ?? true;
        EncoderLabel.Text = hasEncoder ? "K1" : "B8";
        EncoderNotch.Visibility = hasEncoder ? Visibility.Visible : Visibility.Collapsed;
        EncoderBorder.CornerRadius = new CornerRadius(hasEncoder ? 44 : 16);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EncoderBorder,
            hasEncoder ? "K1 encoder" : "B8");
    }

    private static Border Card() => (Border)XamlReader.Load(
        "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' " +
        "BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1' CornerRadius='6'/>");

    private static TextBlock Explanation(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.72,
        Margin = new Thickness(4, 8, 4, 8),
    };

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(4, 20, 4, 8),
    };

    private static StackPanel HeadingWithIcon(string glyph, string text)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9,
            Margin = new Thickness(4, 20, 4, 8) };
        heading.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        heading.Children.Add(new TextBlock { Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center });
        return heading;
    }

    private static Border SettingCard(string glyph, string title, string description, FrameworkElement? trailing = null)
    {
        var card = Card();
        card.Padding = new Thickness(16, 12, 16, 12);
        card.MinHeight = 68;
        var row = new Grid { ColumnSpacing = 16, MinHeight = 40 };
        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description))
            copy.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.72,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.HorizontalAlignment = HorizontalAlignment.Right;
            var switchInset = trailing is ToggleSwitch ? -12 : 0;
            if (trailing is ToggleSwitch toggle) toggle.MinWidth = 0;
            trailing.Margin = new Thickness(0, 0, switchInset, 0);
            Grid.SetColumn(trailing, 2);
            row.Children.Add(trailing);
            // Match Clock's responsive cards: when space is tight, keep the
            // label aligned and move the control below it instead of squeezing
            // or shifting the entire category sideways.
            bool? wasCompact = null;
            card.SizeChanged += (_, _) =>
            {
                var compact = trailing is not ToggleSwitch && card.ActualWidth < 520;
                if (wasCompact == compact) return;
                wasCompact = compact;
                row.RowDefinitions[0].Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
                Grid.SetColumnSpan(copy, compact ? 2 : 1);
                Grid.SetRow(trailing, compact ? 1 : 0);
                Grid.SetColumn(trailing, compact ? 1 : 2);
                Grid.SetColumnSpan(trailing, compact ? 2 : 1);
                trailing.HorizontalAlignment = HorizontalAlignment.Right;
                trailing.Margin = new Thickness(0, compact ? 10 : 0, switchInset, 0);
            };
        }
        card.Child = row;
        return card;
    }

    // One in-flight write and one latest value: continuous feedback without a
    // growing USB queue or disabling the thumb while the pointer is dragging.
    private void BindLiveSlider(Slider slider, TextBlock label, Func<int, Task> send, Action<int> preview)
    {
        var configuration = _configuration;
        var confirmed = (int)Math.Round(slider.Value);
        var requested = confirmed;
        var running = false;
        var resetting = false;
        async Task PumpAsync()
        {
            running = true;
            try
            {
                while (requested != confirmed)
                {
                    // Do not replay a stale drag after a disconnect/reload.
                    if (!ReferenceEquals(configuration, _configuration) || !MacroSerialBus.Instance.IsConnected) return;
                    var next = requested;
                    await send(next);
                    confirmed = next;
                    await Task.Delay(150);
                }
            }
            catch (Exception error)
            {
                requested = confirmed;
                resetting = true;
                slider.Value = confirmed;
                resetting = false;
                label.Text = $"{confirmed}%";
                preview(confirmed);
                if (_isLoaded && ReferenceEquals(configuration, _configuration))
                    ShowMacroMessage(InfoBarSeverity.Error, error.Message);
            }
            finally { running = false; }
        }
        slider.ValueChanged += (_, _) =>
        {
            if (resetting) return;
            requested = (int)Math.Round(slider.Value);
            label.Text = $"{requested}%";
            preview(requested);
            if (!running) _ = PumpAsync();
        };
    }

    private void ShowMacroMessage(InfoBarSeverity severity, string message)
    {
        var status = _macroPanelStatus ?? MacroStatus;
        status.Severity = severity;
        status.Title = severity == InfoBarSeverity.Error ? M("Nu s-a putut finaliza", "Could not complete") :
            M("Octoglow Macro", "Octoglow Macro");
        status.Message = message;
        status.IsOpen = true;
    }

    private static ComboBox Choices((string Label, int Value)[] options, int selected)
    {
        var combo = new ComboBox { MinWidth = 170 };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (int)item.Tag == selected);
        if (combo.SelectedItem is null && combo.Items.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }

    private static int Selected(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag is int value ? value : 0;

    private static Windows.UI.Color DisplayColor(MacroRgbColor color) =>
        Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue);

    private static string ColorHex(MacroRgbColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private void RenderLed()
    {
        var config = _configuration!;
        ContentHost.Children.Add(Heading(M("Iluminare", "Lighting")));
        var modes = new List<(string Label, int Value)> {
            (M("Oprit", "Off"), -1),
            (M("LED adresabil + LED-uri externe", "Addressable + external LEDs"), 0),
            (M("LED adresabil", "Addressable LED"), 1),
        };
        if (MacroSerialBus.Instance.SupportsV7)
            modes.Add((M("Doar LED-uri externe", "External LEDs only"), 2));
        var outputMode = Choices(modes.ToArray(), config.LightingEnabled ? (int)config.LedOutputMode : -1);
        outputMode.IsEnabled = MacroSerialBus.Instance.SupportsV7;
        outputMode.SelectionChanged += async (_, _) =>
        {
            var next = Selected(outputMode);
            if (next == (config.LightingEnabled ? (int)config.LedOutputMode : -1)) return;
            outputMode.IsEnabled = false;
            try
            {
                if (MacroSerialBus.Instance.SupportsV10)
                    await MacroSerialBus.Instance.SaveLightingOutputAsync(next + 1);
                else
                {
                    // Old firmware: reflect each acknowledged change so a partial failure
                    // cannot leave Connect reporting an output that was never enabled.
                    if (next >= 0)
                    {
                        await MacroSerialBus.Instance.SaveLedOutputModeAsync((MacroLedOutputMode)next);
                        config.LedOutputMode = (MacroLedOutputMode)next;
                    }
                    await MacroSerialBus.Instance.SaveLightingEnabledAsync(next >= 0);
                }
                if (next >= 0) config.LedOutputMode = (MacroLedOutputMode)next;
                config.LightingEnabled = next >= 0;
                UpdateLedPreview();
                RenderMacroSection();
            }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
                outputMode.SelectedItem = outputMode.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (int)item.Tag == (config.LightingEnabled ? (int)config.LedOutputMode : -1));
            }
            finally { outputMode.IsEnabled = MacroSerialBus.Instance.SupportsV7; }
        };
        ContentHost.Children.Add(SettingCard("\uE706", M("Iluminare activă", "Active lighting"),
            MacroSerialBus.Instance.SupportsV6
                ? M("LED-ul adresabil, LED-urile externe sau ambele.",
                    "Addressable LED, external LEDs, or both.")
                : M("Actualizează firmware-ul Macro pentru opțiunile de iluminare.",
                    "Update the Macro firmware for lighting options."), outputMode));

        if (config.SupportsSplitLighting)
        {
            RenderSplitLighting();
            return;
        }
        ContentHost.Children.Add(Explanation(M(
            "Instalează noul firmware 0.2 pentru luminozitate și efecte separate pe fiecare ieșire.",
            "Install the new 0.2 firmware for separate brightness and effects on each output.")));
        if (MacroSerialBus.Instance.SupportsV8)
        {
            var brightness = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1,
                Width = 172, Value = config.LedBrightness };
            var amount = new TextBlock { Text = $"{config.LedBrightness}%", MinWidth = 38,
                VerticalAlignment = VerticalAlignment.Center };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            controls.Children.Add(brightness);
            controls.Children.Add(amount);
            BindLiveSlider(brightness, amount, MacroSerialBus.Instance.SaveLedBrightnessAsync, next =>
            {
                config.LedBrightness = next;
                config.AddressableBrightness = next;
                UpdateLedPreview();
            });
            ContentHost.Children.Add(SettingCard("\uE706", M("Luminozitate LED", "LED brightness"),
                M("Reglează luminozitatea LED-urilor active.", "Adjust brightness for the active LEDs."), controls));
        }

        var effects = new List<(string Label, int Value)>
        {
            (M("Static", "Static"), 0), (M("Respirație", "Breathing"), 1),
            (M("Clipire", "Blink"), 2),
        };
        if (MacroSerialBus.Instance.SupportsV7) effects.AddRange([
            (M("Puls dublu", "Double pulse"), 3),
            (M("Bătăi de inimă", "Heartbeat"), 4),
            (M("Val", "Wave"), 5),
            (M("Sclipiri", "Sparkle"), 6),
            (M("Stroboscop", "Strobe"), 7),
        ]);
        var effect = Choices(effects.ToArray(), config.GetTabEffect(_selectedTab));
        effect.SelectionChanged += async (_, _) =>
        {
            var next = Selected(effect);
            if (next == config.GetTabEffect(_selectedTab)) return;
            effect.IsEnabled = false;
            var target = _selectedTab;
            var all = false;
            try
            {
                if (config.TabCount > 1 && MacroSerialBus.Instance.SupportsV7)
                {
                    var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    choice.Items.Add(new ComboBoxItem { Content = M("Toate taburile", "All tabs"), Tag = 0 });
                    for (var tab = 1; tab <= config.TabCount; tab++)
                        choice.Items.Add(new ComboBoxItem { Content = $"Tab {tab}", Tag = tab });
                    choice.SelectedIndex = target;
                    var dialog = new ContentDialog { XamlRoot = XamlRoot,
                        Title = DialogHeading.Create("\uE781", M("Aplică efectul", "Apply effect")),
                        Content = choice, PrimaryButtonText = M("Aplică", "Apply"),
                        CloseButtonText = M("Anulează", "Cancel") };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    target = Selected(choice);
                    all = target == 0;
                }
                if (MacroSerialBus.Instance.SupportsV7)
                {
                    if (all)
                    {
                        await MacroSerialBus.Instance.SaveAllTabEffectsAsync(next);
                        for (var tab = 1; tab <= MacroConfiguration.MaxTabs; tab++) config.SetTabEffect(tab, next);
                    }
                    else
                    {
                        await MacroSerialBus.Instance.SaveTabEffectAsync(target, next);
                        config.SetTabEffect(target, next);
                    }
                }
                else
                {
                    await MacroSerialBus.Instance.SaveLedAsync(next);
                    config.LedEffect = next;
                    for (var tab = 1; tab <= MacroConfiguration.MaxTabs; tab++) config.SetTabEffect(tab, next);
                }
                UpdateLedPreview();
            }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
            }
            finally
            {
                effect.SelectedItem = effect.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (int)item.Tag == config.GetTabEffect(_selectedTab));
                effect.IsEnabled = true;
            }
        };
        ContentHost.Children.Add(SettingCard("\uE781", M("Efect LED", "LED effect"),
            M("Alege efectul și tabul pe care îl folosește.",
              "Choose the effect and the tab that uses it."), effect));
    }

    private void RenderBuzzer()
    {
        var config = _configuration!;
        ContentHost.Children.Add(Heading(M("Buzzer", "Buzzer")));
        var buzzer = new ToggleSwitch { IsOn = config.BuzzerEnabled, OnContent = "", OffContent = "" };
        buzzer.Toggled += async (_, _) =>
        {
            if (buzzer.IsOn == config.BuzzerEnabled) return;
            buzzer.IsEnabled = false;
            try { await MacroSerialBus.Instance.SaveBuzzerAsync(buzzer.IsOn); config.BuzzerEnabled = buzzer.IsOn; }
            catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); buzzer.IsOn = config.BuzzerEnabled; }
            finally { buzzer.IsEnabled = true; }
        };
        ContentHost.Children.Add(SettingCard("\uE767", "Buzzer",
            M("Permite Macropadului să redea sunete.", "Lets the Macropad play sounds."), buzzer));
        var volume = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1,
            Width = 172, Value = config.BuzzerVolume,
            IsEnabled = MacroSerialBus.Instance.SupportsBuzzerVolume };
        var volumeText = new TextBlock { Text = $"{config.BuzzerVolume}%", MinWidth = 38,
            VerticalAlignment = VerticalAlignment.Center };
        var volumeControl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        volumeControl.Children.Add(volume);
        volumeControl.Children.Add(volumeText);
        BindLiveSlider(volume, volumeText, MacroSerialBus.Instance.SaveBuzzerVolumeAsync,
            next => config.BuzzerVolume = next);
        ContentHost.Children.Add(SettingCard("\uE9E9", M("Volum", "Volume"),
            MacroSerialBus.Instance.SupportsBuzzerVolume
                ? M("Intensitatea se schimbă fără a modifica frecvența tonului. Un ding previzualizează nivelul nou.",
                    "Adjust loudness without changing the tone frequency. A ding previews the new level.")
                : M("Actualizează firmware-ul Macro pentru controlul volumului.",
                    "Update the Macro firmware for volume control."), volumeControl));

        if (!MacroSerialBus.Instance.SupportsV7)
        {
            ContentHost.Children.Add(Explanation(M(
                "Actualizează firmware-ul Macro la 0.2 pentru pachetele de tonuri.",
                "Update Macro firmware to 0.2 for tone packs.")));
            return;
        }
        ContentHost.Children.Add(Heading(M("Tonuri", "Tones")));
        var packs = Choices(TonePackChoices(), config.TonePack);
        packs.SelectionChanged += async (_, _) =>
        {
            var next = Selected(packs);
            if (next == config.TonePack) return;
            packs.IsEnabled = false;
            try
            {
                await MacroSerialBus.Instance.SaveTonePackAsync(next);
                config.TonePack = next;
                for (var tab = 1; tab <= MacroConfiguration.MaxTabs; tab++) config.SetTabTonePack(tab, next);
                RenderMacroSection();
            }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
                packs.SelectedItem = packs.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (int)item.Tag == config.TonePack);
            }
            finally { packs.IsEnabled = true; }
        };
        ContentHost.Children.Add(SettingCard("\uE767", M("Temă pentru toate taburile", "Theme for all tabs"),
            M("Aplică un preset tuturor taburilor. Fiecare tab poate apoi avea propriul preset.",
              "Apply a preset to all tabs. Each tab can then have its own theme."), packs));
        ContentHost.Children.Add(IntegrationLink("\uE8D6", M("Tonuri Custom", "Custom tones"), IntegrationPage.CustomTones));
    }

    private static (string Label, int Value)[] ToneChoices(int current)
    {
        var values = new[] { 0, 220, 330, 440, 523, 660, 784, 880, 1047, 1200, 1500, 1800, 2200, 3000, 4000 };
        if (!values.Contains(current)) values = values.Append(current).OrderBy(value => value).ToArray();
        return values.Select(value =>
            (value == 0 ? M("Oprit", "Off") : $"{value} Hz", value)).ToArray();
    }

    private void RenderCustomTones(int pack, StackPanel target)
    {
        var config = _configuration!;
        for (var hold = 0; hold < 2; hold++)
        {
            var isHold = hold == 1;
            var group = new StackPanel { Spacing = 6 };
            var setAll = Choices(ToneChoices(config.GetCustomTone(pack, isHold ? 7 : 0)),
                config.GetCustomTone(pack, isHold ? 7 : 0));
            setAll.SelectedIndex = -1;
            setAll.PlaceholderText = M("Alege tonul", "Choose tone");
            setAll.SelectionChanged += async (_, _) =>
            {
                var next = Selected(setAll);
                setAll.IsEnabled = false;
                try
                {
                    await MacroSerialBus.Instance.SaveCustomToneGroupAsync(pack, isHold, next);
                    for (var key = 0; key < 7; key++) config.SetCustomTone(pack, key + (isHold ? 7 : 0), next);
                    RenderMacroSection();
                }
                catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
                finally { setAll.IsEnabled = true; }
            };
            group.Children.Add(SettingCard("\uE8D4",
                isHold ? M("Toate tonurile Hold", "All hold tones") : M("Toate tonurile la apăsare", "All press tones"),
                M("Aplică aceeași frecvență pentru B1-B7.", "Apply the same frequency to B1-B7."), setAll));
            for (var key = 0; key < 7; key++)
                group.Children.Add(ToneCard(pack, key + (isHold ? 7 : 0),
                    $"B{key + 1}" + (isHold ? " Hold" : "")));
            target.Children.Add(ToneSection(isHold ? M("Apăsare lungă · B1-B7", "Hold · B1-B7") :
                M("Apăsare · B1-B7", "Press · B1-B7"), "\uE765", group, !isHold));
        }
        var other = new StackPanel { Spacing = 6 };
        other.Children.Add(ToneCard(pack, 14, config.HasEncoder ? "K1" : "B8"));
        if (config.HasEncoder)
        {
            other.Children.Add(ToneCard(pack, 15, M("Rotire dreapta", "Turn right")));
            other.Children.Add(ToneCard(pack, 16, M("Rotire stânga", "Turn left")));
        }
        other.Children.Add(ToneCard(pack, 17, M("Conectare USB / pornire", "USB connect / startup")));
        target.Children.Add(ToneSection(M("Alte acțiuni", "Other actions"), "\uE713", other, false));
    }

    private Border ToneCard(int pack, int toneEvent, string label)
    {
        var config = _configuration!;
        var tone = Choices(ToneChoices(config.GetCustomTone(pack, toneEvent)),
            config.GetCustomTone(pack, toneEvent));
        tone.SelectionChanged += async (_, _) =>
        {
            var next = Selected(tone);
            if (next == config.GetCustomTone(pack, toneEvent)) return;
            tone.IsEnabled = false;
            try
            {
                await MacroSerialBus.Instance.SaveCustomToneAsync(pack, toneEvent, next);
                config.SetCustomTone(pack, toneEvent, next);
            }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
                tone.SelectedItem = tone.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (int)item.Tag == config.GetCustomTone(pack, toneEvent));
            }
            finally { tone.IsEnabled = true; }
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(tone);
        var test = new Button { Content = new FontIcon { Glyph = "\uE768", FontSize = 14 },
            IsEnabled = MacroSerialBus.Instance.SupportsV10 && config.BuzzerEnabled && config.BuzzerVolume > 0 };
        ToolTipService.SetToolTip(test, M("Testează sunetul", "Test sound"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(test, M("Testează sunetul", "Test sound"));
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            try { await MacroSerialBus.Instance.PreviewToneAsync(config.GetCustomTone(pack, toneEvent)); }
            catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
            finally { test.IsEnabled = MacroSerialBus.Instance.SupportsV10 && config.BuzzerEnabled && config.BuzzerVolume > 0; }
        };
        controls.Children.Add(test);
        return SettingCard("\uE8D6", label, M("Frecvență", "Frequency"), controls);
    }

    private void RenderButtons()
    {
        var config = _configuration!;
        _selectedTab = Math.Clamp(_selectedTab, 1, config.TabCount);
        var tabs = new TabView { IsAddTabButtonVisible = config.TabCount < MacroConfiguration.MaxTabs,
            CanDragTabs = MacroSerialBus.Instance.SupportsV9,
            CanReorderTabs = MacroSerialBus.Instance.SupportsV9,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var tab = 1; tab <= config.TabCount; tab++)
        {
            // Keep the selected tab and its content on the same card surface as
            // the macro keys. The programmable RGB color belongs to the LED.
            var content = (Border)XamlReader.Load(
                "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' CornerRadius='8' />");
            content.Padding = new Thickness(12, 2, 12, 8);
            content.Child = BuildTabContent(tab);
            var header = TabHeader(tab, config.GetTabDisplayName(tab), config.GetTabIcon(tab), canClose: true);
            var item = new TabViewItem
            {
                Header = header, Tag = tab,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                IsClosable = false,
                Content = content,
            };
            tabs.TabItems.Add(item);
            if (tab == _selectedTab) tabs.SelectedItem = item;
        }
        tabs.SelectionChanged += async (_, _) =>
        {
            if (tabs.SelectedItem is not TabViewItem { Tag: int tab } || tab == _selectedTab) return;
            var previous = _selectedTab;
            _selectedTab = tab;
            try
            {
                await MacroSerialBus.Instance.SetActiveTabAsync(tab);
                config.ActiveTab = tab;
                UpdateLedPreview();
            }
            catch (Exception error)
            {
                _selectedTab = previous;
                tabs.SelectedItem = tabs.TabItems.OfType<TabViewItem>()
                    .FirstOrDefault(item => item.Tag is int value && value == previous);
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
            }
        };
        tabs.AddTabButtonClick += async (_, _) =>
        {
            if (config.TabCount >= MacroConfiguration.MaxTabs) return;
            var switchSlot = await ChooseTabSwitchButtonAsync();
            if (switchSlot is null) return;
            try
            {
                var nextTab = config.TabCount + 1;
                var switchAction = new MacroAction(MacroActionType.NextTab);
                if (MacroSerialBus.Instance.SupportsV5)
                    await MacroSerialBus.Instance.AddTabWithSwitchAsync(switchSlot);
                else
                {
                    // V4 fallback: install navigation before increasing the tab
                    // count, so an interrupted write cannot create a trapped tab.
                    await MacroSerialBus.Instance.SaveMappingAsync(nextTab, switchSlot, switchAction);
                    for (var tab = 1; tab <= config.TabCount; tab++)
                        if (config.Get(tab, switchSlot) != switchAction)
                            await MacroSerialBus.Instance.SaveMappingAsync(tab, switchSlot, switchAction);
                    await MacroSerialBus.Instance.SaveTabCountAsync(nextTab);
                }
                config.TabCount++;
                config.SetTabColor(nextTab, MacroRgbColor.Default);
                config.SetTabEffect(nextTab, config.GetTabEffect(config.ActiveTab));
                config.SetTabTonePack(nextTab, config.GetTabTonePack(config.ActiveTab));
                config.SetTabIdentity(nextTab, "", 0);
                MacroHostStore.RememberTabCount(nextTab);
                for (var tab = 1; tab <= nextTab; tab++) config.Set(tab, switchSlot, switchAction);
                _selectedTab = config.TabCount;
                await MacroSerialBus.Instance.SetActiveTabAsync(_selectedTab);
                config.ActiveTab = _selectedTab;
                UpdateLedPreview();
                RenderMacroSection();
            }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
                await LoadMacroConfigurationAsync();
            }
        };
        tabs.TabDragCompleted += async (_, args) =>
        {
            if (_tabMutationInProgress || !MacroSerialBus.Instance.SupportsV9 ||
                args.Tab is not TabViewItem { Tag: int from }) return;
            var to = tabs.TabItems.IndexOf(args.Tab) + 1;
            if (to is < 1 or > MacroConfiguration.MaxTabs || from == to) return;
            _tabMutationInProgress = true;
            tabs.CanDragTabs = false;
            try
            {
                await MacroSerialBus.Instance.MoveTabAsync(from, to);
                MacroHostStore.MoveTabAppearance(from, to);
                _configuration = null;
                await LoadMacroConfigurationAsync();
            }
            catch (Exception error)
            {
                _configuration = null;
                await LoadMacroConfigurationAsync();
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
            }
            finally { _tabMutationInProgress = false; }
        };
        ContentHost.Children.Add(tabs);
    }

    private async Task DeleteTabAsync(int closing)
    {
        var config = _configuration;
        if (config is null || _tabMutationInProgress) return;
        if (config.TabCount <= 1)
        {
            ShowMacroMessage(InfoBarSeverity.Informational,
                M("Trebuie să rămână cel puțin un tab.", "At least one tab must remain."));
            return;
        }
        if (!MacroSerialBus.Instance.SupportsV8 && closing != config.TabCount)
        {
            ShowMacroMessage(InfoBarSeverity.Warning,
                M("Actualizează firmware-ul Macro pentru a șterge un tab din mijloc.",
                    "Update Macro firmware to delete a tab from the middle."));
            return;
        }
            var confirm = new ContentDialog { XamlRoot = XamlRoot,
                Title = DialogHeading.Create("\uE74D", M("Șterge tabul", "Delete tab")),
                Content = M($"Ștergi «{config.GetTabDisplayName(closing)}»? Celelalte taburi își păstrează acțiunile și își schimbă poziția.",
                    $"Delete “{config.GetTabDisplayName(closing)}”? Other tabs keep their actions and shift into place."),
                PrimaryButtonText = M("Șterge", "Delete"), CloseButtonText = M("Anulează", "Cancel") };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            _tabMutationInProgress = true;
            try
            {
                if (MacroSerialBus.Instance.SupportsV8)
                    await MacroSerialBus.Instance.DeleteTabAsync(closing);
                else if (closing == config.TabCount)
                    await MacroSerialBus.Instance.SaveTabCountAsync(closing - 1);
                else return;
                MacroHostStore.RemoveTabAppearance(closing, config.TabCount - 1);
                _configuration = null;
                await LoadMacroConfigurationAsync();
            }
            catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
            finally { _tabMutationInProgress = false; }
    }

    private static readonly (string Glyph, string Ro, string En)[] TabIcons =
    [
        ("\uE765", "Tastatură", "Keyboard"), ("\uE7FC", "Jocuri", "Games"),
        ("\uE8D6", "Muzică", "Music"), ("\uE713", "Instrumente", "Tools"),
        ("\uE80F", "Acasă", "Home"), ("\uE714", "Video", "Video"),
        ("\uE943", "Cod", "Code"), ("\uE722", "Cameră", "Camera"),
        ("\uE734", "Preferate", "Favorites"), ("\uE8B7", "Fișiere", "Files"),
    ];

    private static string TabIconGlyph(int icon) => TabIcons[Math.Clamp(icon, 0, TabIcons.Length - 1)].Glyph;

    private Grid TabHeader(int tab, string displayName, int icon, bool canClose,
        Func<Task>? onEdit = null, Func<Task>? onDelete = null, string? glyph = null)
    {
        var header = new Grid { ColumnSpacing = 4, MinWidth = 138, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new FontIcon { Glyph = glyph ?? TabIconGlyph(icon), FontSize = 14 });
        var text = new TextBlock { Text = displayName, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        header.Children.Add(text);
        var edit = TabEditButton(tab, displayName, onEdit);
        Grid.SetColumn(edit, 2);
        header.Children.Add(edit);
        var close = new Button { Content = new FontIcon { Glyph = "\uE8BB", FontSize = 10 },
            Padding = new Thickness(3), MinWidth = 22, MinHeight = 22, IsEnabled = canClose,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(close, canClose ? (onDelete is null ? M("Șterge tabul", "Delete tab") : M("Șterge presetul", "Delete preset")) :
            M("Conectează Macro pentru a șterge tabul", "Connect Macro to delete this tab"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close,
            M($"Șterge {displayName}", $"Delete {displayName}"));
        close.Click += async (_, _) => { if (onDelete is not null) await onDelete(); else await DeleteTabAsync(tab); };
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        return header;
    }

    private Button TabEditButton(int tab, string displayName, Func<Task>? onEdit = null)
    {
        var edit = new Button { Content = new FontIcon { Glyph = "\uE70F", FontSize = 10 },
            Padding = new Thickness(3), MinWidth = 22, MinHeight = 22,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(edit, onEdit is null ? M("Editează tabul", "Edit tab") : M("Redenumește presetul", "Rename preset"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(edit,
            M($"Editează {displayName}", $"Edit {displayName}"));
        edit.Click += async (_, _) => { if (onEdit is not null) await onEdit(); else await EditTabAsync(tab); };
        return edit;
    }

    private void RenderOfflineTabs()
    {
        var saved = MacroHostStore.TabAppearances;
        var tabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false,
            CanReorderTabs = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var tab = 1; tab <= MacroHostStore.LastTabCount; tab++)
        {
            var appearance = tab <= saved.Count ? saved[tab - 1] : new MacroHostTabAppearance();
            var displayName = string.IsNullOrWhiteSpace(appearance.Name) ? $"Tab {tab}" : appearance.Name;
            var header = TabHeader(tab, displayName, appearance.Icon, canClose: false);
            var item = new TabViewItem { Header = header, Tag = tab,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsClosable = false, CornerRadius = new CornerRadius(8, 8, 0, 0),
                Content = Explanation(M("Numele și iconița se salvează acum în Connect și se vor sincroniza cu Macro după actualizarea firmware-ului.",
                    "Name and icon are saved in Connect now and will sync to Macro after the firmware update.")) };
            tabs.TabItems.Add(item);
            if (tab == Math.Clamp(_selectedTab, 1, MacroHostStore.LastTabCount)) tabs.SelectedItem = item;
        }
        ContentHost.Children.Add(tabs);
    }

    private static async Task SyncTabAppearancesAsync(MacroConfiguration config)
    {
        var saved = MacroHostStore.TabAppearances;
        for (var tab = 1; tab <= Math.Min(config.TabCount, saved.Count); tab++)
            await MacroSerialBus.Instance.SaveTabIdentityAsync(tab, saved[tab - 1].Name, saved[tab - 1].Icon);
        MacroHostStore.RememberFirmwareTabAppearances(config);
    }

    private async Task EditTabAsync(int tab)
    {
        var config = _configuration;
        var saved = MacroHostStore.TabAppearances;
        var appearance = tab <= saved.Count ? saved[tab - 1] : new MacroHostTabAppearance();
        var name = new TextBox { Text = config?.GetTabName(tab) ?? appearance.Name,
            PlaceholderText = $"Tab {tab}", MaxLength = 24 };
        var icons = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var index = 0; index < TabIcons.Length; index++)
        {
            var (glyph, ro, en) = TabIcons[index];
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
            label.Children.Add(new TextBlock { Text = M(ro, en), VerticalAlignment = VerticalAlignment.Center });
            icons.Items.Add(new ComboBoxItem { Content = label, Tag = index });
        }
        icons.SelectedIndex = config?.GetTabIcon(tab) ?? appearance.Icon;
        var form = new StackPanel { Spacing = 12, MinWidth = 300 };
        form.Children.Add(new TextBlock { Text = M("Numele tabului", "Tab name") });
        form.Children.Add(name);
        form.Children.Add(new TextBlock { Text = M("Iconiță", "Icon") });
        form.Children.Add(icons);
        var dialog = new ContentDialog { XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE70F", M("Personalizează tabul", "Customize tab")),
            Content = form, PrimaryButtonText = M("Aplică", "Apply"),
            CloseButtonText = M("Anulează", "Cancel") };
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled =
            System.Text.Encoding.UTF8.GetByteCount(name.Text.Trim()) <= 24;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var nextName = name.Text.Trim();
            var nextIcon = Selected(icons);
            MacroHostStore.SetTabAppearance(tab, nextName, nextIcon, needsFirmwareSync: true);
            config?.SetTabIdentity(tab, nextName, nextIcon);
            if (config is not null && MacroSerialBus.Instance.IsConnected && MacroSerialBus.Instance.SupportsV8)
            {
                try { await SyncTabAppearancesAsync(config); }
                catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Warning, error.Message); }
            }
            else ShowMacroMessage(InfoBarSeverity.Informational,
                M("Tabul a fost personalizat în Connect. Numele și iconița se vor sincroniza după actualizarea firmware-ului Macro.",
                    "Tab customized in Connect. Name and icon will sync after the Macro firmware update."));
            RenderMacroSection();
        }
        catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
    }

    private FrameworkElement BuildTabContent(int tab)
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(TabSection(tab, "buttons", "\uE765", M("Configurare butoane", "Button configuration"), BuildSlotList(tab)));
        if (_configuration!.LightingEnabled) panel.Children.Add(TabSection(tab, "lighting", "\uE706", M("Iluminare", "Lighting"), BuildTabLighting(tab)));
        if (_configuration.BuzzerEnabled) panel.Children.Add(TabSection(tab, "buzzer", "\uE767", M("Temă buzzer", "Buzzer theme"), BuildTabBuzzer(tab)));
        return panel;
    }

    private readonly Dictionary<string, bool> _tabSectionExpansion = [];

    private Expander TabSection(int tab, string key, string glyph, string title, FrameworkElement content)
    {
        var id = $"{tab}:{key}";
        var expander = ClockNativeSettingsPage.SettingsExpander(glyph, title, null, null, [content],
            _tabSectionExpansion.GetValueOrDefault(id, true));
        expander.Expanding += (_, _) => _tabSectionExpansion[id] = true;
        expander.Collapsed += (_, _) => _tabSectionExpansion[id] = false;
        return expander;
    }

    private FrameworkElement BuildTabLighting(int tab)
    {
        var config = _configuration!;
        var rows = new StackPanel { Spacing = 8, Padding = new Thickness(16) };
        if (config.SupportsSplitLighting)
        {
            if (config.HasAddressableLighting) rows.Children.Add(OutputEffectCard(true, tab, false));
            if (config.HasExternalLighting) rows.Children.Add(OutputEffectCard(false, tab, false));
        }
        else
        {
        var effects = Choices([
            (M("Static", "Static"), 0), (M("Respirație", "Breathing"), 1),
            (M("Clipire", "Blink"), 2), (M("Puls dublu", "Double pulse"), 3),
            (M("Bătăi de inimă", "Heartbeat"), 4), (M("Val", "Wave"), 5),
            (M("Sclipiri", "Sparkle"), 6), (M("Stroboscop", "Strobe"), 7),
        ], config.GetTabEffect(tab));
        effects.IsEnabled = MacroSerialBus.Instance.SupportsV7;
        effects.SelectionChanged += async (_, _) =>
        {
            var next = Selected(effects);
            if (next == config.GetTabEffect(tab)) return;
            effects.IsEnabled = false;
            try
            {
                await MacroSerialBus.Instance.SaveTabEffectAsync(tab, next);
                config.SetTabEffect(tab, next);
                if (config.ActiveTab == tab) UpdateLedPreview();
            }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
                effects.SelectedItem = effects.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (int)item.Tag == config.GetTabEffect(tab));
            }
            finally { effects.IsEnabled = true; }
        };
        rows.Children.Add(SettingCard("\uE781", M("Model de iluminare", "Lighting effect"),
            M("Efectul salvat pentru acest tab.", "The effect saved for this tab."), effects));
        if (!MacroSerialBus.Instance.SupportsV7)
            rows.Children.Add(Explanation(M("Actualizează firmware-ul Macro pentru efecte separate pe tab.",
                "Update Macro firmware for per-tab lighting effects.")));
        }
        if (MacroSerialBus.Instance.SupportsV6 && config.HasAddressableLighting)
        {
            var color = config.GetTabColor(tab);
            var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(DisplayColor(color)) };
            var label = new TextBlock { Text = ColorHex(color), VerticalAlignment = VerticalAlignment.Center };
            var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            caption.Children.Add(swatch);
            caption.Children.Add(label);
            var choose = new Button { Content = caption, MinWidth = 140 };
            choose.Click += async (_, _) =>
            {
                var next = await ChooseTabColorAsync(tab, config.GetTabColor(tab));
                if (next is null || next.Value == config.GetTabColor(tab)) return;
                choose.IsEnabled = false;
                try
                {
                    await MacroSerialBus.Instance.SaveTabColorAsync(tab, next.Value);
                    config.SetTabColor(tab, next.Value);
                    swatch.Background = new SolidColorBrush(DisplayColor(next.Value));
                    label.Text = ColorHex(next.Value);
                    if (config.ActiveTab == tab) UpdateLedPreview();
                }
                catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
                finally { choose.IsEnabled = true; }
            };
            rows.Children.Add(SettingCard("\uE790", M("Culoarea LED-ului adresabil", "Addressable LED color"),
                M("Culoarea apare când acest tab este activ.", "Shown when this tab is active."), choose));
        }
        return rows;
    }

    private FrameworkElement BuildTabBuzzer(int tab)
    {
        var config = _configuration!;
        var rows = new StackPanel { Spacing = 8, Padding = new Thickness(16) };
        var pack = Choices(TonePackChoices(), config.GetTabTonePack(tab));
        pack.IsEnabled = MacroSerialBus.Instance.SupportsV8;
        pack.SelectionChanged += async (_, _) =>
        {
            var next = Selected(pack);
            if (next == config.GetTabTonePack(tab)) return;
            pack.IsEnabled = false;
            try
            {
                await MacroSerialBus.Instance.SaveTabTonePackAsync(tab, next);
                config.SetTabTonePack(tab, next);
            }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
                pack.SelectedItem = pack.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (int)item.Tag == config.GetTabTonePack(tab));
            }
            finally { pack.IsEnabled = MacroSerialBus.Instance.SupportsV8; }
        };
        rows.Children.Add(SettingCard("\uE767", M("Temă Buzzer", "Buzzer theme"),
            M("Presetul folosit de tastele acestui tab.", "Tone pack used by keys on this tab."), pack));
        if (config.GetTabTonePack(tab) >= 5)
            rows.Children.Add(Explanation(M("Editează tonurile personalizate în categoria Buzzer.",
                "Edit custom tones in the main Buzzer category.")));
        return rows;
    }

    private (string Label, int Value)[] TonePackChoices() => new (string Label, int Value)[]
    {
        (M("Clasic", "Classic"), 0), (M("Delicat", "Soft"), 1),
        (M("Luminos", "Bright"), 2), (M("Arcade", "Arcade"), 3),
        (M("Grav", "Low"), 4),
    }.Concat(Enumerable.Range(1, 5)
        .Where(pack => !string.IsNullOrEmpty(_configuration!.GetCustomToneName(pack)))
        .Select(pack => (CustomToneDisplayName(pack), pack + 4))).ToArray();

    private StackPanel BuildSlotList(int tab)
    {
        var config = _configuration!;
        var rows = new StackPanel { Spacing = 12, Padding = new Thickness(12, 14, 12, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var assigned = MacroConfiguration.Slots.Take(8)
            .Count(slot => config.Get(tab, slot).Type != MacroActionType.None);
        var toolbar = new Grid { ColumnSpacing = 16, Margin = new Thickness(4, 2, 4, 6) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = M($"{assigned} din 8 taste configurate · Apasă o tastă pentru a-i schimba acțiunea.",
                $"{assigned} of 8 keys assigned · Select a key to change its action."),
            TextWrapping = TextWrapping.Wrap, Opacity = 0.72,
        });
        toolbar.Children.Add(title);
        rows.Children.Add(toolbar);
        if (config.TabCount > 1 && !HasTabSwitch(config, tab))
        {
            var notice = new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning,
                Title = M("Lipsește tasta de schimbare a tabului", "No tab-switch key"),
                Message = M("Alege una dintre taste și atribuie-i «Tabul următor», «Tabul anterior» sau alt tab.",
                    "Choose a key and assign Next tab, Previous tab or another tab.") };
            rows.Children.Add(notice);
        }
        var pad = new Grid { ColumnSpacing = 10, RowSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 2, 4, 4) };
        var keySlots = new[] { "b1", "b2", "b3", "knob", "b4", "b5", "b6", "b7" };
        for (var index = 0; index < keySlots.Length; index++)
        {
            var slot = keySlots[index];
            var name = slot == "knob" ? config.HasEncoder ? "K1" : "B8" : slot.ToUpperInvariant();
            pad.Children.Add(CreateActionCard(tab, slot, name));
        }
        void ArrangeKeys(double width)
        {
            var columns = width >= 680 ? 4 : width >= 420 ? 2 : 1;
            if (pad.ColumnDefinitions.Count == columns) return;
            pad.ColumnDefinitions.Clear();
            pad.RowDefinitions.Clear();
            for (var index = 0; index < columns; index++)
                pad.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < (keySlots.Length + columns - 1) / columns; index++)
                pad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var index = 0; index < pad.Children.Count; index++)
            {
                var key = (FrameworkElement)pad.Children[index];
                Grid.SetRow(key, index / columns);
                Grid.SetColumn(key, index % columns);
            }
        }
        ArrangeKeys(900);
        pad.SizeChanged += (_, _) => ArrangeKeys(pad.ActualWidth);
        rows.Children.Add(pad);
        if (config.HasEncoder)
        {
            rows.Children.Add(Heading(M("Rotirea encoderului", "Encoder rotation")));
            var encoder = new Grid { ColumnSpacing = 10, RowSpacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 4, 0) };
            encoder.Children.Add(CreateActionCard(tab, "ccw", M("Rotire la stânga", "Rotate left")));
            encoder.Children.Add(CreateActionCard(tab, "cw", M("Rotire la dreapta", "Rotate right")));
            void ArrangeEncoder(double width)
            {
                var columns = width >= 420 ? 2 : 1;
                if (encoder.ColumnDefinitions.Count == columns) return;
                encoder.ColumnDefinitions.Clear();
                encoder.RowDefinitions.Clear();
                for (var index = 0; index < columns; index++)
                    encoder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (var index = 0; index < 2 / columns; index++)
                    encoder.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var index = 0; index < encoder.Children.Count; index++)
                {
                    Grid.SetRow((FrameworkElement)encoder.Children[index], index / columns);
                    Grid.SetColumn((FrameworkElement)encoder.Children[index], index % columns);
                }
            }
            ArrangeEncoder(900);
            encoder.SizeChanged += (_, _) => ArrangeEncoder(encoder.ActualWidth);
            rows.Children.Add(encoder);
        }
        return rows;
    }

    private Button CreateActionCard(int tab, string slot, string label)
    {
        var action = _configuration!.Get(tab, slot);
        var content = new Grid { RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12, Opacity = 0.8 });
        var icon = new FontIcon { Glyph = ActionGlyph(action), FontSize = 17, Opacity = 0.76 };
        Grid.SetColumn(icon, 1);
        content.Children.Add(icon);
        var actionText = new TextBlock { Text = ActionTitle(action), FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2 };
        Grid.SetRow(actionText, 1);
        Grid.SetColumnSpan(actionText, 2);
        content.Children.Add(actionText);
        var kind = new TextBlock { Text = ActionKind(action), FontSize = 11, Opacity = 0.66 };
        Grid.SetRow(kind, 2);
        Grid.SetColumnSpan(kind, 2);
        content.Children.Add(kind);
        var button = new Button { Content = content, MinHeight = 112,
            Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        if (IsTabSwitch(action, tab)) button.BorderBrush = WindowsAccentColor.GetAccentBrush();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,
            $"{label}, {ActionTitle(action)}, {ActionKind(action)}");
        ToolTipService.SetToolTip(button, M("Apasă pentru a configura", "Select to configure"));
        button.Click += async (_, _) => await ShowActionEditorAsync(tab, slot, label);
        return button;
    }

    private static string ActionTitle(MacroAction action) => action.Type switch
    {
        MacroActionType.None => M("Alege o acțiune", "Choose an action"),
        MacroActionType.Key => KeyboardActionTitle(action),
        MacroActionType.Media => MediaChoices().FirstOrDefault(choice => choice.Value == action.Value).Label
            ?? M("Control multimedia", "Media control"),
        MacroActionType.Scroll => action.Key1 == 1 ? M("Scroll în sus", "Scroll up") : M("Scroll în jos", "Scroll down"),
        MacroActionType.NextTab => M("Tabul următor", "Next tab"),
        MacroActionType.PreviousTab => M("Tabul anterior", "Previous tab"),
        MacroActionType.GoToTab => M($"Mergi la tabul {action.Key1}", $"Go to tab {action.Key1}"),
        MacroActionType.Application => MacroHostStore.Find(action.Value) is { } app ?
            (app.IsClock ? MacroClockControl.Label(app.ClockCommand) : app.Name) :
            M("Aplicație lipsă", "Missing application"),
        _ => M("Acțiune necunoscută", "Unknown action"),
    };

    private static string KeyboardActionTitle(MacroAction action)
    {
        var keys = new List<string>();
        if ((action.Modifiers & 1) != 0) keys.Add("Ctrl");
        if ((action.Modifiers & 2) != 0) keys.Add("Shift");
        if ((action.Modifiers & 4) != 0) keys.Add("Alt");
        if ((action.Modifiers & 8) != 0) keys.Add("Win");
        if (action.Key1 != 0) keys.Add(KeyChoices(false).FirstOrDefault(choice => choice.Value == action.Key1).Label ?? $"#{action.Key1}");
        if (action.Key2 != 0) keys.Add(KeyChoices(false).FirstOrDefault(choice => choice.Value == action.Key2).Label ?? $"#{action.Key2}");
        return keys.Count == 0 ? M("Tastă necunoscută", "Unknown key") : string.Join(" + ", keys);
    }

    private static string ActionKind(MacroAction action) => action.Type switch
    {
        MacroActionType.None => M("Neconfigurat", "Not assigned"),
        MacroActionType.Key when action.Modifiers != 0 || action.Key2 != 0 => M("Scurtătură", "Shortcut"),
        MacroActionType.Key => M("Tastatură", "Keyboard"),
        MacroActionType.Media => M("Multimedia", "Media"),
        MacroActionType.Scroll => "Scroll",
        MacroActionType.NextTab or MacroActionType.PreviousTab or MacroActionType.GoToTab =>
            M("Navigare între taburi", "Tab navigation"),
        MacroActionType.Application => MacroHostStore.Find(action.Value) is { IsClock: true } clock ? clock.Name : M("Aplicație", "Application"),
        _ => M("Altă acțiune", "Other action"),
    };

    private static string ActionGlyph(MacroAction action) => action.Type switch
    {
        MacroActionType.Key => "\uE765",
        MacroActionType.Media => "\uE767",
        MacroActionType.Scroll => "\uE8AB",
        MacroActionType.NextTab or MacroActionType.PreviousTab or MacroActionType.GoToTab => "\uE72A",
        MacroActionType.Application => MacroHostStore.Find(action.Value)?.IsClock == true ? "\uE917" : "\uE8B7",
        _ => "\uE70F",
    };

    private async Task<MacroRgbColor?> ChooseTabColorAsync(int tab, MacroRgbColor current)
    {
        var picker = new ColorPicker { Color = DisplayColor(current), IsAlphaEnabled = false,
            Width = 340, HorizontalAlignment = HorizontalAlignment.Center };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE790", M($"Culoarea tabului {tab}", $"Tab {tab} color")),
            Content = picker,
            PrimaryButtonText = M("Salvează", "Save"),
            CloseButtonText = M("Anulează", "Cancel"),
        };
        dialog.Resources["ContentDialogMaxWidth"] = 460;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var color = picker.Color;
        return new MacroRgbColor(color.R, color.G, color.B);
    }

    private static bool IsTabSwitch(MacroAction action, int tab) => action.Type switch
    {
        MacroActionType.NextTab or MacroActionType.PreviousTab => true,
        MacroActionType.GoToTab => action.Key1 != tab,
        _ => false,
    };

    private static bool HasTabSwitch(MacroConfiguration config, int tab, string? exceptSlot = null) =>
        MacroConfiguration.Slots.Take(8).Any(slot => slot != exceptSlot && IsTabSwitch(config.Get(tab, slot), tab));

    private async Task<string?> ChooseTabSwitchButtonAsync()
    {
        var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = M("Alege o tastă", "Choose a key") };
        foreach (var slot in MacroConfiguration.Slots.Take(8))
            selector.Items.Add(new ComboBoxItem { Content = slot == "knob" ? _configuration?.HasEncoder == false ? "B8" : "K1" : slot.ToUpperInvariant(), Tag = slot });
        var warning = Explanation(M(
            "Această tastă va trece la tabul următor pe fiecare tab, ca să le poți accesa pe toate. Acțiunea ei actuală de pe fiecare tab va fi înlocuită.",
            "This key will go to the next tab on every tab, so you can reach them all. Its current action on each tab will be replaced."));
        var content = new StackPanel { MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Stretch, Spacing = 12 };
        content.Children.Add(warning);
        content.Children.Add(selector);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE7F4", M("Adaugă un tab", "Add a tab")),
            Content = content,
            PrimaryButtonText = M("Adaugă", "Add"),
            CloseButtonText = M("Anulează", "Cancel"),
            IsPrimaryButtonEnabled = false,
        };
        selector.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = selector.SelectedItem is not null;
        dialog.Resources["ContentDialogMaxWidth"] = 504;
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? (selector.SelectedItem as ComboBoxItem)?.Tag as string : null;
    }

    private static string DescribeAction(MacroAction action) => action.Type switch
    {
        MacroActionType.None => M("Fără acțiune", "No action"),
        MacroActionType.Key => M("Tastă / combinație de taste", "Key / shortcut"),
        MacroActionType.Media => M("Control multimedia", "Media control"),
        MacroActionType.Scroll => action.Key1 == 1 ? M("Scroll în sus", "Scroll up") : M("Scroll în jos", "Scroll down"),
        MacroActionType.NextTab => M("Tabul următor", "Next tab"),
        MacroActionType.PreviousTab => M("Tabul anterior", "Previous tab"),
        MacroActionType.GoToTab => M($"Mergi la tabul {action.Key1}", $"Go to tab {action.Key1}"),
        MacroActionType.Application => MacroHostStore.Find(action.Value) is { } app
            ? (app.IsClock ? $"{app.Name} · {MacroClockControl.Label(app.ClockCommand)}" :
                M($"Deschide {app.Name} (doar cu Connect pornit)", $"Open {app.Name} (Connect required)"))
            : M($"Aplicația #{action.Value} nu mai este configurată", $"Application #{action.Value} is no longer configured"),
        _ => M("Acțiune necunoscută", "Unknown action"),
    };

    private void RenderIntegrations()
    {
        if (_integrationPage == IntegrationPage.Overview)
        {
            ContentHost.Children.Add(Heading(M("Integrări", "Integrations")));
            ContentHost.Children.Add(IntegrationLink("\uE772", M("Dispozitive Octoglow", "Octoglow devices"), IntegrationPage.Devices));
            ContentHost.Children.Add(IntegrationLink("\uE8B7", M("Aplicații", "Applications"), IntegrationPage.Applications));
            return;
        }
        ContentHost.Children.Add(CreateIntegrationHeader(_integrationPage));
        if (_integrationPage == IntegrationPage.Applications) RenderApplications();
        else RenderOctoglowIntegration();
    }

    private Button IntegrationLink(string glyph, string title, IntegrationPage page)
        => ClockNativeSettingsPage.NavCard(glyph, title, null, () => NavigateIntegration(page));

    private void NavigateIntegration(IntegrationPage page)
    {
        if (page != IntegrationPage.Overview && (!ConfigStore.InlineClockConfigurationPages || MacroShiftOpensPanel()))
        {
            OpenMacroPanel(page);
            return;
        }
        _integrationPage = page;
        MacroContentScroll.ChangeView(null, 0, null);
        RenderMacroSection();
        AnimateMacroNavigation(page != IntegrationPage.Overview);
    }

    private void RenderOctoglowIntegration()
    {
        var permission = new ToggleSwitch { IsOn = MacroHostStore.AllowOtherOctoglowDevices,
            OnContent = "", OffContent = "" };
        permission.Toggled += (_, _) =>
        {
            if (permission.IsOn == MacroHostStore.AllowOtherOctoglowDevices) return;
            try { MacroHostStore.AllowOtherOctoglowDevices = permission.IsOn; }
            catch (Exception error)
            {
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
                permission.IsOn = MacroHostStore.AllowOtherOctoglowDevices;
            }
        };
        IntegrationContent.Children.Add(SettingCard("\uE772",
            M("Permite interacțiunea cu alte dispozitive Octoglow", "Allow interaction with other Octoglow devices"),
            M("Permite tastelor să controleze ceasul salvat în Connect. Alege dispozitivul și comanda în configurarea tastei; Connect trebuie să ruleze.",
              "Allows keys to control the clock saved in Connect. Choose a device and command in the key editor; Connect must be running."), permission));
    }

    private void RenderApplications()
    {
        IntegrationContent.Children.Add(Explanation(M("Căile aplicațiilor rămân numai pe acest calculator. Macro salvează în firmware doar ID-ul ales; lansarea funcționează când Octoglow Connect rulează și este conectat la canalul USB.",
            "Application paths stay on this computer. Macro stores only the selected ID in firmware; launching works while Octoglow Connect is running and the USB channel is connected.")));
        foreach (var app in MacroHostStore.Applications)
        {
            var remove = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                Width = 32, Height = 32, Padding = new Thickness(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, M("Șterge ", "Remove ") + app.Name);
            ToolTipService.SetToolTip(remove, M("Șterge", "Remove"));
            remove.Click += async (_, _) => await RemoveApplicationAsync(app);
            IntegrationContent.Children.Add(SettingCard("\uE8B7", $"{app.Name}  ·  #{app.Id}", app.Path, remove));
        }
        var add = new Button { Content = M("Adaugă o aplicație", "Add an application"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 12, 0, 0) };
        add.Click += async (_, _) => await AddApplicationAsync();
        IntegrationContent.Children.Add(add);
    }

    private async Task AddApplicationAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".lnk");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            MacroHostStore.Add(System.IO.Path.GetFileNameWithoutExtension(file.Path), file.Path);
            RenderMacroSection();
        }
        catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
    }

    private async Task RemoveApplicationAsync(MacroHostApplication app)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = M("Ștergi aplicația?", "Remove application?"),
            Content = M($"{app.Name} nu va mai putea fi deschisă de Macro. ID-ul #{app.Id} rămâne pe butoanele care îl folosesc până le schimbi maparea.",
                $"Macro will no longer open {app.Name}. ID #{app.Id} remains on its buttons until you change their mapping."),
            PrimaryButtonText = M("Șterge", "Remove"),
            CloseButtonText = M("Anulează", "Cancel"),
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        MacroHostStore.Remove(app.Id);
        RenderMacroSection();
    }
}
