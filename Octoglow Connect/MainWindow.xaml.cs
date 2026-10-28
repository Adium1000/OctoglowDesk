using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace OctoglowSender;

public sealed partial class MainWindow : Window
{
    // In effective pixels, as the layout is: the window scales them to the display,
    // so it opens the same size at 125% or 150% as at 100%.
    private const int InitialWidth = 1050;
    private const int InitialHeight = 790;
    private const int MinimumWidth = 900;
    private const int MinimumHeight = 640;

    private readonly Dictionary<string, NavigationViewItem> _deviceItems =
        new(StringComparer.OrdinalIgnoreCase);

    // Which devices the menu is showing, in the order it shows them.
    private readonly List<string> _deviceOrder = new();

    private string? _selectedDeviceId;
    private string _currentRoute = "home";
    private bool _isClosed;
    private readonly DispatcherQueueTimer _windowSizeSaveTimer;
    private (int Width, int Height)? _pendingWindowSize;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        PlaceWindow();
        _windowSizeSaveTimer = DispatcherQueue.CreateTimer();
        _windowSizeSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _windowSizeSaveTimer.Tick += (_, _) => SavePendingWindowSize();
        AppWindow.Changed += AppWindow_Changed;

        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };

        AppStrings.LanguageChanged += AppStrings_LanguageChanged;
        App.DeviceHub.DevicesChanged += DeviceHub_DevicesChanged;
        ConnectionStatus.Changed += ConnectionStatus_Changed;
        Closed += MainWindow_Closed;

        ApplyLanguage();
        RebuildDeviceItems();
        ContentFrame.Navigate(typeof(HubPage));
    }

    // Window sizes are in physical pixels, so the sizes above are scaled by the
    // display's DPI. The window opens centred on its screen, and never larger
    // than the room the taskbar leaves.
    private void PlaceWindow()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var config = ConfigStore.Load();
        var preferredWidth = config.WindowWidth > 0 ? config.WindowWidth : InitialWidth;
        var preferredHeight = config.WindowHeight > 0 ? config.WindowHeight : InitialHeight;
        var width = (int)Math.Min(Math.Clamp(preferredWidth, MinimumWidth, 3000) * scale, area.Width * 0.94);
        var height = (int)Math.Min(Math.Clamp(preferredHeight, MinimumHeight, 2000) * scale, area.Height * 0.94);
        AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Min(MinimumWidth * scale, width);
            presenter.PreferredMinimumHeight = (int)Math.Min(MinimumHeight * scale, height);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || sender.Presenter is not OverlappedPresenter
            { State: OverlappedPresenterState.Restored }) return;

        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        _pendingWindowSize = ((int)Math.Round(sender.Size.Width / scale),
            (int)Math.Round(sender.Size.Height / scale));
        _windowSizeSaveTimer.Stop();
        _windowSizeSaveTimer.Start();
    }

    private void SavePendingWindowSize()
    {
        _windowSizeSaveTimer.Stop();
        if (_pendingWindowSize is not { } size) return;
        _pendingWindowSize = null;
        ConfigStore.TryUpdate(config =>
        {
            if (config.WindowWidth == size.Width && config.WindowHeight == size.Height) return false;
            config.WindowWidth = size.Width;
            config.WindowHeight = size.Height;
            return true;
        });
    }

    public void NavigateToDevice(DeviceDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var pageType = device.Kind == DeviceKind.Macro
            ? typeof(Devices.Macro.MacroDevicePage)
            : typeof(DeskClockPage);
        var alreadyShowing = _currentRoute == "device" &&
                             string.Equals(_selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase) &&
                             ContentFrame.CurrentSourcePageType == pageType;

        _selectedDeviceId = device.Id;
        _currentRoute = "device";
        SelectDeviceItem(device.Id);

        if (!alreadyShowing)
            ContentFrame.Navigate(pageType, device);
    }

    public void OpenDeskClockPanel(DeviceDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.Kind != DeviceKind.DeskClock)
        {
            NavigateToDevice(device);
            return;
        }

        var alreadyShowing = _currentRoute == "desk-clock-panel" &&
                             string.Equals(_selectedDeviceId, device.Id, StringComparison.OrdinalIgnoreCase) &&
                             ContentFrame.CurrentSourcePageType == typeof(CustomizePage);

        _selectedDeviceId = device.Id;
        _currentRoute = "desk-clock-panel";
        SelectDeviceItem(device.Id);
        if (!alreadyShowing)
            ContentFrame.Navigate(typeof(CustomizePage), device);
    }

    public async Task EnterTrayLowMemoryModeAsync()
    {
        if (Content is FrameworkElement root)
            root.Visibility = Visibility.Collapsed;

        if (ContentFrame.Content is CustomizePage customizePage)
            await customizePage.SuspendForTrayAsync();
    }

    public void ExitTrayLowMemoryMode()
    {
        if (ContentFrame.Content is CustomizePage customizePage)
            customizePage.ResumeFromTray();

        if (Content is FrameworkElement root)
            root.Visibility = Visibility.Visible;
    }

    private void RootNavigation_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem item)
            return;

        if (item.Tag is DeviceDescriptor device)
        {
            NavigateToDevice(device);
            return;
        }

        switch (item.Tag?.ToString())
        {
            case "settings":
                NavigateToStaticPage("settings", typeof(SettingsPage));
                break;
            case "about":
                NavigateToStaticPage("about", typeof(AboutPage));
                break;
            default:
                NavigateToStaticPage("home", typeof(HubPage));
                break;
        }
    }

    private void NavigateToStaticPage(string route, Type pageType)
    {
        if (_currentRoute == route && ContentFrame.CurrentSourcePageType == pageType)
            return;

        _currentRoute = route;
        ContentFrame.Navigate(pageType);
    }

    // The devices sit after the "Devices" header the menu starts with.
    private const int DeviceMenuOffset = 1;

    // Changed in place, never made again from scratch: taking every item out and
    // putting it back while one of them is being clicked kills the navigation
    // view natively (0xc0000005 in Microsoft.UI.Xaml.Controls.dll), and the hub
    // looks around often. Only what actually differs is touched.
    private void RebuildDeviceItems()
    {
        var devices = App.DeviceHub.Devices.ToList();
        var present = new HashSet<string>(devices.Select(device => device.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var id in _deviceOrder.Where(id => !present.Contains(id)).ToList())
        {
            if (_deviceItems.Remove(id, out var gone)) RootNavigation.MenuItems.Remove(gone);
        }

        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            if (_deviceItems.TryGetValue(device.Id, out var item))
            {
                // The hub hands out a new descriptor every time it looks; the
                // item keeps whichever one is current.
                item.Tag = device;
            }
            else
            {
                item = CreateDeviceItem(device);
                _deviceItems[device.Id] = item;
            }
            var place = Math.Min(DeviceMenuOffset + index, RootNavigation.MenuItems.Count);
            var at = RootNavigation.MenuItems.IndexOf(item);
            if (at == place) continue;
            if (at >= 0) RootNavigation.MenuItems.RemoveAt(at);
            RootNavigation.MenuItems.Insert(Math.Min(place, RootNavigation.MenuItems.Count), item);
        }

        _deviceOrder.Clear();
        _deviceOrder.AddRange(devices.Select(device => device.Id));
        UpdateDeviceStates();

        if (!IsShowingDevice())
            return;

        if (_selectedDeviceId is not null && _deviceItems.ContainsKey(_selectedDeviceId))
        {
            SelectDeviceItem(_selectedDeviceId);
            return;
        }

        _selectedDeviceId = null;
        _currentRoute = "home";
        RootNavigation.SelectedItem = HomeNavItem;
        ContentFrame.Navigate(typeof(HubPage));
    }

    private NavigationViewItem CreateDeviceItem(DeviceDescriptor device)
    {
        var nameText = new TextBlock
        {
            Text = device.DisplayName,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        var detail = BuildDeviceDetail(DeviceStateText(device), device.Detail);
        var detailText = new TextBlock
        {
            Text = detail,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            Opacity = 0.68
        };

        var content = new StackPanel { Spacing = 1 };
        content.Children.Add(nameText);
        content.Children.Add(detailText);

        var item = new NavigationViewItem
        {
            Tag = device,
            Content = content,
            Icon = new SymbolIcon(device.Kind == DeviceKind.Macro ? Symbol.Keyboard : Symbol.Clock),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = true
        };

        ToolTipService.SetToolTip(item, $"{device.DisplayName}\n{detail}");
        return item;
    }

    private static string BuildDeviceDetail(string state, string deviceDetail)
    {
        if (string.IsNullOrWhiteSpace(deviceDetail))
            return state;

        var separator = deviceDetail.IndexOf('·');
        var startsWithState = deviceDetail.StartsWith("Online", StringComparison.OrdinalIgnoreCase) ||
                              deviceDetail.StartsWith("Offline", StringComparison.OrdinalIgnoreCase) ||
                              deviceDetail.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ||
                              deviceDetail.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase);

        if (!startsWithState)
            return $"{state} · {deviceDetail}";

        return separator >= 0 && separator + 1 < deviceDetail.Length
            ? $"{state} · {deviceDetail[(separator + 1)..].Trim()}"
            : state;
    }

    private void SelectDeviceItem(string deviceId)
    {
        if (_deviceItems.TryGetValue(deviceId, out var item))
            RootNavigation.SelectedItem = item;
    }

    private bool IsShowingDevice() =>
        _currentRoute is "device" or "desk-clock-panel";

    private void ApplyLanguage()
    {
        Title = AppStrings.Get("app.title");
        WindowTitle.Text = Title;
        HomeNavItem.Content = AppStrings.Get("nav.home");
        DevicesHeader.Content = AppStrings.Get("nav.devices");
        SettingsNavItem.Content = AppStrings.Get("nav.settings");
        AboutNavItem.Content = AppStrings.Get("nav.about");
    }

    private void AppStrings_LanguageChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLanguage();
            // Their text was built in the old language, so these do go and come back.
            foreach (var item in _deviceItems.Values) RootNavigation.MenuItems.Remove(item);
            _deviceItems.Clear();
            _deviceOrder.Clear();
            RebuildDeviceItems();
        });

    // The hub speaks up whenever it has looked around, which is mostly the same
    // devices with something new to say about them. Rebuilding the menu then can
    // land in the middle of a click on one of its items, and the navigation view
    // does not survive that - so the list is made again only when it is a
    // different list.
    private void DeviceHub_DevicesChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (App.DeviceHub.Devices.Select(device => device.Id).SequenceEqual(_deviceOrder))
            {
                UpdateDeviceStates();
                return;
            }
            RebuildDeviceItems();
        });

    private void ConnectionStatus_Changed(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateDeviceStates);

    // Answering on the network is not being signed in to, and the clock says which.
    private static string DeviceStateText(DeviceDescriptor device) => DeviceState.StatusText(device);

    // Only the line under each name is rewritten. Rebuilding the menu itself
    // every time the connection changes can land in the middle of a click on one
    // of its items, and the navigation view does not survive that.
    private void UpdateDeviceStates()
    {
        foreach (var device in App.DeviceHub.Devices)
        {
            if (!_deviceItems.TryGetValue(device.Id, out var item)) continue;
            if (item.Content is not StackPanel content || content.Children.Count < 2) continue;
            if (content.Children[1] is not TextBlock detailText) continue;
            var detail = BuildDeviceDetail(DeviceStateText(device), device.Detail);
            if (detailText.Text == detail) continue;
            detailText.Text = detail;
            ToolTipService.SetToolTip(item, $"{device.DisplayName}\n{detail}");
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_isClosed)
            return;

        SavePendingWindowSize();
        AppWindow.Changed -= AppWindow_Changed;
        _isClosed = true;
        AppStrings.LanguageChanged -= AppStrings_LanguageChanged;
        App.DeviceHub.DevicesChanged -= DeviceHub_DevicesChanged;
        ConnectionStatus.Changed -= ConnectionStatus_Changed;
        Closed -= MainWindow_Closed;
    }
}
