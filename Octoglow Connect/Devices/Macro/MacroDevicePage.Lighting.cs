using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OctoglowSender.Devices.Macro.Models;
using OctoglowSender.Devices.Macro.Services;

namespace OctoglowSender.Devices.Macro;

public sealed partial class MacroDevicePage
{
    private string OutputLabel(bool addressable) => addressable
        ? M("LED adresabil", "Addressable LED") : M("LED-uri externe", "External LEDs");

    private void RenderSplitLighting()
    {
        var config = _configuration!;
        if (!config.LightingEnabled) return;
        if (config.HasAddressableLighting && config.HasExternalLighting)
        {
            ContentHost.Children.Add(SplitBrightnessExpander());
        }
        else ContentHost.Children.Add(OutputBrightnessCard(config.HasAddressableLighting));
        ContentHost.Children.Add(Heading(M("Efecte de iluminare", "Lighting effects")));
        if (config.HasAddressableLighting) ContentHost.Children.Add(OutputEffectCard(true, _selectedTab, true));
        if (config.HasExternalLighting) ContentHost.Children.Add(OutputEffectCard(false, _selectedTab, true));
    }

    private FrameworkElement SplitBrightnessExpander()
    {
        var config = _configuration!;
        var desired = new[] { config.AddressableBrightness, config.LedBrightness };
        var confirmed = (int[])desired.Clone();
        var sliders = new Slider[3];
        var labels = new TextBlock[3];
        var syncing = false;
        var running = false;
        void Refresh()
        {
            syncing = true;
            for (var i = 0; i < 3; i++)
            {
                var value = i == 2 ? Math.Max(desired[0], desired[1]) : desired[i];
                sliders[i].Value = value;
                labels[i].Text = $"{value}%";
            }
            config.AddressableBrightness = desired[0];
            config.LedBrightness = desired[1];
            syncing = false;
            UpdateLedPreview();
        }
        async Task FlushAsync()
        {
            if (running) return;
            running = true;
            try
            {
                while (!desired.SequenceEqual(confirmed))
                {
                    await Task.Delay(100);
                    if (!ReferenceEquals(config, _configuration) || !_isLoaded) return;
                    for (var output = 0; output < 2; output++)
                    {
                        var next = desired[output];
                        if (next == confirmed[output]) continue;
                        await MacroSerialBus.Instance.SaveOutputBrightnessAsync(output == 0, next);
                        confirmed[output] = next;
                    }
                }
            }
            catch (Exception error)
            {
                Array.Copy(confirmed, desired, 2);
                Refresh();
                ShowMacroMessage(InfoBarSeverity.Error, error.Message);
            }
            finally { running = false; }
        }
        FrameworkElement Control(int index)
        {
            var value = index == 2 ? Math.Max(desired[0], desired[1]) : desired[index];
            var slider = sliders[index] = new Slider { Minimum = 0, Maximum = 100,
                StepFrequency = 1, Width = 200, Value = value };
            AutomationProperties.SetName(slider, index == 2 ? M("Luminozitate generală", "Master brightness") : OutputLabel(index == 0));
            var label = labels[index] = new TextBlock { Text = $"{value}%", MinWidth = 42,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = Microsoft.UI.Xaml.TextAlignment.Right };
            slider.ValueChanged += async (_, args) =>
            {
                if (syncing) return;
                if (index == 2) desired[0] = desired[1] = (int)Math.Round(args.NewValue);
                else desired[index] = (int)Math.Round(args.NewValue);
                Refresh();
                await FlushAsync();
            };
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { slider, label } };
        }
        var rgb = Control(0);
        var external = Control(1);
        var master = Control(2);
        return ClockNativeSettingsPage.SettingsExpander("\uE9E9", M("Luminozitate", "Brightness"),
            M("Reglează ambele ieșiri sau extinde pentru control separat.", "Adjust both outputs or expand for separate control."), master,
            [ClockNativeSettingsPage.SubRow(OutputLabel(true), null, rgb),
             ClockNativeSettingsPage.SubRow(OutputLabel(false), null, external)]);
    }

    private FrameworkElement OutputBrightnessCard(bool addressable)
    {
        var config = _configuration!;
        var value = addressable ? config.AddressableBrightness : config.LedBrightness;
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, Width = 172, Value = value };
        AutomationProperties.SetName(slider, M("Luminozitate - ", "Brightness - ") + OutputLabel(addressable));
        var amount = new TextBlock { Text = $"{value}%", MinWidth = 38, VerticalAlignment = VerticalAlignment.Center };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(slider);
        controls.Children.Add(amount);
        BindLiveSlider(slider, amount, level => MacroSerialBus.Instance.SaveOutputBrightnessAsync(addressable, level), level =>
        {
            if (addressable) config.AddressableBrightness = level;
            else config.LedBrightness = level;
            UpdateLedPreview();
        });
        return SettingCard("\uE706", OutputLabel(addressable),
            M("Luminozitatea acestei ieșiri, pentru toate taburile.", "Brightness of this output across all tabs."), controls);
    }

    private FrameworkElement OutputEffectCard(bool addressable, int tab, bool chooseTarget)
    {
        var config = _configuration!;
        int Current() => addressable ? config.GetTabRgbEffect(tab) : config.GetTabEffect(tab);
        void Set(int target, int value)
        {
            if (addressable) config.SetTabRgbEffect(target, value);
            else config.SetTabEffect(target, value);
        }
        var effect = Choices([
            (M("Static", "Static"), 0), (M("Respirație", "Breathing"), 1),
            (M("Clipire", "Blink"), 2), (M("Puls dublu", "Double pulse"), 3),
            (M("Bătăi de inimă", "Heartbeat"), 4), (M("Val", "Wave"), 5),
            (M("Sclipiri", "Sparkle"), 6), (M("Stroboscop", "Strobe"), 7)
        ], Current());
        AutomationProperties.SetName(effect, M("Efect - ", "Effect - ") + OutputLabel(addressable));
        var updating = false;
        effect.SelectionChanged += async (_, _) =>
        {
            if (updating || Selected(effect) == Current()) return;
            updating = true;
            effect.IsEnabled = false;
            var next = Selected(effect);
            var target = tab;
            try
            {
                if (chooseTarget && config.TabCount > 1)
                {
                    var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    choice.Items.Add(new ComboBoxItem { Content = M("Toate taburile", "All tabs"), Tag = 0 });
                    for (var index = 1; index <= config.TabCount; index++)
                        choice.Items.Add(new ComboBoxItem { Content = config.GetTabDisplayName(index), Tag = index });
                    choice.SelectedIndex = tab;
                    var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                        Title = M("Aplică efectul - ", "Apply effect - ") + OutputLabel(addressable), Content = choice,
                        PrimaryButtonText = M("Aplică", "Apply"), CloseButtonText = M("Anulează", "Cancel") };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    target = Selected(choice);
                }
                if (!ReferenceEquals(config, _configuration)) return;
                await MacroSerialBus.Instance.SaveOutputEffectAsync(addressable, target, next);
                if (target == 0)
                    for (var index = 1; index <= MacroConfiguration.MaxTabs; index++) Set(index, next);
                else Set(target, next);
                UpdateLedPreview();
            }
            catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, error.Message); }
            finally
            {
                effect.SelectedItem = effect.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (int)item.Tag == Current());
                effect.IsEnabled = true;
                updating = false;
            }
        };
        return SettingCard("\uE781", OutputLabel(addressable), chooseTarget
            ? M("Alege efectul și taburile pe care se aplică.", "Choose the effect and which tabs use it.")
            : M("Efectul salvat pentru acest tab.", "The effect saved for this tab."), effect);
    }
}
