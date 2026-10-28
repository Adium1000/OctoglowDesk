using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

public sealed partial class SettingsPage : Page
{
    private AppConfig _config = ConfigStore.Load();
    private bool _isRestoringDefaults;

    public SettingsPage()
    {
        InitializeComponent();
        // The same centred column as the clock's pages, so the cards line up with theirs.
        CenteredColumn.Attach(PageScroller, PageStack, maxWidth: 976, gutter: 32);
        // Just the switch, flush with the right edge like the clock pages' ones: no
        // On/Off label, and not the room the template keeps for one.
        foreach (var toggle in new[] { StartupSwitch, TraySwitch, InlineClockConfigurationSwitch,
                     HideUsbSwitch, HideWifiSwitch, ShowDeviceRefreshSwitch })
        {
            toggle.OnContent = "";
            toggle.OffContent = "";
            toggle.MinWidth = 0;
            toggle.Margin = new Thickness(0, 0, -12, 0);
        }
        LanguageBox.SelectedIndex = _config.Language switch { "ro" => 1, "en" => 2, _ => 0 };
        ThemeBox.SelectedIndex = _config.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        BackdropBox.SelectedIndex = _config.BackdropMaterial switch { "none" => 0, "mica" => 1, "acrylic" => 3, _ => 2 };
        StartupSwitch.IsOn = _config.RunAtStartup;
        TraySwitch.IsOn = _config.MinimizeToTray;
        InlineClockConfigurationSwitch.IsOn = _config.InlineClockConfigurationPages;
        HideUsbSwitch.IsOn = _config.HideDisconnectedUsbDevices;
        HideWifiSwitch.IsOn = _config.HideDisconnectedWifiDevices;
        ShowDeviceRefreshSwitch.IsOn = _config.ShowNetworkDeviceRefreshButtons;

        LanguageBox.SelectionChanged += LanguageBox_SelectionChanged;
        ThemeBox.SelectionChanged += ThemeBox_SelectionChanged;
        BackdropBox.SelectionChanged += BackdropBox_SelectionChanged;
        StartupSwitch.Toggled += StartupSwitch_Toggled;
        TraySwitch.Toggled += TraySwitch_Toggled;
        InlineClockConfigurationSwitch.Toggled += InlineClockConfigurationSwitch_Toggled;
        HideUsbSwitch.Toggled += HideUsbSwitch_Toggled;
        HideWifiSwitch.Toggled += HideWifiSwitch_Toggled;
        ShowDeviceRefreshSwitch.Toggled += ShowDeviceRefreshSwitch_Toggled;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ApplyLanguage();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AppStrings.LanguageChanged += OnLanguageChanged;
    private void OnUnloaded(object sender, RoutedEventArgs e) => AppStrings.LanguageChanged -= OnLanguageChanged;
    private void OnLanguageChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ApplyLanguage);

