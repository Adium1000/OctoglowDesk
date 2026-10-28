using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace OctoglowSender;

// Where a section is shown: the page itself, or a panel over it. Configuration
// opens each of the clock's settings - the Tile Manager, Wi-Fi, backups and the rest -
// in a panel of its own, which works the way the page does - a header with
// back, refresh and save status, a status bar, pages one level deeper - so
// the builders fill either one without knowing which.
public sealed partial class ClockNativeSettingsPage
{
    private sealed class SettingsSurface
    {
        public required ClockSettingsSection Section { get; set; }
        public required StackPanel Content { get; init; }
        public required ScrollViewer Scroller { get; init; }
        public required InfoBar Status { get; init; }
        public required UIElement Loading { get; init; }
        public required Button Refresh { get; init; }
        public required Button Back { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Description { get; init; }
        public CancellationTokenSource? Cancellation { get; set; }
        // Stops what the section left running, such as the hardware monitor's poll.
        public Action? SectionCleanup { get; set; }
        public SubPage? SubPage { get; set; }
        public Action? SubPageCleanup { get; set; }
        // The pages left open behind this one: going back walks them in turn
        // before the section itself comes back.
        public List<SubPage> SubPageStack { get; } = new();
        // The section set aside while a page of its own is open, and where it was scrolled.
        public List<UIElement>? SectionChildren { get; set; }
        public double SectionOffset { get; set; }
        public bool DragScrollHooked { get; set; }
        public int NavigationVersion { get; set; }

        // Only a panel has these.
        public Popup? Popup { get; init; }
        public Button? Close { get; init; }
        public SaveIndicator? SaveIndicator { get; init; }
        public Action? Detach { get; set; }
    }

    private SettingsSurface? _pageSurface;
    private SettingsSurface? _panel;

    private SettingsSurface PageSurface => _pageSurface ??= new SettingsSurface
    {
        Section = ClockSettingsSection.Configuration,
        Content = ContentHost,
        Scroller = PageScroller,
        Status = StatusBar,
        Loading = LoadingLayer,
        Refresh = RefreshButton,
        Back = BackButton,
        Title = PageTitle,
        Description = PageDescription,
    };

    // An open panel covers the page, so whatever is clicked, saved or reported belongs to it.
    private SettingsSurface Surface => _panel ?? PageSurface;

    private IEnumerable<SettingsSurface> Surfaces()
    {
        yield return PageSurface;
        if (_panel is not null) yield return _panel;
    }

