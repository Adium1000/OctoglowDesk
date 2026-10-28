using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using OctoglowSender.Devices.Macro.Models;
using OctoglowSender.Devices.Macro.Services;

namespace OctoglowSender.Devices.Macro;

public sealed partial class MacroDevicePage : Page
{
    private const double PressedBorderThickness = 3;
    private const double MinimumPreviewHeight = 140;
    private const double MinimumSettingsHeight = 240;
    private Dictionary<string, Border>? _keysById;
    private IMacropadDevice? _subscribedDevice;
    private double _knobAngle;
    private double _tabsFullWidth;
    private bool _isLoaded;
    private Storyboard? _ledPreviewStoryboard;
    private double _previewFraction;
    private bool _resizingSections;

    public MacroDevicePage()
    {
        InitializeComponent();
        _previewFraction = ConfigStore.Load().MacroPreviewFraction;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => { ApplyPreviewHeight(); FitLayout(); };
        ApplyLanguage();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        UpdateConnectionStatus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        _keysById = new Dictionary<string, Border>
        {
            ["b1"] = KeyB1, ["b2"] = KeyB2, ["b3"] = KeyB3, ["b4"] = KeyB4,
            ["b5"] = KeyB5, ["b6"] = KeyB6, ["b7"] = KeyB7,
        };
        AppStrings.LanguageChanged += OnLanguageChanged;
        MacropadConnectionState.Changed += OnConnectionChanged;
        MacroSerialBus.Instance.ConnectionChanged += OnSerialConnectionChanged;
        MacroSerialBus.Instance.StatusChanged += OnSerialStatusChanged;
        MacroSerialBus.Instance.PhysicalInput += OnSerialInput;
        MacroSerialBus.Instance.ActiveTabChanged += OnSerialTabChanged;
        SubscribeToInput();
        MacropadConnectionState.CheckNow();
        UpdateConnectionStatus();
        UpdateLedPreview();
        ApplyPreviewHeight();
        UpdateMacroContentWidth(MacroContentScroll.ActualWidth);
        FitLayout();
        MacroNavigation.SelectedItem = ButtonsItem;
        _ = LoadMacroConfigurationAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        CloseMacroPanel(animate: false, restoreFocus: false);
        ResetMacroNavigationMotion();
        FinishSectionResize();
        _ledPreviewStoryboard?.Stop();
        _ledPreviewStoryboard = null;
        MacropadConnectionState.Changed -= OnConnectionChanged;
        MacroSerialBus.Instance.ConnectionChanged -= OnSerialConnectionChanged;
        MacroSerialBus.Instance.StatusChanged -= OnSerialStatusChanged;
        MacroSerialBus.Instance.PhysicalInput -= OnSerialInput;
        MacroSerialBus.Instance.ActiveTabChanged -= OnSerialTabChanged;
        UnsubscribeFromInput();
        AppStrings.LanguageChanged -= OnLanguageChanged;
    }

    private void SubscribeToInput()
    {
        var device = MacropadInputBus.Device;
        if (device is null || ReferenceEquals(device, _subscribedDevice)) return;
        UnsubscribeFromInput();
        device.KeyDown += OnDeviceKeyDown;
        device.KeyUp += OnDeviceKeyUp;
        device.KnobRotate += OnDeviceKnobRotate;
        device.KnobDown += OnDeviceKnobDown;
        device.KnobUp += OnDeviceKnobUp;
        device.Connected += OnInputConnected;
        device.Disconnected += OnInputDisconnected;
        _subscribedDevice = device;
    }

