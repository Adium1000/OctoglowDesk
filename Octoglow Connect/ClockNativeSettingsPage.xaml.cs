using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace OctoglowSender;

internal enum ClockSettingsSection
{
    Configuration,
    TileManager,
    LanguageManager,
    Buzzer,
    Touch,
    Integrations,
    MediaPlayer,
    ObsStudio,
    QBittorrent,
    MsiAfterburner,
    Ets2,
    BeamNg,
    AssettoCorsa,
    Nfsu2,
    NfsMw,
    NfsCarbon,
    Voicemeeter,
    ObsGuide,
    Ets2Guide,
    BeamNgGuide,
    BrowserDownloadsGuide,
    QBittorrentGuide,
    MsiAfterburnerGuide,
    Brightness,
    Wifi,
    AccessPoint,
    GeneralManagement,
    Preferences,
    Power,
    Hardware,
    Backup,
    Account,
    Credentials,
    HelpManager,
    About,
}

internal sealed record ClockSettingsNavigation(ClockSettingsSection Section, DeviceDescriptor? Device);

public sealed partial class ClockNativeSettingsPage : Page
{
    private DeviceDescriptor? _device;
    private ClockDeviceState? _nowPlayingState;
    private ClockDeviceState? _weatherState;
    private bool _subscribed;

