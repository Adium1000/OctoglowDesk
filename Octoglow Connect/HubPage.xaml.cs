using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OctoglowSender;

public sealed partial class HubPage : Page
{
    private static readonly ImageSource PickerClockImage =
        new BitmapImage(new Uri("ms-appx:///Assets/smartclock-nobg_2.png"));
    private bool _isSubscribed;
    private bool _pickerOpen;
    private int _refreshQueued;

    public HubPage()
    {
        InitializeComponent();
        Loaded += HubPage_Loaded;
        Unloaded += HubPage_Unloaded;
    }

    private void HubPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            App.DeviceHub.DevicesChanged += DeviceHub_DevicesChanged;
            App.DeviceHub.ScanStateChanged += DeviceHub_ScanStateChanged;
            AppStrings.LanguageChanged += AppStrings_LanguageChanged;
            ConnectionStatus.Changed += ConnectionStatus_Changed;
            _isSubscribed = true;
        }

        RefreshPage();
    }

    private void HubPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed) return;

        App.DeviceHub.DevicesChanged -= DeviceHub_DevicesChanged;
        App.DeviceHub.ScanStateChanged -= DeviceHub_ScanStateChanged;
        AppStrings.LanguageChanged -= AppStrings_LanguageChanged;
        ConnectionStatus.Changed -= ConnectionStatus_Changed;
        _isSubscribed = false;
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        try { await App.DeviceHub.RefreshAsync(force: true); }
        catch (Exception error) { AppLog.Write($"[DEVICE SCAN ERR] {error.Message}"); }
        finally { UpdateScanState(); }
    }

    private async void AddDeviceButton_Click(object sender, RoutedEventArgs e) => await OpenPickerSafelyAsync();

    private async Task OpenPickerSafelyAsync()
    {
        try { await ShowAddDevicePickerAsync(); }
        catch (Exception error) { AppLog.Write($"[DEVICE SCAN ERR] {error.Message}"); }
    }

    private async Task<bool> ShowClockPairingDialogAsync(DeviceDescriptor clock)
    {
        if (XamlRoot is null || string.IsNullOrWhiteSpace(clock.Address)) return false;
        var signedIn = false;
        var config = ConfigStore.Load();
        var sameClock = string.Equals(config.Esp32Ip, clock.Address, StringComparison.OrdinalIgnoreCase) &&
                        config.Esp32Port == clock.Port;
        var user = new TextBox { Header = AppStrings.Get("connection.user"),
            Text = sameClock ? config.ScUser : "" };
        var password = new PasswordBox { Header = AppStrings.Get("connection.password"),
            Password = sameClock ? config.ScPass : "" };
        var problem = new InfoBar { IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Error };
        var body = new StackPanel { Width = 340, Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"{clock.DisplayName} · {clock.Address}:{clock.Port}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = AppStrings.Get("clock.signInMessage"),
            TextWrapping = TextWrapping.Wrap });
        body.Children.Add(user);
        body.Children.Add(password);
        body.Children.Add(problem);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE72E", AppStrings.Get("clock.signInTitle")),
            Content = body,
            PrimaryButtonText = AppStrings.Get("clock.signInAction"),
            CloseButtonText = AppStrings.Get("hub.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                var name = user.Text.Trim();
                if (name.Length == 0 || password.Password.Length == 0)
                {
                    Problem("clock.signInEmpty");
                    return;
                }
                dialog.IsPrimaryButtonEnabled = false;
                problem.IsOpen = false;
                if (!await App.DeviceHub.PairClockAsync(clock.Address, clock.Port, name,
                        password.Password, CancellationToken.None))
                {
                    Problem("clock.signInWrong");
                    return;
                }
                signedIn = true;
                args.Cancel = false;
            }
            catch (UnauthorizedAccessException)
            {
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
        void Problem(string key)
        {
            problem.Message = AppStrings.Get(key);
            problem.IsOpen = true;
        }
        try
        {
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // A different dialog is still closing.
        }
        return signedIn;
    }

    private void DeviceTile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HubDeviceListItem item && App.MainWindow is MainWindow window)
            window.NavigateToDevice(item.Device);
    }

    private void DeviceHub_DevicesChanged(object? sender, EventArgs e) => EnqueueRefresh();
    private void ConnectionStatus_Changed(object? sender, EventArgs e) => EnqueueRefresh();
    private void DeviceHub_ScanStateChanged(object? sender, EventArgs e) => EnqueueRefresh();
    private void AppStrings_LanguageChanged(object? sender, EventArgs e) => EnqueueRefresh();

    private void EnqueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (_isSubscribed) RefreshPage();
        })) Interlocked.Exchange(ref _refreshQueued, 0);
    }

    private void RefreshPage()
    {
        ApplyLanguage();

        var devices = App.DeviceHub.Devices;
        DeviceTiles.ItemsSource = devices
            .Select(device => new HubDeviceListItem(device, BuildDeviceDetail(device)))
            .ToList();

        var isEmpty = devices.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        DeviceTiles.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

        UpdateScanState();
    }

    private void ApplyLanguage()
    {
        PageTitle.Text = AppStrings.Get("hub.title");
        PageSubtitle.Text = AppStrings.Get("hub.subtitle");
        EmptyTitle.Text = AppStrings.Get("hub.emptyTitle");
        EmptyDescription.Text = AppStrings.Get("hub.emptyDescription");
        ToolTipService.SetToolTip(AddDeviceButton, AppStrings.Get("hub.addDevice"));
    }

    private void UpdateScanState()
    {
        var isScanning = App.DeviceHub.IsScanning;
        ScanButton.IsEnabled = !isScanning;
        ScanButtonText.Text = AppStrings.Get(isScanning ? "nav.scanning" : "nav.scan");
        ScanIcon.Visibility = isScanning ? Visibility.Collapsed : Visibility.Visible;
        ScanProgress.IsActive = isScanning;
        ScanProgress.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildDeviceDetail(DeviceDescriptor device)
    {
        if (device.Kind == DeviceKind.Macro)
            return "USB HID";

        if (!string.IsNullOrWhiteSpace(device.Address))
            return device.Port > 0 ? $"{device.Address}:{device.Port}" : device.Address;

        return device.Detail;
    }
}

public sealed class HubDeviceListItem
{
    private static readonly ImageSource MacroImage = new BitmapImage(new Uri("ms-appx:///Assets/macropad-nobg.png"));
    private static readonly ImageSource ClockImage = new BitmapImage(new Uri("ms-appx:///Assets/smartclock-nobg_2.png"));

    public HubDeviceListItem(DeviceDescriptor device, string detail)
    {
        Device = device;
        Detail = detail;
        Status = DeviceState.StatusText(device);
        var connected = device.Kind == DeviceKind.Macro ? device.IsOnline : DeviceState.IsSignedIn(device);
        StatusGlyph = connected ? "\uF13E" : "\uF13D";
        StatusBrush = new SolidColorBrush(connected ? Colors.LimeGreen : Colors.Gray);
        ImageOpacity = device.IsOnline ? 1 : 0.58;
        DeviceImage = device.Kind == DeviceKind.Macro ? MacroImage : ClockImage;
    }

    public DeviceDescriptor Device { get; }
    public string DisplayName => Device.DisplayName;
    public string Detail { get; }
    public string Status { get; }
    public string StatusGlyph { get; }
    public Brush StatusBrush { get; }
    public ImageSource DeviceImage { get; }
    public double ImageOpacity { get; }
}
