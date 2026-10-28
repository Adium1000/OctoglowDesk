using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using OctoglowSender.Devices.Macro.Services;
using Windows.System;
using Windows.UI.Core;

namespace OctoglowSender.Devices.Macro;

public sealed partial class MacroDevicePage
{
    private static readonly Windows.UI.ViewManagement.UISettings MotionSettings = new();
    private int _macroSectionIndex;
    private int _macroNavigationVersion;
    private Popup? _macroPanel;
    private StackPanel? _macroPanelContent;
    private InfoBar? _macroPanelStatus;
    private IntegrationPage? _macroPanelIntegration;
    private Action? _detachMacroPanel;
    private StackPanel IntegrationContent => _macroPanelContent ?? ContentHost;

    private static bool MacroShiftOpensPanel() => InputKeyboardSource
        .GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    private static (string Title, string Description, string Glyph) IntegrationHeader(IntegrationPage page) =>
        page == IntegrationPage.CustomTones
            ? (M("Tonuri Custom", "Custom tones"),
                M("Creează până la 5 preseturi de sunet, independente de taburile butoanelor.",
                    "Create up to 5 sound presets, independent of your button tabs."), "\uE8D6")
        : page == IntegrationPage.Devices
            ? (M("Dispozitive Octoglow", "Octoglow devices"),
                M("Interacțiunea dintre Macro și celelalte dispozitive Octoglow.",
                    "Interaction between Macro and other Octoglow devices."), "\uE772")
            : (M("Aplicații", "Applications"),
                M("Aplicațiile pe care le poți deschide cu butoanele Macro cât timp Connect rulează.",
                    "Applications you can open with Macro buttons while Connect is running."), "\uE8B7");

