using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace OctoglowSender;

public sealed partial class DeskClockPage : Page
{
    private DeviceDescriptor? _device;
    private bool _ready;
    private bool _subscribed;
    // Set while the tab bar's selection is changed from code, so it doesn't
    // navigate on top of what is being shown.
    private bool _suppressSelection;
    // What pressing the pill does, from what it says.
    private PillAction _pillAction = PillAction.None;
    private bool _askToSignIn;
    // The last screen state acted on, so a frame arriving doesn't redo the header.
    private ScreenMirrorState _screenState = ScreenMirrorState.Waiting;

    private enum PillAction
    {
        None,
        SignIn,
        Retry,
        Disconnect,
    }
    private readonly Ellipse[,] _screenLeds = new Ellipse[8, 32];
    private readonly byte[] _renderedScreenColumns = new byte[32];
    private readonly SolidColorBrush _screenLedOnBrush;
    private readonly SolidColorBrush _screenLedOffBrush;
    private double _renderedLedOpacity = -1;
    private int _screenRenderQueued;
    // How wide the tabs are with their labels, measured once per language.
    private double _tabsFullWidth;
    private const double MinimumPreviewHeight = 140;
    private const double MinimumSettingsHeight = 240;
    private double _previewFraction;
    private bool _resizingClockSections;