    private static (string Title, string Description) SectionHeader(ClockSettingsSection section) => section switch
    {
        ClockSettingsSection.Configuration => (L("Configurări", "Configurations"), L("Tot ce se poate configura pe ceas", "Everything that can be set up on the clock")),
        ClockSettingsSection.TileManager => (L("Manager de tile-uri", "Tile Manager"), L("Ordinea, conținutul și aspectul tile-urilor", "The order, content and look of the tiles")),
        ClockSettingsSection.LanguageManager => (L("Manager de limbi", "Language Manager"), L("Setați limba interfeței, a datei și a vremii", "Set the interface, date and weather language")),
        ClockSettingsSection.Buzzer => (L("Setări buzzer", "Buzzer Settings"), L("Volum și tonuri presetate", "Volume and preset tones")),
        ClockSettingsSection.Touch => (L("Acțiuni senzor tactil", "Touch Sensor Actions"), L("Acțiuni tap și dublu tap", "Tap and Double Tap Actions")),
        ClockSettingsSection.Integrations => (L("Integrări", "Integrations"), L("Ce trimite Octoglow Connect ceasului de pe acest calculator. Se salvează în aplicație.", "What Octoglow Connect sends the clock from this computer. Saved in the app.")),
        ClockSettingsSection.MediaPlayer => ("Media Player", L("Ce aplicații sunt tratate ca surse video sau muzică pentru tile-ul Redare curentă.", "Which apps are treated as video or music sources for the Now Playing tile.")),
        ClockSettingsSection.ObsStudio => ("OBS Studio", L("Afișează starea înregistrării sau a transmisiei live din OBS Studio.",
            "Shows OBS Studio's recording or live stream status.")),
        ClockSettingsSection.QBittorrent => ("qBittorrent", L("Afișează progresul unui torent activ din qBittorrent.",
            "Shows the progress of an active qBittorrent download.")),
        ClockSettingsSection.MsiAfterburner => ("MSI Afterburner", L("Afișează FPS-ul sau temperatura CPU ori GPU.",
            "Shows FPS or the CPU or GPU temperature.")),
        ClockSettingsSection.Ets2 => ("Euro Truck Simulator 2", PriorityDescription("ets2")),
        ClockSettingsSection.BeamNg => ("BeamNG.drive", L("Afișează un vitezometru cu viteza vehiculului pe matrice.",
            "Shows a speedometer with the vehicle's speed on the matrix.")),
        ClockSettingsSection.AssettoCorsa => ("Assetto Corsa", L("Afișează un vitezometru cu viteza mașinii pe matrice.",
            "Shows a speedometer with the car's speed on the matrix.")),
        ClockSettingsSection.Nfsu2 => ("NFS Underground 2", L("Afișează un vitezometru cu viteza mașinii pe matrice.",
            "Shows a speedometer with the car's speed on the matrix.")),
        ClockSettingsSection.NfsMw => ("NFS Most Wanted", L("Afișează un vitezometru cu viteza mașinii pe matrice.",
            "Shows a speedometer with the car's speed on the matrix.")),
        ClockSettingsSection.NfsCarbon => ("NFS Carbon", L("Afișează un vitezometru cu viteza mașinii pe matrice.",
            "Shows a speedometer with the car's speed on the matrix.")),
        ClockSettingsSection.Voicemeeter => ("Voicemeeter", L("Afișează pe matrice animația audio a busului Voicemeeter ales.",
            "Shows an audio animation of the selected Voicemeeter bus on the matrix.")),
        ClockSettingsSection.ObsGuide => (L("Ghid OBS Studio", "OBS Studio guide"),
            L("Ce se face o singură dată în OBS Studio ca Octoglow Connect să-i poată citi starea.",
              "What to do once in OBS Studio so Octoglow Connect can read its state.")),
        ClockSettingsSection.Ets2Guide => (L("Ghid Euro Truck Simulator 2", "Euro Truck Simulator 2 guide"),
            L("Viteza camionului vine dintr-un plugin de telemetrie pus în folderul jocului.",
              "The truck's speed comes from a telemetry plugin put in the game's folder.")),
        ClockSettingsSection.BeamNgGuide => (L("Ghid BeamNG.drive", "BeamNG.drive guide"),
            L("BeamNG.drive trimite viteza prin OutGauge, care se pornește din opțiunile jocului.",
              "BeamNG.drive sends the speed over OutGauge, which is turned on in the game's options.")),
        ClockSettingsSection.BrowserDownloadsGuide => (L("Ghid Browser Downloads", "Browser Downloads guide"),
            L("O extensie pentru Chrome, Edge, Brave sau Opera trimite progresul descărcărilor către Octoglow Connect.",
              "An extension for Chrome, Edge, Brave or Opera sends download progress to Octoglow Connect.")),
        ClockSettingsSection.QBittorrentGuide => (L("Ghid qBittorrent", "qBittorrent guide"),
            L("Ce se face o singură dată în qBittorrent ca Octoglow Connect să-i poată citi descărcările.",
              "What to do once in qBittorrent so Octoglow Connect can read its downloads.")),
        ClockSettingsSection.MsiAfterburnerGuide => (L("Ghid MSI Afterburner", "MSI Afterburner guide"),
            L("Ce se face o singură dată în MSI Afterburner ca Octoglow Connect să-i poată citi valorile.",
              "What to do once in MSI Afterburner so Octoglow Connect can read its values.")),
        ClockSettingsSection.Brightness => (L("Luminozitate", "Brightness"), L("Cât de puternic luminează matricea", "How bright the matrix glows")),
        ClockSettingsSection.Wifi => (L("WiFi", "WiFi"), L("Scanați rețelele și schimbați rețeaua ceasului", "Scan for networks and change the clock's network")),
        ClockSettingsSection.AccessPoint => (L("Access Point", "Access Point"), L("Rețeaua proprie a ceasului, pentru configurare fără router", "The clock's own network, for setting it up without a router")),
        ClockSettingsSection.GeneralManagement => (L("Gestionare generală", "General Management"), L("Hardware, utilizator și backup", "Hardware, user and backup")),
        ClockSettingsSection.Preferences => (L("Cont și preferințe", "Account & Preferences"), L("Datele de acces, pornirea și interfața", "Login info, startup and interface")),
        ClockSettingsSection.Power => (L("Alimentare și somn", "Power & Sleep"), L("Opțiuni de alimentare și somn automat", "Power options and automatic sleep")),
        ClockSettingsSection.Hardware => (L("Monitor hardware", "Hardware Monitor"), L("CPU, memorie și spațiu de stocare", "CPU, memory and storage space")),
        ClockSettingsSection.Backup => (L("Backup și restaurare", "Backup and Restore"), L("Exportați, încărcați sau resetați setările", "Export, upload or reset the settings")),
        ClockSettingsSection.Account => (L("Schimbați datele de autentificare", "Change User Account Login Info"), L("Utilizatorul și parola interfeței", "Username and password for this interface")),
        ClockSettingsSection.Credentials => (AppStrings.Get("clock.credentials"), AppStrings.Get("clock.credentialsDescription")),
        ClockSettingsSection.HelpManager => (L("Manager de ajutor", "Help Manager"), L("Ajutor pentru setări și conectare", "Help with settings and connecting")),
        _ => (L("Despre", "About"), L("Vedeți versiunea și verificați actualizările", "See the version and check for updates")),
    };