    private FrameworkElement CreateIntegrationHeader(IntegrationPage page)
    {
        var (title, description, glyph) = IntegrationHeader(page);
        var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Same default Button template and transparent resting state as Clock.
        var back = (Button)XamlReader.Load(
            "<Button xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "Padding='10,8' VerticalAlignment='Center' Background='Transparent' BorderBrush='Transparent'>" +
            "<FontIcon Glyph='&#xE72B;' FontSize='16'/></Button>");
        var backLabel = M("Înapoi", "Back");
        ToolTipService.SetToolTip(back, backLabel);
        AutomationProperties.SetName(back, backLabel);
        back.Click += (_, _) => NavigateIntegration(IntegrationPage.Overview);
        header.Children.Add(back);
        var icon = new FontIcon { Glyph = glyph, FontSize = 24, Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 1);
        header.Children.Add(icon);
        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        copy.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });
        Grid.SetColumn(copy, 2);
        header.Children.Add(copy);
        return header;
    }

    private void ResetMacroNavigationMotion()
    {
        ++_macroNavigationVersion;
        ContentHost.TranslationTransition = null;
        ContentHost.OpacityTransition = null;
        ContentHost.Translation = Vector3.Zero;
        ContentHost.Opacity = 1;
    }

    private void AnimateMacroNavigation(bool forward)
    {
        ResetMacroNavigationMotion();
        if (!MotionSettings.AnimationsEnabled) return;
        var version = _macroNavigationVersion;
        ContentHost.Translation = new Vector3(forward ? 44 : -44, 0, 0);
        ContentHost.Opacity = 0;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded || version != _macroNavigationVersion) return;
            ContentHost.TranslationTransition = new Vector3Transition { Duration = ClockNativeSettingsPage.NavigationAnimation };
            ContentHost.OpacityTransition = new ScalarTransition { Duration = ClockNativeSettingsPage.NavigationAnimation };
            ContentHost.Translation = Vector3.Zero;
            ContentHost.Opacity = 1;
        })) ResetMacroNavigationMotion();
    }

    private void OpenMacroPanel(IntegrationPage? integration)
    {
        var root = XamlRoot;
        if (root is null || _macroPanel is not null) return;
        var overlay = (Grid)XamlReader.Load(ClockNativeSettingsPage.PanelXaml);
        T Part<T>(string name) where T : class => (T)overlay.FindName(name);
        var card = Part<Border>("Card");
        card.Shadow = new ThemeShadow();
        card.Translation = new Vector3(0, 0, 32);
        Part<TextBlock>("Title").Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"];
        Part<Button>("Refresh").Visibility = Visibility.Collapsed;
        Part<ScrollViewer>("Scroller").HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var close = Part<Button>("Close");
        close.Click += (_, _) => CloseMacroPanel();
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            CloseMacroPanel();
        };
        void Fit()
        {
            overlay.Width = root.Size.Width;
            overlay.Height = root.Size.Height;
            card.Width = Math.Max(1, Math.Min(integration is null ? 640 : 880, root.Size.Width - 64));
            card.MaxHeight = Math.Max(1, root.Size.Height - 64);
            // Auto height, unlike the Clock's much longer About page.
            card.Height = double.NaN;
        }
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args) => Fit();
        void ThemeChanged(FrameworkElement sender, object args) => overlay.RequestedTheme = ActualTheme;
        Fit();
        overlay.RequestedTheme = ActualTheme;
        root.Changed += Resized;
        ActualThemeChanged += ThemeChanged;
        var opener = FocusManager.GetFocusedElement(root) as Control;
        _detachMacroPanel = () => { root.Changed -= Resized; ActualThemeChanged -= ThemeChanged; };
        var popup = new Popup { XamlRoot = root, Child = overlay };
        popup.Closed += (_, _) =>
        {
            // A superseding popup must retain focus during rapid close/open.
            if (_isLoaded && _macroPanel is null && popup.Tag is true) opener?.Focus(FocusState.Programmatic);
        };
        _macroPanel = popup;
        _macroPanelIntegration = integration;
        _macroPanelContent = Part<StackPanel>("Content");
        _macroPanelStatus = Part<InfoBar>("Status");
        RebuildMacroPanel();
        var animate = MotionSettings.AnimationsEnabled;
        overlay.Opacity = animate ? 0 : 1;
        overlay.OpacityTransition = animate ? new ScalarTransition { Duration = ClockNativeSettingsPage.PanelAnimation } : null;
        card.Scale = animate ? new Vector3(0.96f, 0.96f, 1) : Vector3.One;
        card.ScaleTransition = animate ? new Vector3Transition { Duration = ClockNativeSettingsPage.PanelAnimation } : null;
        card.SizeChanged += (_, _) => card.CenterPoint = new Vector3((float)card.ActualWidth / 2, (float)card.ActualHeight / 2, 0);
        popup.IsOpen = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_macroPanel, popup) || !popup.IsOpen) return;
            overlay.Opacity = 1;
            card.Scale = Vector3.One;
            close.Focus(FocusState.Programmatic);
        });
    }

    private void RebuildMacroPanel()
    {
        if (_macroPanel?.Child is not Grid overlay || _macroPanelContent is null) return;
        var (title, description, glyph) = _macroPanelIntegration is { } page ? IntegrationHeader(page) :
            (M("Despre", "About"), M("Conexiunea și versiunea firmware a dispozitivului Octoglow Macro.",
                "Connection and firmware version of your Octoglow Macro."), "\uE946");
        overlay.Language = AppStrings.IsEnglish ? "en-US" : "ro-RO";
        ((TextBlock)overlay.FindName("Title")).Text = title;
        ((TextBlock)overlay.FindName("Description")).Text = description;
        ((FontIcon)overlay.FindName("Icon")).Glyph = glyph;
        var close = (Button)overlay.FindName("Close");
        var closeLabel = M("Închideți", "Close");
        ToolTipService.SetToolTip(close, closeLabel);
        AutomationProperties.SetName(close, closeLabel);
        _macroPanelContent.Children.Clear();
        if (_macroPanelIntegration == IntegrationPage.CustomTones) RenderCustomTonePage();
        else if (_macroPanelIntegration == IntegrationPage.Applications) RenderApplications();
        else if (_macroPanelIntegration == IntegrationPage.Devices) RenderOctoglowIntegration();
        else
        {
            var serial = MacroSerialBus.Instance;
            _macroPanelContent.Children.Add(Heading(M("Conexiune", "Connection")));
            _macroPanelContent.Children.Add(SettingCard("\uE71B", M("Port serial", "Serial port"), "",
                new TextBlock { Text = serial.IsConnected ? serial.PortName : M("Neconectat", "Not connected"),
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }));
            _macroPanelContent.Children.Add(Heading("Software"));
            _macroPanelContent.Children.Add(SettingCard("\uE9F5", M("Versiune firmware", "Firmware version"), "",
                new TextBlock { Text = serial.IsConnected ? serial.FirmwareVersion : M("Indisponibil", "Unavailable"),
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }));
            AddBackupControls(_macroPanelContent);
        }
    }

    private void CloseMacroPanel(bool animate = true, bool restoreFocus = true)
    {
        var popup = _macroPanel;
        if (popup is null) return;
        _macroPanel = null;
        _macroPanelContent = null;
        _macroPanelStatus = null;
        _macroPanelIntegration = null;
        _detachMacroPanel?.Invoke();
        _detachMacroPanel = null;
        popup.Tag = restoreFocus;
        if (!animate || !MotionSettings.AnimationsEnabled || popup.Child is not Grid overlay)
        {
            popup.IsOpen = false;
            return;
        }
        overlay.IsHitTestVisible = false;
        overlay.Opacity = 0;
        if (overlay.FindName("Card") is Border card) card.Scale = new Vector3(0.96f, 0.96f, 1);
        _ = CloseMacroPanelLaterAsync(popup);
    }

    private static async Task CloseMacroPanelLaterAsync(Popup popup)
    {
        await Task.Delay(ClockNativeSettingsPage.PanelAnimation);
        popup.IsOpen = false;
    }
}