    public DeskClockPage()
    {
        InitializeComponent();
        _screenLedOnBrush = (SolidColorBrush)Resources["ScreenLedOnBrush"];
        _screenLedOffBrush = (SolidColorBrush)Resources["ScreenLedOffBrush"];
        _previewFraction = ConfigStore.Load().ClockPreviewFraction;
        CreateScreenLeds();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            FitLayout();
            ApplyPreviewHeight();
        };
        ApplyLanguage();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _device = e.Parameter as DeviceDescriptor;
        _ready = true;
        UpdateConnectionState();
        _suppressSelection = true;
        ClockNavigation.SelectedItem = ConfigurationItem;
        _suppressSelection = false;
        NavigateTo("configuration");
        // Opened from Home or from the list on the left while the clock is only on
        // the network: it asks for the clock's account straight away.
        _askToSignIn = _pillAction == PillAction.SignIn;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            AppStrings.LanguageChanged += OnLanguageChanged;
            ConnectionStatus.Changed += ConnectionStatus_Changed;
            App.DeviceHub.DevicesChanged += DeviceHub_DevicesChanged;
            ScreenMirror.Changed += ScreenMirror_Changed;
            ScreenMirror.AddViewer();
            RenderScreen();
            FitLayout();
            ApplyPreviewHeight();
        }

        if (!_askToSignIn) return;
        _askToSignIn = false;
        // Once the page itself is up, so the question arrives over it rather than over nothing.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_pillAction == PillAction.SignIn) _ = SignInAsync();
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FinishClockSectionResize();
        if (!_subscribed) return;
        _subscribed = false;
        AppStrings.LanguageChanged -= OnLanguageChanged;
        ConnectionStatus.Changed -= ConnectionStatus_Changed;
        App.DeviceHub.DevicesChanged -= DeviceHub_DevicesChanged;
        ScreenMirror.Changed -= ScreenMirror_Changed;
        ScreenMirror.RemoveViewer();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ApplyLanguage);
    private void ConnectionStatus_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateConnectionState);
    private void DeviceHub_DevicesChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_device is not null)
            _device = App.DeviceHub.Devices.FirstOrDefault(device => device.Id == _device.Id) ?? _device;
        UpdateConnectionState();
    });

    private void ScreenMirror_Changed(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _screenRenderQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _screenRenderQueued, 0);
                RenderScreen();
                // The picture stopping is how the web interface notices the clock
                // has gone, and it is how this notices too.
                var screen = ScreenMirror.State;
                if (screen == _screenState) return;
                _screenState = screen;
                UpdateConnectionState();
            }))
        {
            Interlocked.Exchange(ref _screenRenderQueued, 0);
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e) =>
        ClockNativeSettingsPage.ShowPanel(ClockSettingsSection.About, _device, this);

    private void ClockNavigation_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_ready || _suppressSelection || sender.SelectedItem is not { } selected) return;
        NavigateTo(selected.Tag?.ToString() ?? "configuration");
    }

    private void NavigateTo(string section)
    {
        var index = SectionIndex(section);
        var transition = SectionTransition(index);
        if (section == "activity")
        {
            if (ClockContentFrame.CurrentSourcePageType == typeof(LogsPage)) return;
            _sectionIndex = index;
            ClockContentFrame.Navigate(typeof(LogsPage), _device, transition);
            return;
        }

        var settingsSection = section == "integrations" ? ClockSettingsSection.Integrations : ClockSettingsSection.Configuration;
        _sectionIndex = index;
        ClockContentFrame.Navigate(typeof(ClockNativeSettingsPage), new ClockSettingsNavigation(settingsSection, _device), transition);
    }

    // Where each tab sits in the bar, left to right.
    private static int SectionIndex(string section) => section switch
    {
        "integrations" => 1,
        "activity" => 2,
        _ => 0,
    };

    // Which tab is showing, so the next one arrives from the side it was picked on.
    private int _sectionIndex;

    // A tab further right along the bar slides in from the right, one further
    // left from the left, so the movement follows the tabs. The first section of
    // all has nothing to move away from, and simply appears.
    private NavigationTransitionInfo SectionTransition(int index) =>
        index == _sectionIndex
            ? new SuppressNavigationTransitionInfo()
            : new SlideNavigationTransitionInfo
            {
                Effect = index > _sectionIndex
                    ? SlideNavigationTransitionEffect.FromRight
                    : SlideNavigationTransitionEffect.FromLeft,
            };

    private void ApplyPreviewHeight()
    {
        if (_resizingClockSections || _previewFraction <= 0 || ActualHeight <= 0) return;
        var available = ActualHeight - ClockHeaderRow.ActualHeight;
        if (available < MinimumPreviewHeight + MinimumSettingsHeight) return;
        ClockPreviewRow.Height = new GridLength(Math.Clamp(_previewFraction * available,
            MinimumPreviewHeight, available - MinimumSettingsHeight));
    }

    private void ClockSectionSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ClockSectionSplitter.CapturePointer(e.Pointer)) return;
        _resizingClockSections = true;
        ResizeClockSections(e);
        e.Handled = true;
    }

    private void ClockSectionSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingClockSections) return;
        ResizeClockSections(e);
        e.Handled = true;
    }

    private void ResizeClockSections(PointerRoutedEventArgs e)
    {
        var available = ActualHeight - ClockHeaderRow.ActualHeight;
        if (available < MinimumPreviewHeight + MinimumSettingsHeight) return;
        var desired = e.GetCurrentPoint(ClockLayoutRoot).Position.Y - ClockHeaderRow.ActualHeight;
        var height = Math.Clamp(desired, MinimumPreviewHeight, available - MinimumSettingsHeight);
        ClockPreviewRow.Height = new GridLength(height);
        _previewFraction = height / available;
    }

    private void ClockSectionSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        FinishClockSectionResize();
        ClockSectionSplitter.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ClockSectionSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        FinishClockSectionResize();

    private void FinishClockSectionResize()
    {
        if (!_resizingClockSections) return;
        _resizingClockSections = false;
        ConfigStore.Update(config => config.ClockPreviewFraction = _previewFraction);
    }

    // Keeps the page whole at any window size: on a short window the live clock
    // gives up height to the section under it, and when the tab labels no longer
    // fit across, the tabs keep their icons and show the labels as tooltips.
    private void FitLayout()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        ClockPreview.MaxHeight = Math.Clamp(ActualHeight * 0.26, 96, 240);
        ClockPreview.Margin = ActualHeight < 700 ? new Thickness(32, 16, 32, 20) : new Thickness(32, 28, 32, 32);

        var tabs = new (SelectorBarItem Item, string Label)[]
        {
            (ConfigurationItem, AppStrings.Get("clock.configurationTab")),
            (IntegrationsItem, AppStrings.Get("clock.integrationsTab")),
            (ActivityItem, AppStrings.Get("clock.activityTab")),
        };
        if (_tabsFullWidth <= 0)
        {
            foreach (var (item, label) in tabs) item.Text = label;
            ClockNavigation.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            _tabsFullWidth = ClockNavigation.DesiredSize.Width;
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

    private void ApplyLanguage()
    {
        PageTitle.Text = AppStrings.Get("clock.title");
        IntegrationsItem.Text = AppStrings.Get("clock.integrationsTab");
        ActivityItem.Text = AppStrings.Get("clock.activityTab");
        ConfigurationItem.Text = AppStrings.Get("clock.configurationTab");
        _tabsFullWidth = 0;
        FitLayout();
        ToolTipService.SetToolTip(AboutButton, AppStrings.Get("clock.aboutTab"));
        AutomationProperties.SetName(AboutButton, AppStrings.Get("clock.aboutTab"));
        UpdateConnectionState();
        RenderScreen();
    }

    private void UpdateConnectionState()
    {
        var online = _device?.IsOnline == true;
        // A session running for another clock says nothing about this one.
        var state = !DeskClockSession.IsRunning || IsCurrentSessionDevice()
            ? ConnectionStatus.Current
            : ConnectionStatusKind.Disconnected;
        // Signed in, but the clock stopped answering: the frames it was sending
        // have dried up, which is what the web interface watches as well.
        var lost = state == ConnectionStatusKind.Connected && ScreenMirror.State == ScreenMirrorState.Offline;

        string glyph, text;
        string? hint = null;
        var action = PillAction.None;
        switch (state)
        {
            case ConnectionStatusKind.Connected when lost:
                (glyph, text) = ("\uE783", AppStrings.Get("clock.signInRetry"));
                hint = AppStrings.Get("clock.connectionLostHint");
                action = PillAction.Retry;
                break;
            case ConnectionStatusKind.Connected:
                (glyph, text) = ("\uE73E", AppStrings.Get("connection.connected"));
                hint = AppStrings.Get("clock.disconnectAction");
                action = PillAction.Disconnect;
                break;
            case ConnectionStatusKind.Connecting:
                (glyph, text) = ("\uE895", AppStrings.Get("connection.connecting"));
                break;
            // A login that didn't take offers the one thing worth doing about it.
            case ConnectionStatusKind.Failed:
                (glyph, text) = ("\uE783", AppStrings.Get("clock.signInRetry"));
                hint = AppStrings.Get("clock.signInRetryHint");
                action = PillAction.Retry;
                break;
            // Answering on the network is not being signed in to: the clock's
            // account is what is still missing, and the pill asks for it.
            case not ConnectionStatusKind.Failed when online:
                (glyph, text) = ("\uE8CE", AppStrings.Get("clock.sameNetwork"));
                hint = AppStrings.Get("clock.signInAction");
                action = PillAction.SignIn;
                break;
            default:
                (glyph, text) = ("\uE711", AppStrings.Get("connection.disconnected"));
                break;
        }

        _pillAction = action;
        ConnectionStateIcon.Glyph = glyph;
        ConnectionStateText.Text = text;
        // Not disabled, which would grey the pill out: it simply isn't a button
        // when there is nothing to do about what it says.
        ConnectionStateCard.IsHitTestVisible = action != PillAction.None;
        ConnectionStateCard.IsTabStop = action != PillAction.None;
        ToolTipService.SetToolTip(ConnectionStateCard, hint);
        AutomationProperties.SetName(ConnectionStateCard, hint is null ? text : $"{text} · {hint}");

        // None of the tabs, nor About, can do anything with a clock the app isn't
        // signed in to, so the whole band below and the About button are greyed
        // out rather than each page failing on its own.
        var usable = state == ConnectionStatusKind.Connected && !lost;
        ClockNavigation.IsEnabled = usable;
        ClockContentFrame.IsEnabled = usable;
        AboutButton.IsEnabled = usable;
    }

    private void ConnectionState_Click(object sender, RoutedEventArgs e)
    {
        switch (_pillAction)
        {
            case PillAction.SignIn:
                _ = SignInAsync();
                break;
            case PillAction.Retry:
                Reconnect();
                break;
            case PillAction.Disconnect:
                _ = ConfirmDisconnectAsync();
                break;
        }
    }

    // The same account, tried again - after a failed login or after the clock
    // went away. Only when there is no account saved does it ask for one.
    private void Reconnect()
    {
        var config = ConfigStore.Load();
        if (string.IsNullOrWhiteSpace(config.Esp32Ip) ||
            string.IsNullOrWhiteSpace(config.ScUser) ||
            string.IsNullOrEmpty(config.ScPass))
        {
            _ = SignInAsync();
            return;
        }
        ClockDeviceApi.ForgetSession();
        DeskClockSession.Start(config);
        UpdateConnectionState();
    }

    private async Task ConfirmDisconnectAsync()
    {
        if (XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE8BB", AppStrings.Get("clock.disconnectTitle")),
            Content = new TextBlock { Text = AppStrings.Get("clock.disconnectMessage"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = AppStrings.Get("clock.disconnectAction"),
            CloseButtonText = AppStrings.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }
        DeskClockSession.Stop();
        AppLog.WriteDeskClock(AppStrings.Get("connection.stopped"));
        UpdateConnectionState();
    }

    // The clock answers on the network but nothing has signed in to it. Windows
    // asks before something that needs permission; this asks for the account the
    // clock itself was given, and only then starts sending to it.
    private async Task SignInAsync()
    {
        if (XamlRoot is null) return;
        var config = ConfigStore.Load();
        var address = _device?.Address is { Length: > 0 } found ? found : config.Esp32Ip;
        var port = _device?.Port is > 0 and <= 65535 ? _device.Port : config.Esp32Port;
        var sameClock = string.Equals(address, config.Esp32Ip, StringComparison.OrdinalIgnoreCase) &&
                        port == config.Esp32Port;

        var user = new TextBox { Header = AppStrings.Get("connection.user"), Text = sameClock ? config.ScUser : "" };
        var password = new PasswordBox { Header = AppStrings.Get("connection.password"), Password = sameClock ? config.ScPass : "" };
        var problem = new InfoBar { IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Error };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(new FontIcon { Glyph = "\uE72E", FontSize = 22, VerticalAlignment = VerticalAlignment.Center });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "Octoglow Clock", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        names.Children.Add(new TextBlock { Text = $"{address}:{port}", Opacity = 0.72, FontSize = 12 });
        heading.Children.Add(names);

        var content = new StackPanel { Spacing = 14, Width = 320 };
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = AppStrings.Get("clock.signInMessage"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(user);
        content.Children.Add(password);
        content.Children.Add(problem);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE72E", AppStrings.Get("clock.signInTitle")),
            Content = content,
            PrimaryButtonText = AppStrings.Get("clock.signInAction"),
            CloseButtonText = AppStrings.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
        };

        void Problem(string key)
        {
            problem.Message = AppStrings.Get(key);
            problem.IsOpen = true;
        }

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // The dialog stays open until the clock has actually accepted these.
            args.Cancel = true;
            var deferral = args.GetDeferral();
            var name = user.Text.Trim();
            var secret = password.Password;
            try
            {
                if (name.Length == 0 || secret.Length == 0)
                {
                    Problem("clock.signInEmpty");
                    return;
                }
                dialog.IsPrimaryButtonEnabled = false;
                problem.IsOpen = false;
                if (!await App.DeviceHub.PairClockAsync(address, port, name, secret, CancellationToken.None))
                {
                    Problem("clock.signInWrong");
                    return;
                }
                args.Cancel = false;
            }
            catch (UnauthorizedAccessException)
            {
                // Too many wrong passwords: the clock's login stays shut until it restarts.
                Problem("clock.signInLocked");
            }
            catch (Exception)
            {
                Problem("clock.signInUnreachable");
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog is already open; the pill stays as it was.
        }
        UpdateConnectionState();
    }

    private bool IsCurrentSessionDevice()
    {
        var device = _device;
        if (device is null || string.IsNullOrWhiteSpace(device.Address)) return false;
        return DeskClockSession.Matches(
            device.Address,
            device.Port is > 0 and <= 65535 ? device.Port : 80);
    }

    private const double LedPitch = 20;
    private const double LitLedSize = 14;
    private const double UnlitLedSize = 8;
    private const double UnlitLedOpacity = 0.42;

    private void CreateScreenLeds()
    {
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var cx = x * LedPitch + LedPitch / 2;
                var cy = y * LedPitch + LedPitch / 2;
                var led = new Ellipse
                {
                    Width = UnlitLedSize,
                    Height = UnlitLedSize,
                    Fill = _screenLedOffBrush,
                    Opacity = UnlitLedOpacity,
                    IsHitTestVisible = false,
                };

                Canvas.SetLeft(led, cx - UnlitLedSize / 2);
                Canvas.SetTop(led, cy - UnlitLedSize / 2);
                ScreenCanvas.Children.Add(led);
                _screenLeds[y, x] = led;
            }
        }
    }

    private void RenderScreen()
    {
        var snapshot = ScreenMirror.GetSnapshot();
        var hasFrame = snapshot.LastUpdate != default;
        var live = snapshot.State == ScreenMirrorState.Live;
        var stale = snapshot.State == ScreenMirrorState.Offline;
        var showFrame = hasFrame && snapshot.IsOn && (live || stale);
        var level = showFrame ? 0.9 + 0.1 * Math.Clamp(snapshot.Brightness, 1, 16) / 16.0 : 1.0;
        var brightnessChanged = Math.Abs(level - _renderedLedOpacity) > 0.001;
        _renderedLedOpacity = level;
        ScreenCanvas.Opacity = stale ? 0.75 : 1.0;

        for (var x = 0; x < 32; x++)
        {
            var nextColumn = showFrame ? snapshot.Columns[x] : (byte)0;
            var previousColumn = _renderedScreenColumns[x];
            if (nextColumn == previousColumn && !brightnessChanged) continue;

            for (var y = 0; y < 8; y++)
            {
                var mask = 1 << y;
                var lit = (nextColumn & mask) != 0;
                var wasLit = (previousColumn & mask) != 0;
                if (lit == wasLit && !(brightnessChanged && lit)) continue;

                var led = _screenLeds[y, x];
                if (lit != wasLit)
                {
                    var size = lit ? LitLedSize : UnlitLedSize;
                    led.Fill = lit ? _screenLedOnBrush : _screenLedOffBrush;
                    led.Width = size;
                    led.Height = size;
                    Canvas.SetLeft(led, x * LedPitch + (LedPitch - size) / 2);
                    Canvas.SetTop(led, y * LedPitch + (LedPitch - size) / 2);
                }

                led.Opacity = lit ? level : UnlitLedOpacity;
            }

            _renderedScreenColumns[x] = nextColumn;
        }

        (string key, string glyph)? overlay = snapshot.State switch
        {
            ScreenMirrorState.Unsupported => ("clock.screenUnsupported", "\uE7BA"),
            ScreenMirrorState.Waiting => ("clock.screenWaiting", "\uE895"),
            ScreenMirrorState.Live when !snapshot.IsOn => ("clock.screenOff", "\uE708"),
            ScreenMirrorState.Offline when !hasFrame => ("clock.screenOffline", "\uE7BA"),
            _ => null,
        };

        ScreenOverlay.Visibility = overlay is null ? Visibility.Collapsed : Visibility.Visible;
        if (overlay is { } shown)
        {
            ScreenOverlayText.Text = AppStrings.Get(shown.key);
            ScreenOverlayIcon.Glyph = shown.glyph;
        }
    }
}