    private void SavePreference(Action<AppConfig> change) => _config = ConfigStore.Update(change);

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        var language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "ro" => "ro", "en" => "en", _ => "system",
        };
        SavePreference(config => config.Language = language);
        AppStrings.SetLanguage(language);
        ApplyLanguage();
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        var theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "light" => "light", "dark" => "dark", _ => "system",
        };
        SavePreference(config => config.Theme = theme);
        ThemeService.Apply(theme);
    }

    private void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        var backdrop = (BackdropBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "none" => "none", "mica" => "mica", "acrylic" => "acrylic", _ => "micaalt",
        };
        SavePreference(config => config.BackdropMaterial = backdrop);
        ThemeService.ApplyBackdrop(backdrop);
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        var enabled = StartupSwitch.IsOn;
        if (!StartupService.SetRunAtStartup(enabled))
        {
            _isRestoringDefaults = true;
            _config = ConfigStore.Load();
            StartupSwitch.IsOn = _config.RunAtStartup;
            _isRestoringDefaults = false;
            return;
        }
        SavePreference(config => config.RunAtStartup = enabled);
    }

    private void TraySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        SavePreference(config => config.MinimizeToTray = TraySwitch.IsOn);
    }

    private void InlineClockConfigurationSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        SavePreference(config => config.InlineClockConfigurationPages = InlineClockConfigurationSwitch.IsOn);
    }

    private void HideUsbSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        SavePreference(config => config.HideDisconnectedUsbDevices = HideUsbSwitch.IsOn);
        App.DeviceHub.RefreshDeviceVisibility();
    }

    private void HideWifiSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        SavePreference(config => config.HideDisconnectedWifiDevices = HideWifiSwitch.IsOn);
        App.DeviceHub.RefreshDeviceVisibility();
    }

    private void ShowDeviceRefreshSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isRestoringDefaults) return;
        SavePreference(config => config.ShowNetworkDeviceRefreshButtons = ShowDeviceRefreshSwitch.IsOn);
    }

    private async void ClearDevices_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE74D", AppStrings.Get("settings.clearDevicesConfirmTitle")),
            Content = AppStrings.Get("settings.clearDevicesConfirmDescription"),
            PrimaryButtonText = AppStrings.Get("settings.clearDevices"),
            CloseButtonText = AppStrings.Get("hub.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ClearDevicesButton.IsEnabled = false;
        try
        {
            await App.DeviceHub.ClearAllDevicesAsync();
            _config = ConfigStore.Load();
        }
        catch (Exception error)
        {
            AppLog.Write($"[DEVICE] Nu am putut șterge dispozitivele: {error.Message}");
        }
        finally
        {
            ClearDevicesButton.IsEnabled = true;
        }
    }

    private async void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var isEnglish = AppStrings.IsEnglish;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE777", isEnglish ? "Restore app defaults?" : "Restabilești setările aplicației?"),
            Content = isEnglish
                ? "This resets appearance, startup, tray behavior, and configuration navigation. Octoglow Clock settings are kept."
                : "Se resetează aspectul, pornirea automată, comportamentul tray și navigarea configurărilor. Setările Octoglow Clock rămân neschimbate.",
            PrimaryButtonText = isEnglish ? "Restore" : "Restabilește",
            CloseButtonText = isEnglish ? "Cancel" : "Anulează",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var defaults = new AppConfig();
        _isRestoringDefaults = true;
        try
        {
            SavePreference(config =>
            {
                config.Language = defaults.Language;
                config.Theme = defaults.Theme;
                config.BackdropMaterial = defaults.BackdropMaterial;
                config.RunAtStartup = defaults.RunAtStartup;
                config.MinimizeToTray = defaults.MinimizeToTray;
                config.InlineClockConfigurationPages = defaults.InlineClockConfigurationPages;
                config.HideDisconnectedUsbDevices = defaults.HideDisconnectedUsbDevices;
                config.HideDisconnectedWifiDevices = defaults.HideDisconnectedWifiDevices;
                config.ShowNetworkDeviceRefreshButtons = defaults.ShowNetworkDeviceRefreshButtons;
            });
            LanguageBox.SelectedIndex = 0;
            ThemeBox.SelectedIndex = 0;
            BackdropBox.SelectedIndex = 2;
            StartupSwitch.IsOn = _config.RunAtStartup;
            TraySwitch.IsOn = _config.MinimizeToTray;
            InlineClockConfigurationSwitch.IsOn = _config.InlineClockConfigurationPages;
            HideUsbSwitch.IsOn = _config.HideDisconnectedUsbDevices;
            HideWifiSwitch.IsOn = _config.HideDisconnectedWifiDevices;
            ShowDeviceRefreshSwitch.IsOn = _config.ShowNetworkDeviceRefreshButtons;
            StartupService.SetRunAtStartup(_config.RunAtStartup);
            ThemeService.Apply(_config.Theme);
            ThemeService.ApplyBackdrop(_config.BackdropMaterial);
            App.DeviceHub.RefreshDeviceVisibility();
        }
        finally
        {
            _isRestoringDefaults = false;
        }
        AppStrings.SetLanguage(_config.Language);
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        PageTitle.Text = AppStrings.Get("settings.title");
        PageSubtitle.Text = AppStrings.Get("settings.appSubtitle");

        AppearanceHeading.Text = AppStrings.Get("settings.appearance");
        ThemeTitle.Text = AppStrings.Get("settings.theme");
        ThemeDescription.Text = AppStrings.Get("settings.theme.description");
        ThemeSystemItem.Content = AppStrings.Get("settings.theme.system");
        ThemeLightItem.Content = AppStrings.Get("settings.theme.light");
        ThemeDarkItem.Content = AppStrings.Get("settings.theme.dark");
        BackdropTitle.Text = AppStrings.Get("settings.backdrop");
        BackdropDescription.Text = AppStrings.Get("settings.backdrop.description");
        BackdropNoneItem.Content = AppStrings.Get("settings.backdrop.none");
        BackdropMicaItem.Content = AppStrings.Get("settings.backdrop.mica");
        BackdropMicaAltItem.Content = AppStrings.Get("settings.backdrop.micaalt");
        BackdropAcrylicItem.Content = AppStrings.Get("settings.backdrop.acrylic");

        LanguageHeading.Text = AppStrings.Get("settings.language");
        LanguageTitle.Text = AppStrings.Get("settings.language.app");
        LanguageDescription.Text = AppStrings.Get("settings.language.description");
        LanguageSystemItem.Content = AppStrings.Get("settings.language.system");

        StartupHeading.Text = AppStrings.Get("settings.startupSection");
        StartupTitle.Text = AppStrings.Get("settings.startup");
        StartupDescription.Text = AppStrings.Get("settings.startupDescription");
        TrayTitle.Text = AppStrings.Get("settings.tray");
        TrayDescription.Text = AppStrings.Get("settings.trayDescription");

        NavigationHeading.Text = AppStrings.Get("settings.navigation");
        InlineClockConfigurationTitle.Text = AppStrings.Get("settings.inlineClockConfigurations");
        InlineClockConfigurationDescription.Text = AppStrings.Get("settings.inlineClockConfigurationsDescription");

        DevicesHeading.Text = AppStrings.Get("settings.devicesSection");
        HideUsbTitle.Text = AppStrings.Get("settings.hideUsb");
        HideUsbDescription.Text = AppStrings.Get("settings.hideUsbDescription");
        HideWifiTitle.Text = AppStrings.Get("settings.hideWifi");
        HideWifiDescription.Text = AppStrings.Get("settings.hideWifiDescription");
        ShowDeviceRefreshTitle.Text = AppStrings.Get("settings.showDeviceRefresh");
        ShowDeviceRefreshDescription.Text = AppStrings.Get("settings.showDeviceRefreshDescription");
        DevicesSettingsTitle.Text = AppStrings.Get("settings.devices");
        DevicesSettingsDescription.Text = AppStrings.Get("settings.devicesDescription");
        ClearDevicesButton.Content = AppStrings.Get("settings.clearDevices");

        ResetHeading.Text = AppStrings.Get("settings.reset");
        RestoreDefaultsTitle.Text = AppStrings.Get("settings.restoreDefaults");
        RestoreDefaultsDescription.Text = AppStrings.Get("settings.restoreDefaultsDescription");
        RestoreDefaultsButton.Content = AppStrings.Get("settings.restore");

        // The titles sit beside the controls rather than on them, so a screen
        // reader is told which setting each one is.
        AutomationProperties.SetName(ThemeBox, ThemeTitle.Text);
        AutomationProperties.SetName(BackdropBox, BackdropTitle.Text);
        AutomationProperties.SetName(LanguageBox, LanguageTitle.Text);
        AutomationProperties.SetName(StartupSwitch, StartupTitle.Text);
        AutomationProperties.SetName(TraySwitch, TrayTitle.Text);
        AutomationProperties.SetName(InlineClockConfigurationSwitch, InlineClockConfigurationTitle.Text);
        AutomationProperties.SetName(HideUsbSwitch, HideUsbTitle.Text);
        AutomationProperties.SetName(HideWifiSwitch, HideWifiTitle.Text);
        AutomationProperties.SetName(ShowDeviceRefreshSwitch, ShowDeviceRefreshTitle.Text);
        AutomationProperties.SetName(RestoreDefaultsButton, RestoreDefaultsTitle.Text);
    }
}