    private static string SectionGlyph(ClockSettingsSection section) => section switch
    {
        ClockSettingsSection.TileManager => "\uECA5",
        ClockSettingsSection.Buzzer => "\uE767",
        ClockSettingsSection.LanguageManager => "\uE774",
        ClockSettingsSection.Touch => "\uE815",
        ClockSettingsSection.Integrations => "\uEA86",
        ClockSettingsSection.MediaPlayer => PriorityGlyph("nowplaying"),
        ClockSettingsSection.ObsStudio => PriorityGlyph("obs"),
        ClockSettingsSection.QBittorrent => PriorityGlyph("qbittorrent"),
        ClockSettingsSection.MsiAfterburner => TileGlyph(14)!,
        ClockSettingsSection.Ets2 => PriorityGlyph("ets2"),
        ClockSettingsSection.BeamNg => PriorityGlyph("beamng"),
        ClockSettingsSection.AssettoCorsa => PriorityGlyph("assetto"),
        ClockSettingsSection.Nfsu2 => PriorityGlyph("nfsu2"),
        ClockSettingsSection.NfsMw => PriorityGlyph("nfsmw"),
        ClockSettingsSection.NfsCarbon => PriorityGlyph("nfsc"),
        ClockSettingsSection.Voicemeeter => PriorityGlyph("voicemeeter"),
        ClockSettingsSection.ObsGuide => PriorityGlyph("obs"),
        ClockSettingsSection.Ets2Guide => PriorityGlyph("ets2"),
        ClockSettingsSection.BeamNgGuide => PriorityGlyph("beamng"),
        ClockSettingsSection.BrowserDownloadsGuide => PriorityGlyph("browserdownloads"),
        ClockSettingsSection.QBittorrentGuide => PriorityGlyph("qbittorrent"),
        ClockSettingsSection.MsiAfterburnerGuide => TileGlyph(14)!,
        ClockSettingsSection.Brightness => "\uE706",
        ClockSettingsSection.Wifi => "\uE701",
        ClockSettingsSection.AccessPoint => "\uEC05",
        ClockSettingsSection.GeneralManagement => "\uE713",
        ClockSettingsSection.Preferences => "\uE77B",
        ClockSettingsSection.Power => "\uE7E8",
        ClockSettingsSection.Hardware => "\uE950",
        ClockSettingsSection.Backup => "\uE8F7",
        ClockSettingsSection.Account => "\uE77B",
        ClockSettingsSection.Credentials => "\uE72E",
        ClockSettingsSection.HelpManager => "\uE897",
        ClockSettingsSection.About => "\uE946",
        _ => "\uE9E9",
    };