    private void UnsubscribeFromInput()
    {
        if (_subscribedDevice is null) return;
        _subscribedDevice.KeyDown -= OnDeviceKeyDown;
        _subscribedDevice.KeyUp -= OnDeviceKeyUp;
        _subscribedDevice.KnobRotate -= OnDeviceKnobRotate;
        _subscribedDevice.KnobDown -= OnDeviceKnobDown;
        _subscribedDevice.KnobUp -= OnDeviceKnobUp;
        _subscribedDevice.Connected -= OnInputConnected;
        _subscribedDevice.Disconnected -= OnInputDisconnected;
        _subscribedDevice = null;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_isLoaded) DispatcherQueue.TryEnqueue(ApplyLanguage);
    }

    private void OnConnectionChanged(object? sender, bool connected) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_isLoaded) return;
        UpdateConnectionStatus();
        if (!MacroSerialBus.Instance.IsConnected) RenderMacroSection();
        if (connected) SubscribeToInput();
        else ResetAllVisualStates();
    });

    private void OnInputConnected(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateConnectionStatus);
    private void OnInputDisconnected(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        UpdateConnectionStatus();
        ResetAllVisualStates();
    });
    private void OnDeviceKeyDown(object? sender, string buttonId) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!MacroSerialBus.Instance.IsConnected) SetKeyPressed(buttonId, true);
    });
    private void OnDeviceKeyUp(object? sender, string buttonId) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!MacroSerialBus.Instance.IsConnected) SetKeyPressed(buttonId, false);
    });
    private void OnDeviceKnobRotate(object? sender, int delta) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!MacroSerialBus.Instance.IsConnected) AnimateKnobRotate(delta);
    });
    private void OnDeviceKnobDown(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!MacroSerialBus.Instance.IsConnected) SetKnobPressed(true);
    });
    private void OnDeviceKnobUp(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!MacroSerialBus.Instance.IsConnected) SetKnobPressed(false);
    });

    private void UpdateConnectionStatus()
    {
        var serial = MacroSerialBus.Instance;
        var connected = serial.IsConnected;
        var present = MacropadConnectionState.IsConnected;
        var error = serial.LastError;
        var glyph = connected ? "\uE73E" : present && error.Length > 0 ? "\uE783"
            : present ? "\uE895" : "\uE711";
        var text = connected ? AppStrings.Get("connection.connected")
            : present && error.Length > 0 ? M("Configurare indisponibilă", "Setup unavailable")
            : present ? AppStrings.Get("connection.connecting")
            : AppStrings.Get("connection.disconnected");
        StatusIcon.Glyph = glyph;
        StatusIcon.ClearValue(FontIcon.ForegroundProperty);
        StatusText.Text = text;
        // Like the Clock pill: keep the normal appearance, with no misleading
        // hover or click while automatic discovery is handling the connection.
        StatusPill.IsHitTestVisible = false;
        StatusPill.IsTabStop = false;
        ToolTipService.SetToolTip(StatusPill, connected ? null : error.Length > 0 ? error : null);
        AutomationProperties.SetName(StatusPill, error.Length > 0 && !connected ? $"{text}: {error}" : text);
    }

    private void FitLayout()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var available = ActualHeight - MacroHeaderRow.ActualHeight;
        MacroPreview.MaxHeight = _previewFraction > 0 && available > 0
            ? Math.Clamp(_previewFraction * available - 32, 96, 360)
            : Math.Clamp(ActualHeight * 0.26, 96, 240);
        MacroPreview.Margin = ActualHeight < 700 ? new Thickness(32, 16, 32, 20)
            : new Thickness(32, 28, 32, 32);
        var tabs = new (SelectorBarItem Item, string Label)[]
        {
            (ButtonsItem, M("Butoane", "Buttons")),
            (LedItem, M("Iluminare", "Lighting")),
            (BuzzerItem, M("Buzzer", "Buzzer")),
            (IntegrationsItem, M("Integrări", "Integrations")),
        };
        if (_tabsFullWidth <= 0)
        {
            foreach (var (item, label) in tabs) item.Text = label;
            MacroNavigation.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            _tabsFullWidth = MacroNavigation.DesiredSize.Width;
            if (_tabsFullWidth <= 0) return;
        }
        var compact = _tabsFullWidth > ActualWidth - 48;
        foreach (var (item, label) in tabs)
        {
            item.Text = compact ? "" : label;
            ToolTipService.SetToolTip(item, compact ? label : null);
            AutomationProperties.SetName(item, label);
        }
    }

    private void MacroContentScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateMacroContentWidth(e.NewSize.Width);

    private void UpdateMacroContentWidth(double viewportWidth)
    {
        if (viewportWidth <= 0) return;
        // ScrollViewer otherwise measures the vertical StackPanel at its
        // desired width. TabView then hugs the right edge as the window resizes.
        // Give the content an explicit, centered viewport width instead.
        var width = Math.Min(976, viewportWidth);
        if (double.IsNaN(MacroContentViewport.Width) || Math.Abs(MacroContentViewport.Width - viewportWidth) > 0.5)
            MacroContentViewport.Width = viewportWidth;
        if (double.IsNaN(MacroContentColumn.Width) || Math.Abs(MacroContentColumn.Width - width) > 0.5)
            MacroContentColumn.Width = width;
        var innerWidth = Math.Max(1, width - MacroContentColumn.Padding.Left - MacroContentColumn.Padding.Right);
        if (double.IsNaN(ContentHost.Width) || Math.Abs(ContentHost.Width - innerWidth) > 0.5)
            ContentHost.Width = innerWidth;
    }

    private void ApplyPreviewHeight()
    {
        if (_resizingSections || _previewFraction <= 0 || ActualHeight <= 0) return;
        var available = ActualHeight - MacroHeaderRow.ActualHeight;
        if (available < MinimumPreviewHeight + MinimumSettingsHeight) return;
        MacroPreviewRow.Height = new GridLength(Math.Clamp(_previewFraction * available,
            MinimumPreviewHeight, available - MinimumSettingsHeight));
    }

    private void MacroSectionSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!MacroSectionSplitter.CapturePointer(e.Pointer)) return;
        _resizingSections = true;
        ResizeSections(e);
        e.Handled = true;
    }

    private void MacroSectionSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingSections) return;
        ResizeSections(e);
        e.Handled = true;
    }

    private void ResizeSections(PointerRoutedEventArgs e)
    {
        var available = ActualHeight - MacroHeaderRow.ActualHeight;
        if (available < MinimumPreviewHeight + MinimumSettingsHeight) return;
        var desired = e.GetCurrentPoint(MacroLayoutRoot).Position.Y - MacroHeaderRow.ActualHeight;
        var height = Math.Clamp(desired, MinimumPreviewHeight, available - MinimumSettingsHeight);
        MacroPreviewRow.Height = new GridLength(height);
        MacroPreview.MaxHeight = Math.Clamp(height - 32, 96, 360);
        _previewFraction = height / available;
    }

    private void MacroSectionSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        FinishSectionResize();
        MacroSectionSplitter.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void MacroSectionSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        FinishSectionResize();

    private void FinishSectionResize()
    {
        if (!_resizingSections) return;
        _resizingSections = false;
        ConfigStore.Update(config => config.MacroPreviewFraction = _previewFraction);
    }

    private void MacroInfoButton_Click(object sender, RoutedEventArgs e)
        => OpenMacroPanel(null);

    private void SetKeyPressed(string buttonId, bool pressed)
    {
        if (_keysById is null || !_keysById.TryGetValue(buttonId, out var key)) return;
        if (pressed)
        {
            key.BorderBrush = WindowsAccentColor.GetAccentBrush();
            key.BorderThickness = new Thickness(PressedBorderThickness);
        }
        else
        {
            key.ClearValue(Border.BorderBrushProperty);
            key.ClearValue(Border.BorderThicknessProperty);
        }
    }

    private void SetKnobPressed(bool pressed)
    {
        if (pressed)
        {
            EncoderBorder.BorderBrush = WindowsAccentColor.GetAccentBrush();
            EncoderBorder.BorderThickness = new Thickness(PressedBorderThickness);
        }
        else
        {
            EncoderBorder.ClearValue(Border.BorderBrushProperty);
            EncoderBorder.ClearValue(Border.BorderThicknessProperty);
        }
    }

    private void AnimateKnobRotate(int delta)
    {
        _knobAngle += delta * 25;
        var animation = new DoubleAnimation
        {
            To = _knobAngle,
            Duration = new Duration(TimeSpan.FromMilliseconds(120)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, EncoderNotchRotation);
        Storyboard.SetTargetProperty(animation, "Angle");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void ResetAllVisualStates()
    {
        if (_keysById is not null)
            foreach (var id in _keysById.Keys) SetKeyPressed(id, false);
        SetKnobPressed(false);
    }

    // Independent saved-output previews, not physical LED telemetry.
    private void UpdateLedPreview()
    {
        _ledPreviewStoryboard?.Stop();
        _ledPreviewStoryboard = null;

        if (!_isLoaded || _configuration is null || !MacroSerialBus.Instance.IsConnected)
        {
            LedPreview.Opacity = 0.25;
            return;
        }

        var supportsRgb = MacroSerialBus.Instance.SupportsV6;
        var activeTab = Math.Clamp(_configuration.ActiveTab, 1, _configuration.TabCount);
        var color = _configuration.GetTabColor(Math.Clamp(_configuration.ActiveTab, 1, _configuration.TabCount));
        AddressablePreview.Fill = supportsRgb
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue))
            : WindowsAccentColor.GetAccentBrush();
        _ledPreviewStoryboard = new Storyboard();
        var externalLevel = _configuration.HasExternalLighting ? _configuration.LedBrightness / 100.0 : 0;
        var rgbLevel = _configuration.HasAddressableLighting ? _configuration.AddressableBrightness / 100.0 : 0;
        LedPreview.Opacity = externalLevel > 0 || rgbLevel > 0 ? 1 : 0.25;
        AddLightingPreview(BacklightPreview, 0.28 * externalLevel, _configuration.GetTabEffect(activeTab), activeTab);
        AddLightingPreview(AddressablePreview, rgbLevel, _configuration.SupportsSplitLighting
            ? _configuration.GetTabRgbEffect(activeTab) : _configuration.GetTabEffect(activeTab), activeTab);
        if (_ledPreviewStoryboard.Children.Count > 0) _ledPreviewStoryboard.Begin();
    }

    private void AddLightingPreview(FrameworkElement target, double level, int effect, int tab)
    {
        target.Opacity = level;
        if (effect == 0 || level == 0 || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        var period = effect switch { 5 => 1536, 6 => 625, 7 => 300, _ => 2000 };
        var animation = new DoubleAnimationUsingKeyFrames {
            Duration = new Duration(TimeSpan.FromMilliseconds(period)), RepeatBehavior = RepeatBehavior.Forever };
        for (var time = 0; time < period; time += 25)
        {
            var value = effect switch {
                1 => (time < 1000 ? time : 2000 - time) / 4,
                2 => time < 1000 ? 255 : 0,
                3 => time < 180 || (time > 340 && time < 520) ? 255 : 12,
                4 => time < 140 || (time > 230 && time < 420) ? 255 : 24,
                5 => (time / 6) % 256,
                6 => ((time / 125 + (tab - 1) * 13) * 37) % 5 == 0 ? 255 : 18,
                7 => (time / 75) % 4 == 0 ? 255 : 0,
                _ => 255
            };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(time)), Value = level * value / 255.0 });
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _ledPreviewStoryboard!.Children.Add(animation);
    }

    private void ApplyLanguage()
    {
        PageTitle.Text = AppStrings.Get("macro.title");
        var infoLabel = M("Informații despre Octoglow Macro", "About Octoglow Macro");
        AutomationProperties.SetName(MacroInfoButton, infoLabel);
        ToolTipService.SetToolTip(MacroInfoButton, infoLabel);
        _tabsFullWidth = 0;
        FitLayout();
        if (_isLoaded) RenderMacroSection();
        UpdateConnectionStatus();
    }
}