    public ClockNativeSettingsPage()
    {
        InitializeComponent();
        Language = UiLanguage;
        CenteredColumn.Attach(PageScroller, PageStack, maxWidth: 976, gutter: 32);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ClockSettingsNavigation navigation)
        {
            PageSurface.Section = navigation.Section;
            _device = navigation.Device;
        }
        ApplyHeader(PageSurface);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            AppStrings.LanguageChanged += OnLanguageChanged;
        }
        _ = LoadSurfaceAsync(PageSurface);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _tileDialogCancellation?.Cancel();
        _tileDialog?.Hide();
        ClosePanel(animate: false);
        PageSurface.Cancellation?.Cancel();
        DiscardSubPage(PageSurface);
        StopSection(PageSurface);
        if (!_subscribed) return;
        _subscribed = false;
        AppStrings.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        Language = UiLanguage;
        _tileDialogCancellation?.Cancel();
        // An open tile dialog was built in the old language; it closes rather than mix the two.
        _tileDialog?.Hide();
        foreach (var surface in Surfaces().ToList())
        {
            // A section set aside under a page of its own was built in the old language too; going back rebuilds it.
            surface.SectionChildren = null;
            ApplyHeader(surface);
            Reload(surface);
        }
    });

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload(PageSurface);

    private void Reload(SettingsSurface surface)
    {
        if (surface.SubPage is not null) _ = LoadSubPageAsync(surface);
        else _ = LoadSurfaceAsync(surface);
    }

    private async Task LoadSurfaceAsync(SettingsSurface surface)
    {
        DismissTileDialog(surface);
        PauseTileLineReads();
        surface.Cancellation?.Cancel();
        surface.Status.IsOpen = false;
        if (IsLocalSection(surface.Section))
        {
            // Nothing to read first: cards that open the panels, or the app's own settings.
            surface.Loading.Visibility = Visibility.Collapsed;
            surface.Refresh.Visibility = Visibility.Collapsed;
            BuildSection(surface, null);
            return;
        }

        var cancellation = new CancellationTokenSource();
        surface.Cancellation = cancellation;
        var token = cancellation.Token;
        surface.Loading.Visibility = Visibility.Visible;
        surface.Refresh.Visibility = ShouldShowRefresh(surface) ? Visibility.Visible : Visibility.Collapsed;
        surface.Refresh.IsEnabled = false;
        try
        {
            // Now Playing and weather read their own state when their tile's settings open.
            var state = await ClockDeviceApi.GetStateAsync(_device, token);
            if (!token.IsCancellationRequested) BuildSection(surface, state);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            StopSection(surface);
            surface.Content.Children.Clear();
            ShowError(surface.Status, FriendlyError(error));
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                surface.Loading.Visibility = Visibility.Collapsed;
                surface.Refresh.IsEnabled = true;
            }
        }
    }

    private void ApplyHeader(SettingsSurface surface)
    {
        if (surface.Popup?.Child is FrameworkElement overlay) overlay.Language = UiLanguage;
        if (surface == PageSurface)
        {
            PageHeader.Visibility = surface.SubPage is null ? Visibility.Collapsed : Visibility.Visible;
            PageIcon.Glyph = surface.SubPage?.Glyph ?? "";
            PageIcon.Visibility = string.IsNullOrEmpty(PageIcon.Glyph) ? Visibility.Collapsed : Visibility.Visible;
        }
        surface.Back.Visibility = surface.SubPage is null ? Visibility.Collapsed : Visibility.Visible;
        surface.Refresh.Visibility = ShouldShowRefresh(surface) ? Visibility.Visible : Visibility.Collapsed;
        // Named as well as tipped: a button that is only a glyph has nothing to
        // tell a screen reader otherwise.
        Name(surface.Back, L("Înapoi", "Back"));
        Name(surface.Refresh, L("Reîncarcă", "Refresh"));
        if (surface.Close is not null) Name(surface.Close, L("Închideți", "Close"));

        static void Name(Button button, string label)
        {
            ToolTipService.SetToolTip(button, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        }
        (surface.Title.Text, surface.Description.Text) = surface.SubPage is { } page
            ? (page.Title, page.Description)
            : SectionHeader(surface.Section);
    }

    private static bool ShouldShowRefresh(SettingsSurface surface) =>
        ConfigStore.ShowNetworkDeviceRefreshButtons &&
        !IsLocalSection(surface.Section) &&
        surface.Section is not (ClockSettingsSection.Integrations or ClockSettingsSection.Hardware or ClockSettingsSection.Wifi) &&
        surface.SubPage?.RequiresDeviceState != false;

    private void BuildSection(SettingsSurface surface, ClockDeviceState? state)
    {
        DiscardSubPage(surface);
        StopSection(surface);
        ApplyHeader(surface);
        surface.Content.Children.Clear();
        surface.Status.IsOpen = false;
        BuildInto(surface, () => surface.SectionCleanup = BuildSectionContent(surface.Section, state));
    }

    // Fills a section in, and hands back what stops it again - such as the
    // hardware monitor's poll - for when it is rebuilt or closed.
    private Action? BuildSectionContent(ClockSettingsSection section, ClockDeviceState? state)
    {
        if (section == ClockSettingsSection.Configuration)
        {
            BuildConfiguration();
            return null;
        }
        if (section == ClockSettingsSection.Integrations)
            return state is not null ? BuildIntegrations(state) : null;
        if (section == ClockSettingsSection.Credentials)
        {
            BuildCredentials();
            return null;
        }
        switch (section)
        {
            case ClockSettingsSection.MediaPlayer:
                BuildMediaPlayerPage();
                return null;
            case ClockSettingsSection.ObsStudio:
                return BuildObsPage();
            case ClockSettingsSection.QBittorrent:
                return BuildQbitPage();
            case ClockSettingsSection.MsiAfterburner:
                return BuildAfterburnerPage();
            case ClockSettingsSection.Ets2:
                return BuildEts2Page();
            case ClockSettingsSection.BeamNg:
                return BuildBeamNgPage();
            case ClockSettingsSection.AssettoCorsa:
                return BuildAssettoCorsaPage();
            case ClockSettingsSection.Nfsu2:
                return BuildNfsu2Page();
            case ClockSettingsSection.NfsMw:
                return BuildNfsMwPage();
            case ClockSettingsSection.NfsCarbon:
                return BuildNfsCarbonPage();
            case ClockSettingsSection.Voicemeeter:
                BuildVoicemeeterPage();
                return null;
        }
        if (IsGuideSection(section))
        {
            BuildGuide(section);
            return null;
        }
        if (section == ClockSettingsSection.GeneralManagement)
        {
            BuildGeneralManagement();
            return null;
        }
        if (section == ClockSettingsSection.HelpManager)
        {
            BuildHelpManager();
            return null;
        }
        if (state is null) return null;
        switch (section)
        {
            case ClockSettingsSection.TileManager:
                return BuildCompleteTileManager(state);
            case ClockSettingsSection.LanguageManager:
                BuildLanguageManager(state);
                break;
            case ClockSettingsSection.Buzzer:
                BuildBuzzer(state);
                break;
            case ClockSettingsSection.Touch:
                BuildTouch(state);
                break;
            case ClockSettingsSection.Brightness:
                BuildBrightness(state);
                break;
            case ClockSettingsSection.Wifi:
                return BuildWifi(state);
            case ClockSettingsSection.AccessPoint:
                BuildAccessPoint(state);
                break;
            case ClockSettingsSection.Preferences:
                return BuildPreferences(state);
            case ClockSettingsSection.Power:
                return BuildPowerPage(state);
            case ClockSettingsSection.Hardware:
                return BuildHardwarePage(state);
            case ClockSettingsSection.Backup:
                BuildBackup();
                break;
            case ClockSettingsSection.Account:
                BuildAccount();
                break;
            case ClockSettingsSection.About:
                BuildAbout(state);
                break;
        }
        return null;
    }

    private static void StopSection(SettingsSurface surface)
    {
        surface.SectionCleanup?.Invoke();
        surface.SectionCleanup = null;
    }

    // Builds into one surface whichever is in front: the page can finish
    // loading while a panel is open over it.
    private void BuildInto(SettingsSurface surface, Action build)
    {
        var previous = _buildTarget;
        _buildTarget = surface.Content;
        try
        {
            build();
        }
        finally
        {
            _buildTarget = previous;
        }
    }

    // ------------------------------------------------------------------ languages

    private void BuildLanguageManager(ClockDeviceState state)
    {
        AddSection(L("Setări de limbă", "Language Settings"));

        Add(WeatherLanguageCard(state));

        // On the clock 1 is Romanian and 0 English (Octoglow.ino, WEEKDAY_NAMES_RO).
        var dateLanguage = ChoiceBox([("English (en)", "0"), ("Română (ro)", "1")], Str(state.Int("datelang")));
        dateLanguage.SelectionChanged += (_, _) =>
            QueueSave("datelang", token => Post(token, "/datelang", ("lang", ChoiceValue(dateLanguage))), 0);
        Add(SettingsCard("", L("Limba datei", "Date Language"), null, dateLanguage));

        // The words of the Events tile ("89 de zile", "89 days"): 0 is Romanian
        // and 1 English on the clock.
        if (state.Has("cdLang"))
        {
            var eventsLanguage = ChoiceBox([("English (en)", "1"), ("Română (ro)", "0")], Str(state.Int("cdLang")));
            eventsLanguage.SelectionChanged += (_, _) =>
                QueueSave("cdlang", token => Post(token, "/countdownopt", ("lang", ChoiceValue(eventsLanguage))), 0);
            Add(SettingsCard("\uE774", L("Limba tile-ului Evenimente", "Events tile language"), null, eventsLanguage));
        }

        // The web interface's own language, kept on the clock so every browser
        // opens in it.
        var interfaceLanguage = ChoiceBox([("English (en)", "en"), ("Română (ro)", "ro")], state.String("uiLang", "ro"));
        interfaceLanguage.SelectionChanged += (_, _) => QueueSetting("uilang", ChoiceValue(interfaceLanguage));
        Add(SettingsCard("\uE774", L("Limba interfeței web", "Web Interface Language"), null, interfaceLanguage));
    }

    // ------------------------------------------------------------------ buzzer

    private void BuildBuzzer(ClockDeviceState state)
    {
        AddSection(L("Volum", "Volume"));

        var enabled = Toggle(state.Bool("buzzer"));
        enabled.Toggled += (_, _) =>
            QueueSave("buzzer", token => Post(token, "/settings", ("buzzer", enabled.IsOn ? "1" : "0")), 0);
        Add(SettingsCard("", "Buzzer", L("Permite ceasului să redea sunete.", "Lets the clock play sounds."), enabled));

        var categories = new (string Preview, string StateKey, string Form, string Label)[]
        {
            ("notif", "buzzerVolNotif", "volnotif", L("Notificări", "Notifications")),
            ("auto", "buzzerVolAuto", "volauto", L("Acțiuni automate", "Automatic actions")),
            ("alarm", "buzzerVolAlarm", "volalarm", L("Alarme", "Alarms")),
            ("touch", "buzzerVolTouch", "voltouch", L("Senzor tactil", "Touch Sensor")),
        };
        var categorySliders = new List<Slider>();
        var volumeRows = new List<UIElement>();
        foreach (var category in categories)
        {
            var slider = NewSlider(0, 100, state.Int(category.StateKey, 80), 200);
            categorySliders.Add(slider);
            // Letting a category go plays a chime at its new level, the way the web interface does.
            slider.ValueChanged += (_, _) => QueueSave("vol:" + category.Form, token => Post(token, "/buzzersett",
                (category.Form, Str((int)Math.Round(slider.Value))), ("preview", category.Preview)), 450);
            volumeRows.Add(SubRow(category.Label, null, SliderWithValue(slider, Percent)));
        }

        var volume = NewSlider(0, 100, state.Int("buzzerVolume", 80), 200);
        volume.ValueChanged += (_, args) =>
        {
            // The clock moves every category along with the main volume.
            var value = Math.Round(args.NewValue);
            foreach (var category in categories) CancelSave("vol:" + category.Form);
            WithoutSaving(() => categorySliders.ForEach(slider => slider.Value = value));
            // A chime at the new level, the same as every category slider gives.
            QueueSave("volume", token => Post(token, "/buzzersett",
                ("volume", Str((int)Math.Round(volume.Value))), ("preview", "1")), 450);
        };
        Add(SettingsExpander("", L("Volum", "Volume"),
            L("Cât de tare sună beep-urile", "How loud the beeps are"),
            SliderWithValue(volume, Percent), volumeRows));

        AddSection(L("Sunete pentru evenimente", "Sounds for Events"));
        // In the web interface's order, which is also when each one sounds.
        var events = new (string Key, string StateKey, string Glyph, string Label, string Description)[]
        {
            ("tile", "evSndTile", "", L("Schimbare tile", "Tile Change"), L("Beep la schimbarea tile-ului", "Beep when the tile changes")),
            ("wifi", "evSndWifi", "", L("Conexiune WiFi Pierdută", "WiFi Connection Lost"), L("Sunet la pierderea conexiunii WiFi", "Sound when WiFi connection is lost")),
            ("notif", "evSndNotif", "", L("Notificare nouă", "New Notification"), L("Sunet la notificare nouă de la PC", "Sound on new notification from PC")),
            ("ets2", "evSndEts2", "", L("Depășire viteză ETS2", "ETS2 Speed Limit Exceeded"), L("Sunet la depășirea limitei de viteză", "Sound when the speed limit is exceeded")),
            ("web", "evSndWeb", "", L("Alerte", "Alerts"), L("Sunet la o alertă a interfeței web", "Sound on a web interface alert")),
            ("touch", "evSndTouch", "", L("Senzor tactil", "Touch Sensor"), L("Sunet la atingerea senzorului", "Sound when the sensor is touched")),
            ("timer", "evSndTimer", "", L("Temporizator", "Timer"), L("Sunet redat când countdown-ul ajunge la 0", "Sound played when the countdown reaches 0")),
            ("pomobreak", "evSndPomoBreak", "", L("Pomodoro: pauză", "Pomodoro: break"), L("Sunet când se termină o rundă de muncă", "Sound when a round of work ends")),
            ("pomowork", "evSndPomoWork", "", L("Pomodoro: muncă", "Pomodoro: work"), L("Sunet când se termină o pauză", "Sound when a break ends")),
        };
        foreach (var item in events)
        {
            // A firmware from before the Pomodoro has no such sounds.
            if (!state.Has(item.StateKey)) continue;
            var box = ChoiceBox(EventSounds(), state.String(item.StateKey, "none"), 220);
            box.SelectionChanged += (_, _) => QueueSave("event:" + item.Key, token => Post(token, "/eventsoundsett",
                ("event", item.Key), ("preset", ChoiceValue(box))), 0);
            Add(SettingsCard(item.Glyph, item.Label, item.Description, box));
        }
    }

    // ------------------------------------------------------------------ touch

    private void BuildTouch(ClockDeviceState state)
    {
        AddSection(L("Acțiuni gesturi", "Gesture Actions"));

        var tap = ChoiceBox(TouchActions(), Str(state.Int("touchTapAction", 8)), 240);
        tap.SelectionChanged += (_, _) => QueueSave("tap", token => Post(token, "/touchsett", ("tap", ChoiceValue(tap))), 0);
        Add(SettingsCard("", L("Atingere", "Tap"),
            L("Atingere scurtă a senzorului", "A short tap on the sensor"), tap));

        var doubleTap = ChoiceBox(TouchActions(), Str(state.Int("touchDoubleTapAction")), 240);
        doubleTap.SelectionChanged += (_, _) => QueueSave("dbl", token => Post(token, "/touchsett", ("dbl", ChoiceValue(doubleTap))), 0);
        Add(SettingsCard("", L("Atingere dublă", "Double Tap"),
            L("Două atingeri rapide, consecutive", "Two quick, consecutive taps"), doubleTap));
        AddTouchInsight();
    }

    // One informational surface, not a third settings category: these gestures
    // are fixed in firmware and cannot be configured here.
    private void AddTouchHoldHelp()
    {
        var card = new Border { CornerRadius = new CornerRadius(5) };
        card.Margin = new Thickness(0, 12, 0, 0);
        card.Padding = new Thickness(14, 12, 14, 12);

        var layout = new Grid { ColumnSpacing = 10 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new FontIcon { Glyph = "\uE946", FontSize = 18, VerticalAlignment = VerticalAlignment.Top };
        layout.Children.Add(icon);

        var copy = new StackPanel { Spacing = 4 };
        var title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        title.Text = L("Știați că?", "Did you know?");
        copy.Children.Add(title);
        copy.Children.Add(new TextBlock
        {
            Text = L("Comutați Wi-Fi / Access Point: țineți apăsat senzorul 2 secunde cât timp ceasul funcționează.",
                     "Switch Wi-Fi / Access Point: hold the sensor for 2 seconds while the clock is running."),
            TextWrapping = TextWrapping.Wrap,
        });
        copy.Children.Add(new TextBlock
        {
            Text = L("Resetare din fabrică: la pornire, când apare ecranul Wi-Fi sau Access Point, începeți apăsarea în primele 3 secunde și țineți 5 secunde. Setările, rețeaua Wi-Fi și contul de acces se șterg.",
                     "Factory Reset: at power-up, when the Wi-Fi or Access Point screen appears, start pressing within the first 3 seconds and hold for 5 seconds. The settings, the Wi-Fi network and the login account are erased."),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(copy, 1);
        layout.Children.Add(copy);
        card.Child = layout;

        // An informational strip, not a third settings card. Keep its blue
        // contrast in both themes without inheriting the user's accent color.
        void ApplyColors()
        {
            var light = card.ActualTheme == ElementTheme.Light;
            card.Background = new SolidColorBrush(light
                ? ColorHelper.FromArgb(255, 225, 242, 252)
                : ColorHelper.FromArgb(255, 27, 49, 63));
            icon.Foreground = title.Foreground = new SolidColorBrush(light
                ? ColorHelper.FromArgb(255, 10, 94, 145)
                : ColorHelper.FromArgb(255, 104, 204, 247));
        }
        card.ActualThemeChanged += (_, _) => ApplyColors();
        ApplyColors();
        Add(card);
    }

    // ------------------------------------------------------------------ about

    // The clock's name on the network (POST /hostname), as the web interface's
    // About has it. The app picks the new name up on its next search.
    private Button HostNameCard(string current)
    {
        var parts = ActionCard("\uE753", L("Nume în rețea", "Network name"),
            current.Length > 0 ? current + ".local" : "-", L("Editați", "Edit"), "\uE70F");
        parts.Card.Click += async (_, _) => await ShowHostNameDialogAsync(parts, current);
        return parts.Card;
    }

    private async Task ShowHostNameDialogAsync(ActionCardParts parts, string current)
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null || _tileDialog is not null) return;

        var description = new TextBlock
        {
            Text = L("Ceasul răspunde și la acest nume, nu doar la adresa IP. Dacă aveți mai multe ceasuri, dați-le nume diferite.",
                     "The clock answers to this name as well as to its IP address. If you have several clocks, give them different names."),
            TextWrapping = TextWrapping.Wrap,
        };
        var box = new TextBox { Text = current, MaxLength = 32, IsSpellCheckEnabled = false,
            PlaceholderText = "octoglow-xxxx", MinWidth = 240 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, L("Nume în rețea", "Network name"));
        var errorBar = new InfoBar { IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Error };
        var body = new StackPanel { Width = Math.Clamp(root.Size.Width - 120, 280, 430), Spacing = 14 };
        body.Children.Add(description);
        body.Children.Add(Inline(box, new TextBlock { Text = ".local", Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center }));
        body.Children.Add(errorBar);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = DialogHeading.Create("\uE753", L("Nume în rețea", "Network name")),
            Content = body,
            PrimaryButtonText = L("Salvați", "Save"),
            CloseButtonText = L("Anulați", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeSource.ActualTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = body.Width + 64;
        var savedName = "";
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                var name = box.Text.Trim().ToLowerInvariant();
                if (!HostNameValid(name))
                {
                    ShowError(errorBar, L("Folosiți litere mici, cifre și cratimă (nu la început sau la sfârșit), cel mult 32.",
                                          "Use lowercase letters, digits and hyphens (not at the start or end), up to 32."));
                    return;
                }
                dialog.IsPrimaryButtonEnabled = false;
                errorBar.IsOpen = false;
                await Post(CancellationToken.None, "/hostname", ("name", name));
                savedName = name;
                parts.Description.Text = name + ".local";
                args.Cancel = false;
            }
            catch (Exception error)
            {
                ShowError(errorBar, FriendlyError(error));
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        _tileDialog = dialog;
        try
        {
            await dialog.ShowAsync();
            if (savedName.Length > 0)
                ShowSuccess(L($"Ceasul se numește acum {savedName}.local.", $"The clock is now called {savedName}.local."));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog is already open.
        }
        finally
        {
            _tileDialog = null;
        }
    }

    // Lowercase letters, digits and hyphens, not at either end, up to 32: a DNS label.
    private static bool HostNameValid(string name) =>
        name.Length is >= 1 and <= 32 && name[0] != '-' && name[^1] != '-' &&
        name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private void BuildAbout(ClockDeviceState state)
    {
        // Laid out as the web interface's About: its information first, then the
        // software, then what only the app needs.
        AddSection(L("Informații", "Information"));
        var uptime = TimeSpan.FromSeconds(Math.Max(0, state.Int("uptime")));
        Add(SettingsCard("", L("Adresa IP", "IP Address"), null, ValueText(state.String("ip", _device?.Address ?? "-"))));
        var hostName = state.String("hostName", "");
        Add(HostNameCard(hostName));
        Add(SettingsCard("", L("Timp activ", "Uptime"), null, ValueText(FormatUptime(uptime))));

        AddSection(L("Software", "Software"));
        var installed = state.String("version", "");
        Add(SettingsCard("", L("Versiune", "Version"), null, ValueText(installed.Length > 0 ? installed : "-")));
        Add(NavCard("\uE895", L("Actualizări software", "Software Updates"),
            L("Verificați și instalați o versiune nouă", "Check for and install a new version"),
            () => _ = ShowSoftwareUpdateDialogAsync(installed)));
        Add(NavCard("\uE8A5", L("Licențe", "Licenses"),
            L("Licența Octoglow și componentele open-source", "Octoglow's license and its open-source components"),
            () => _ = ShowClockLicensesDialogAsync()));

        AddSection(L("Conexiune", "Connection"));
        Add(NavCard(SectionGlyph(ClockSettingsSection.Credentials), AppStrings.Get("clock.credentials"),
            AppStrings.Get("clock.credentialsDescription"),
            () =>
            {
                // Only one panel is open at a time, so this one steps aside for
                // it - unless About is a panel over this very page, which can
                // open the next one itself.
                if (_panelOwner is { } owner)
                {
                    var device = _device;
                    ClosePanel();
                    ShowPanel(ClockSettingsSection.Credentials, device, owner);
                    return;
                }
                if (_panel is not null)
                {
                    ClosePanel();
                    OpenPanel(ClockSettingsSection.Credentials);
                    return;
                }
                OpenConfigurationSection(ClockSettingsSection.Credentials);
            }));
    }

    // Search only once the popup is visible, so the user sees the progress
    // immediately. Firmware is downloaded only after they choose Install.
    private async Task ShowSoftwareUpdateDialogAsync(string installed)
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null || _tileDialog is not null) return;

        var progress = new ProgressRing { IsActive = true, Width = 28, Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left };
        var status = new TextBlock { Text = L("Se caută actualizări…", "Checking for updates…"),
            TextWrapping = TextWrapping.Wrap };
        var version = new TextBlock { Text = L($"Versiune instalată: {(installed.Length > 0 ? installed : "-")}",
                                              $"Installed version: {(installed.Length > 0 ? installed : "-")}"),
            Opacity = 0.72 };
        var notice = new TextBlock
        {
            Text = L("Firmware-ul se descarcă și se trimite pe ceas. Nu scoateți ceasul din priză; acesta se va reporni după instalare.",
                     "The firmware is downloaded and sent to the clock. Don't unplug the clock; it will restart after installation."),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Opacity = 0.72,
        };
        var errorBar = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error };
        var body = new StackPanel { Width = Math.Clamp(root.Size.Width - 120, 280, 440), Spacing = 14 };
        body.Children.Add(progress);
        body.Children.Add(status);
        body.Children.Add(version);
        body.Children.Add(notice);
        body.Children.Add(errorBar);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = DialogHeading.Create("\uE895", L("Actualizări software", "Software Updates")),
            Content = body,
            CloseButtonText = L("Închideți", "Close"),
            DefaultButton = ContentDialogButton.None,
            RequestedTheme = ThemeSource.ActualTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = body.Width + 64;
        FirmwareRelease? available = null;
        var installing = false;
        var installedSuccessfully = false;
        using var checking = new CancellationTokenSource();

        dialog.Opened += async (_, _) =>
        {
            try
            {
                var release = await FirmwareUpdates.FindLatestAsync(checking.Token);
                if (checking.IsCancellationRequested) return;
                progress.IsActive = false;
                progress.Visibility = Visibility.Collapsed;
                if (FirmwareUpdates.IsNewer(release.Version, installed))
                {
                    available = release;
                    status.Text = L($"Versiunea {release.Version} este disponibilă.",
                                    $"Version {release.Version} is available.");
                    notice.Visibility = Visibility.Visible;
                    dialog.PrimaryButtonText = L("Instalați", "Install");
                    dialog.DefaultButton = ContentDialogButton.Primary;
                }
                else
                {
                    status.Text = L($"Ceasul are deja cea mai nouă versiune ({release.Version}).",
                                    $"The clock is already on the newest version ({release.Version}).");
                }
            }
            catch (OperationCanceledException) when (checking.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                progress.IsActive = false;
                progress.Visibility = Visibility.Collapsed;
                status.Visibility = Visibility.Collapsed;
                ShowStatus(errorBar, InfoBarSeverity.Error,
                    L("Nu s-au putut verifica actualizările", "Could not check for updates"),
                    FriendlyError(error));
            }
        };
        dialog.Closing += (_, args) =>
        {
            if (installing) args.Cancel = true;
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                if (available is null) return;
                installing = true;
                dialog.IsPrimaryButtonEnabled = false;
                errorBar.IsOpen = false;
                status.Visibility = Visibility.Visible;
                progress.Visibility = Visibility.Visible;
                progress.IsActive = true;
                status.Text = L("Se descarcă firmware-ul…", "Downloading firmware…");
                var report = new Progress<int>(percent =>
                    status.Text = L($"Se descarcă firmware-ul… {percent}%", $"Downloading firmware… {percent}%"));
                var firmware = await FirmwareUpdates.DownloadAsync(available, report, CancellationToken.None);
                status.Text = L("Se trimite firmware-ul pe ceas…", "Sending firmware to the clock…");
                using var stream = new MemoryStream(firmware);
                await ClockDeviceApi.UploadFirmwareAsync(_device, stream, available.AssetName, CancellationToken.None);
                installedSuccessfully = true;
                args.Cancel = false;
            }
            catch (Exception error)
            {
                status.Visibility = Visibility.Collapsed;
                ShowStatus(errorBar, InfoBarSeverity.Error,
                    L("Actualizarea nu a putut fi instalată", "Could not install the update"),
                    FriendlyError(error));
            }
            finally
            {
                installing = false;
                progress.IsActive = false;
                progress.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        _tileDialog = dialog;
        try
        {
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog is already open.
        }
        finally
        {
            checking.Cancel();
            _tileDialog = null;
        }
        if (installedSuccessfully)
            ShowSuccess(L("Firmware-ul a fost trimis. Ceasul se repornește.", "Firmware uploaded. The clock is restarting."));
    }

    // ------------------------------------------------------------------ shared

    // For actions that are not a setting - connecting, changing the account -
    // which say how they went in the bar at the top of the page.
    private async Task RunActionAsync(Button button, Func<CancellationToken, Task> operation, string? success = null)
    {
        button.IsEnabled = false;
        Surface.Status.IsOpen = false;
        try
        {
            await operation(CancellationToken.None);
            ShowSuccess(success ?? L("Setările au fost salvate pe ceas.", "Settings saved to the clock."));
        }
        catch (Exception error)
        {
            ShowError(FriendlyError(error));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // Into the tile dialog while one is open, since it covers the panel or page under it.
    private void ShowSuccess(string message) =>
        ShowStatus(_dialogStatus ?? Surface.Status, InfoBarSeverity.Success, L("Gata", "Done"), message);

    private void ShowError(string message) => ShowError(_dialogStatus ?? Surface.Status, message);

    private static void ShowError(InfoBar bar, string message) =>
        ShowStatus(bar, InfoBarSeverity.Error, L("Nu s-a putut finaliza", "Could not complete"), message);

    private static void ShowStatus(InfoBar bar, InfoBarSeverity severity, string title, string message)
    {
        bar.Severity = severity;
        bar.Title = title;
        bar.Message = message;
        bar.IsOpen = true;
    }

    private static string FriendlyError(Exception error) => error switch
    {
        UnauthorizedAccessException => error.Message,
        TaskCanceledException => L("Ceasul nu a răspuns la timp.", "The clock did not respond in time."),
        HttpRequestException => L("Nu s-a putut comunica cu ceasul. ", "Could not communicate with the clock. ") + error.Message,
        _ => error.Message,
    };

    private static string FormatUptime(TimeSpan value) => AppStrings.IsEnglish
        ? $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m"
        : $"{(int)value.TotalDays}z {value.Hours}h {value.Minutes}m";

    private static TimePicker TimeBox(string value)
    {
        var picker = new TimePicker { ClockIdentifier = "24HourClock", MinuteIncrement = 1 };
        if (TimeSpan.TryParseExact(value, @"hh\:mm", Inv, out var time)) picker.Time = time;
        return picker;
    }

    private static string FormatTime(TimeSpan time) => $"{time.Hours:00}:{time.Minutes:00}";

    private static string L(string ro, string en) => AppStrings.IsEnglish ? en : ro;

    private static IEnumerable<KeyValuePair<string, string>> Form(params (string Key, string Value)[] values) =>
        values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value));

    private static T Resource<T>(string key) where T : class => (T)Application.Current.Resources[key];

    // ------------------------------------------------------------------ choice lists
    // Built on demand rather than once, so they follow a change of language.

    private static (string, string)[] BuzzerPresets() =>
    [
        ("Calm", "calm"), (L("Puternic", "Loud"), "loud"), ("Urgent", "urgent"),
        (L("Discret", "Soft"), "soft"), (L("Bip dublu", "Double Beep"), "double"), (L("Bip triplu", "Triple Beep"), "triple"),
    ];

    private static (string, string)[] EventSounds() =>
    [
        (L("Niciunul", "None"), "none"), (L("Clopoțel de sticlă", "Glass Bell"), "glass"), (L("Cutie muzicală", "Music Box"), "musicbox"),
        ("Kalimba", "kalimba"), ("Ding-Dong", "dingdong"), (L("Picătură", "Droplet"), "droplet"), (L("Reușită", "Success"), "success"),
        (L("Ping moale", "Soft Ping"), "softping"), (L("Auroră", "Aurora"), "aurora"), (L("Armonie", "Harmony"), "harmony"),
        (L("Sclipire", "Twinkle"), "twinkle"), ("Calm", "calm"), (L("Puternic", "Loud"), "loud"), ("Urgent", "urgent"),
        (L("Discret", "Soft"), "soft"), (L("Bip dublu", "Double Beep"), "double"), (L("Bip triplu", "Triple Beep"), "triple"),
        (L("Clinchet", "Chime"), "chime"), (L("Clopot", "Bell"), "bell"), (L("Sonerie", "Doorbell"), "doorbell"),
        (L("Xilofon", "Xylophone"), "xylophone"), (L("Glisando de harpă", "Harp Glissando"), "harp"), ("Marimba", "marimba"),
        (L("Sclipici de cristal", "Crystal Sparkle"), "crystal"), (L("Val blând", "Gentle Wave"), "wave"),
        (L("Cântec de leagăn", "Lullaby"), "lullaby"), ("Ping Pong", "pingpong"), ("SOS", "sos"), (L("Sirenă", "Siren"), "siren"),
        (L("Claxon", "Horn"), "klaxon"), ("Laser", "laser"), (L("Bipuri robot", "Robot Beeps"), "robot"),
        (L("Fanfară", "Fanfare"), "fanfare"), (L("Oprire", "Power Down"), "powerdown"), (L("Pornire", "Power Up"), "powerup"),
        (L("Bătaie de inimă", "Heartbeat"), "heartbeat"), ("Sci-Fi", "scifi"), ("Arcade", "arcade"), (L("Gong zen", "Zen Gong"), "zen"),
        (L("Bule", "Bubbles"), "bubble"), (L("Fluierat", "Whistle"), "whistle"), (L("Alertă puternică", "Loud Alert"), "boldalert"),
    ];

    private static (string, string)[] TouchActions() =>
    [
        (L("Nu face nimic", "Do nothing"), "0"), (L("Tile anterior", "Previous Tile"), "1"), (L("Tile următor", "Next Tile"), "2"),
        (L("Pornește / oprește ecranul", "Turn the screen on / off"), "3"), (L("Creșteți luminozitatea", "Increase Brightness"), "4"),
        (L("Scădeți luminozitatea", "Decrease Brightness"), "5"), (L("Activează / dezactivează buzzer-ul", "Mute / unmute the buzzer"), "6"),
        (L("Repornește ESP32", "Restart the ESP32"), "7"), (L("Arată adresa IP", "Show the IP address"), "8"),
    ];
}