    // Sections that need nothing read from the clock to be shown - among them
    // the integrations' pages, whose settings are all the app's, and the guides.
    private static bool IsLocalSection(ClockSettingsSection section) =>
        section is ClockSettingsSection.Configuration
            or ClockSettingsSection.Credentials or ClockSettingsSection.GeneralManagement
            or ClockSettingsSection.HelpManager
            or ClockSettingsSection.MediaPlayer or ClockSettingsSection.ObsStudio or ClockSettingsSection.QBittorrent
            or ClockSettingsSection.MsiAfterburner
            or ClockSettingsSection.Ets2 or ClockSettingsSection.BeamNg or ClockSettingsSection.AssettoCorsa
            or ClockSettingsSection.Nfsu2 or ClockSettingsSection.NfsMw or ClockSettingsSection.NfsCarbon
            or ClockSettingsSection.Voicemeeter || IsGuideSection(section);

    // ------------------------------------------------------------------ configuration

    // Grouped and ordered as the clock's own web interface has it, so the two
    // read the same way round.
    private void BuildConfiguration()
    {
        AddSection(L("Conexiune", "Connection"));
        AddPanelCard(ClockSettingsSection.Wifi);
        AddPanelCard(ClockSettingsSection.AccessPoint);

        AddSection(L("Afișare", "Display"));
        AddPanelCard(ClockSettingsSection.TileManager);
        AddPanelCard(ClockSettingsSection.LanguageManager);
        AddPanelCard(ClockSettingsSection.Brightness);

        AddSection(L("Periferice", "Peripherals"));
        AddPanelCard(ClockSettingsSection.Buzzer);
        AddPanelCard(ClockSettingsSection.Touch);

        AddSection(L("Sistem", "System"));
        AddPanelCard(ClockSettingsSection.GeneralManagement);
        AddPanelCard(ClockSettingsSection.About);
    }

    // The web interface's main list calls these two by a shorter name than the
    // screens they open.
    private static string CardTitle(ClockSettingsSection section, string title) => section switch
    {
        ClockSettingsSection.AccessPoint => "AP",
        ClockSettingsSection.Buzzer => "Buzzer",
        ClockSettingsSection.Touch => L("Senzor tactil", "Touch Sensor"),
        _ => title,
    };

    private void AddPanelCard(ClockSettingsSection section, string? description = null)
    {
        var (title, header) = SectionHeader(section);
        title = CardTitle(section, title);
        Add(NavCard(SectionGlyph(section), title, description ?? header, () => OpenConfigurationSection(section)));
    }

    private void OpenConfigurationSection(ClockSettingsSection section)
    {
        if (!ConfigStore.InlineClockConfigurationPages || ShiftOpensPanel())
        {
            OpenPanel(section);
            return;
        }

        var (title, description) = SectionHeader(section);
        if (IsLocalSection(section))
            OpenLocalSubPage(title, description, () => BuildSectionContent(section, null), SectionGlyph(section));
        else
            OpenSubPage(title, description, state => BuildSectionContent(section, state), SectionGlyph(section));
    }

    // A panel cannot open over a panel, so what a panel leads to opens as a page
    // inside it - which is also how the web interface goes one screen deeper.
    private void AddSubPageCard(ClockSettingsSection section, Func<ClockDeviceState, Action?> build, string? description = null)
    {
        var (title, header) = SectionHeader(section);
        var text = description ?? header;
        Add(NavCard(SectionGlyph(section), title, text, () =>
        {
            if (ShiftOpensPanel()) OpenPanel(section);
            else OpenSubPage(title, text, build);
        }));
    }

    // Shift+click still opens a section in a panel while configurations open in
    // place - the way it opens with that setting off - unless one is open
    // already, since a panel cannot open over a panel.
    private bool ShiftOpensPanel() =>
        ConfigStore.InlineClockConfigurationPages && _panel is null &&
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    // ------------------------------------------------------------------ panel

    // Shared with Macro so header controls, surfaces and hover states cannot drift.
    internal const string PanelXaml =
        "<Grid " + XamlNs + " xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
        "Background='{ThemeResource SmokeFillColorDefaultBrush}' TabFocusNavigation='Cycle'>" +
        "<Border x:Name='Card' HorizontalAlignment='Center' VerticalAlignment='Center' CornerRadius='8' BorderThickness='1' " +
        "Background='{ThemeResource SolidBackgroundFillColorBaseBrush}' BorderBrush='{ThemeResource SurfaceStrokeColorFlyoutBrush}'>" +
        "<Grid>" +
        "<Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>" +
        "<Grid x:Name='Header' Padding='24,20,20,18' ColumnSpacing='12'>" +
        "<Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/>" +
        "<ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>" +
        "<Button x:Name='Back' Visibility='Collapsed' Padding='10,8' VerticalAlignment='Center' Background='Transparent' BorderBrush='Transparent'>" +
        "<FontIcon Glyph='&#xE72B;' FontSize='16'/></Button>" +
        "<FontIcon x:Name='Icon' Grid.Column='1' FontSize='24' Margin='0,0,4,0' VerticalAlignment='Center'/>" +
        "<StackPanel Grid.Column='2' Spacing='2' VerticalAlignment='Center'>" +
        "<TextBlock x:Name='Title' TextTrimming='CharacterEllipsis'/>" +
        "<TextBlock x:Name='Description' Opacity='0.72' TextWrapping='Wrap'/>" +
        "</StackPanel>" +
        "<Button x:Name='Refresh' Grid.Column='4' Padding='10,8' VerticalAlignment='Center' Background='Transparent' BorderBrush='Transparent'>" +
        "<FontIcon Glyph='&#xE72C;' FontSize='16'/></Button>" +
        "<Button x:Name='Close' Grid.Column='5' Padding='10,8' VerticalAlignment='Center' Background='Transparent' BorderBrush='Transparent'>" +
        "<FontIcon Glyph='&#xE711;' FontSize='16'/></Button>" +
        "</Grid>" +
        "<Rectangle Grid.Row='1' Height='1' Fill='{ThemeResource DividerStrokeColorDefaultBrush}'/>" +
        "<InfoBar x:Name='Status' Grid.Row='2' IsOpen='False' IsClosable='True' Margin='24,12,24,0'/>" +
        "<ScrollViewer x:Name='Scroller' Grid.Row='3'>" +
        "<StackPanel x:Name='Content' Spacing='4' Padding='24,8,24,28'/>" +
        "</ScrollViewer>" +
        "<Grid x:Name='Loading' Grid.Row='3' Background='Transparent' MinHeight='180' Visibility='Collapsed'>" +
        "<ProgressRing IsActive='True' Width='34' Height='34'/>" +
        "</Grid>" +
        "</Grid>" +
        "</Border>" +
        "</Grid>";

    internal static readonly TimeSpan PanelAnimation = TimeSpan.FromMilliseconds(167);

    // Set when the panel belongs to another page, such as the clock page's About
    // button: this page then never joins the tree, and that one lends it its
    // window and its theme.
    private FrameworkElement? _panelOwner;

    private FrameworkElement ThemeSource => _panelOwner ?? this;

    internal static void ShowPanel(ClockSettingsSection section, DeviceDescriptor? device, FrameworkElement owner)
    {
        if (owner.XamlRoot is null) return;
        var page = new ClockNativeSettingsPage { _device = device, _panelOwner = owner };
        page.OpenPanel(section);
    }

    private void OpenPanel(ClockSettingsSection section)
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (_panel is not null || root is null) return;
        DismissTileDialog(PageSurface);
        PauseTileLineReads();
        var overlay = Themed<Grid>(PanelXaml);
        overlay.Language = UiLanguage;
        T Part<T>(string name) where T : class => (T)overlay.FindName(name);

        var card = Part<Border>("Card");
        card.Shadow = new ThemeShadow();
        card.Translation = new Vector3(0, 0, 32);
        var title = Part<TextBlock>("Title");
        title.Style = Resource<Style>("SubtitleTextBlockStyle");
        Part<FontIcon>("Icon").Glyph = SectionGlyph(section);
        var save = NewSaveIndicator();
        Grid.SetColumn(save.Panel, 3);
        Part<Grid>("Header").Children.Add(save.Panel);

        var panel = new SettingsSurface
        {
            Section = section,
            Content = Part<StackPanel>("Content"),
            Scroller = Part<ScrollViewer>("Scroller"),
            Status = Part<InfoBar>("Status"),
            Loading = Part<Grid>("Loading"),
            Refresh = Part<Button>("Refresh"),
            Back = Part<Button>("Back"),
            Title = title,
            Description = Part<TextBlock>("Description"),
            Popup = new Popup { XamlRoot = root, Child = overlay },
            Close = Part<Button>("Close"),
            SaveIndicator = save,
        };
        panel.Back.Click += (_, _) => CloseSubPage(panel);
        panel.Refresh.Click += (_, _) => Reload(panel);
        panel.Close!.Click += (_, _) => ClosePanel();
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            // Escape goes back a page first, the way the back button does.
            if (panel.SubPage is not null) CloseSubPage(panel);
            else ClosePanel();
        };

        // The long panels keep one height, so the card doesn't jump as their settings load.
        var tall = section is ClockSettingsSection.TileManager or ClockSettingsSection.Buzzer
            or ClockSettingsSection.Wifi or ClockSettingsSection.Hardware or ClockSettingsSection.About;
        void Fit()
        {
            overlay.Width = root.Size.Width;
            overlay.Height = root.Size.Height;
            card.Width = Math.Max(320, Math.Min(880, root.Size.Width - 64));
            var height = Math.Max(260, root.Size.Height - 64);
            card.MaxHeight = height;
            card.Height = tall ? Math.Min(height, 920) : double.NaN;
        }
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args) => Fit();
        // The popup sits outside the page, so it takes the app's theme from it.
        void ThemeChanged(FrameworkElement sender, object args) => overlay.RequestedTheme = ThemeSource.ActualTheme;
        Fit();
        overlay.RequestedTheme = ThemeSource.ActualTheme;
        root.Changed += Resized;
        ThemeSource.ActualThemeChanged += ThemeChanged;
        var opener = FocusManager.GetFocusedElement(root) as Control;
        panel.Detach = () =>
        {
            root.Changed -= Resized;
            ThemeSource.ActualThemeChanged -= ThemeChanged;
            opener?.Focus(FocusState.Programmatic);
        };

        // Fades in and grows slightly into place, as a dialog opens.
        overlay.Opacity = AnimationsEnabled ? 0 : 1;
        overlay.OpacityTransition = AnimationsEnabled ? new ScalarTransition { Duration = PanelAnimation } : null;
        card.Scale = AnimationsEnabled ? new Vector3(0.96f, 0.96f, 1) : Vector3.One;
        card.ScaleTransition = AnimationsEnabled ? new Vector3Transition { Duration = PanelAnimation } : null;
        card.SizeChanged += (_, _) => card.CenterPoint = new Vector3((float)card.ActualWidth / 2, (float)card.ActualHeight / 2, 0);

        _panel = panel;
        ApplyHeader(panel);
        panel.Popup.IsOpen = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_panel, panel) || !panel.Popup.IsOpen) return;
            overlay.Opacity = 1;
            card.Scale = Vector3.One;
            panel.Close.Focus(FocusState.Programmatic);
        });
        _ = LoadSurfaceAsync(panel);
    }

    private void ClosePanel(bool animate = true)
    {
        var panel = _panel;
        if (panel?.Popup is not { } popup) return;
        _tileDialogCancellation?.Cancel();
        _tileDialog?.Hide();
        _panel = null;
        panel.Cancellation?.Cancel();
        DiscardSubPage(panel);
        StopSection(panel);
        if (_dragList is not null) EndDrag();
        panel.Detach?.Invoke();

        if (!animate || !AnimationsEnabled || popup.Child is not Grid overlay)
        {
            popup.IsOpen = false;
            return;
        }
        overlay.IsHitTestVisible = false;
        overlay.Opacity = 0;
        if (overlay.FindName("Card") is Border card) card.Scale = new Vector3(0.96f, 0.96f, 1);
        _ = ClosePopupLaterAsync(popup);
    }

    private static async Task ClosePopupLaterAsync(Popup popup)
    {
        await Task.Delay(PanelAnimation);
        popup.IsOpen = false;
    }
}
