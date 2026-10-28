using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using Windows.ApplicationModel.DataTransfer;

namespace OctoglowSender;

// The guides behind the ⓘ on the Integrations tab: what has to be done once,
// outside Octoglow Connect, before an integration can work - turning on OBS's
// WebSocket server, putting the telemetry plugin in Euro Truck Simulator 2,
// switching OutGauge on in BeamNG.drive, loading the browser extension. Each
// step says what to do, shows it on the app's own screen (Assets/Guides, drawn
// by the guides generator with what to click ringed and numbered), and where it
// can, takes the user there. A guide opens the way the configurations do.
public sealed partial class ClockNativeSettingsPage
{
    private sealed record GuideStep(string Title, string Text, string? Image = null, GuideLink[]? Links = null);

    private sealed record GuideLink(string Label, string Glyph, Action<Button> Run);

    // The integrations that need something done elsewhere first, and their guides.
    private static ClockSettingsSection? GuideSection(ClockSettingsSection integration) => integration switch
    {
        ClockSettingsSection.ObsStudio => ClockSettingsSection.ObsGuide,
        ClockSettingsSection.QBittorrent => ClockSettingsSection.QBittorrentGuide,
        ClockSettingsSection.MsiAfterburner => ClockSettingsSection.MsiAfterburnerGuide,
        ClockSettingsSection.Ets2 => ClockSettingsSection.Ets2Guide,
        ClockSettingsSection.BeamNg => ClockSettingsSection.BeamNgGuide,
        _ => null,
    };

    private static bool IsGuideSection(ClockSettingsSection section) =>
        section is ClockSettingsSection.ObsGuide or ClockSettingsSection.Ets2Guide
            or ClockSettingsSection.BeamNgGuide or ClockSettingsSection.BrowserDownloadsGuide
            or ClockSettingsSection.QBittorrentGuide or ClockSettingsSection.MsiAfterburnerGuide;

    private Button GuideButton(ClockSettingsSection guide)
    {
        var button = TransparentIconButton("\uE946", 16);
        var label = L("Ghid de configurare", "Setup guide");
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => OpenConfigurationSection(guide);
        return button;
    }

    private void BuildGuide(ClockSettingsSection guide)
    {
        var steps = GuideSteps(guide);
        for (var i = 0; i < steps.Count; i++) Add(GuideStepCard(i + 1, steps[i]));
    }

    private List<GuideStep> GuideSteps(ClockSettingsSection guide) => guide switch
    {
        ClockSettingsSection.ObsGuide =>
        [
            new(L("Deschide setările WebSocket din OBS", "Open OBS's WebSocket settings"),
                L("În OBS Studio, din meniul Tools (Unelte), alege WebSocket Server Settings (Setări pentru serverul WebSocket). Serverul vine cu OBS 28 și mai nou.",
                  "In OBS Studio, open the Tools menu and choose WebSocket Server Settings. The server comes with OBS 28 and newer."),
                "obs-tools.png"),
            new(L("Pornește serverul", "Turn the server on"),
                L("Bifează Enable WebSocket server (1). Lasă portul 4455 (2) și bifa Enable Authentication, apoi apasă Apply. Show Connect Info (3) arată parola.",
                  "Tick Enable WebSocket server (1). Keep port 4455 (2) and Enable Authentication ticked, then press Apply. Show Connect Info (3) shows the password."),
                "obs-websocket.png"),
            new(L("Copiază parola", "Copy the password"),
                L("În fereastra Connect Info, apasă Copy lângă Server Password (Parola serverului).",
                  "In the Connect Info window, press Copy next to Server Password."),
                "obs-connect-info.png"),
            new(L("Pune parola în Octoglow Connect", "Paste it into Octoglow Connect"),
                L("În setările OBS Studio de aici, lipește parola la Parolă și apasă Salvează. Dacă OBS rulează pe alt calculator, scrie și adresa lui la Adresă. Cardul OBS Studio din Integrări arată Activ când legătura merge.",
                  "In the OBS Studio settings here, paste the password under Password and press Save. If OBS runs on another computer, enter its address under Address too. The OBS Studio card in Integrations shows Active once the link works."),
                null,
                [SectionLink(ClockSettingsSection.ObsStudio, L("Deschide setările OBS Studio", "Open the OBS Studio settings"))]),
        ],
        ClockSettingsSection.Ets2Guide => Ets2GuideSteps(),
        ClockSettingsSection.QBittorrentGuide =>
        [
            new(L("Deschide Options", "Open Options"),
                L("În qBittorrent, din meniul Tools (Unelte), alege Options... (Opțiuni) (1).",
                  "In qBittorrent, open the Tools menu and choose Options... (1)."),
                "qbit-tools.png"),
            new(L("Pornește WebUI-ul", "Turn the WebUI on"),
                L("În lista din stânga alege WebUI (1) și bifează Web User Interface (Remote control) (2). La IP address lasă * (sau 127.0.0.1, dacă qBittorrent rulează pe acest calculator), iar la Port 8080 sau alt port liber (3) - același ca în setările qBittorrent de aici. Lasă Use HTTPS instead of HTTP debifat: Octoglow Connect se leagă prin HTTP.",
                  "Choose WebUI in the list on the left (1) and tick Web User Interface (Remote control) (2). Keep * under IP address (or 127.0.0.1, if qBittorrent runs on this computer), and 8080 or another free port under Port (3) - the same as in the qBittorrent settings here. Leave Use HTTPS instead of HTTP unticked: Octoglow Connect connects over HTTP."),
                "qbit-webui.png"),
            new(L("Alege cum se autentifică Octoglow Connect", "Choose how Octoglow Connect signs in"),
                L("Mai jos, la Authentication. qBittorrent 5.2 sau mai nou: la API Key apasă butonul care generează cheia (1), copiaz-o (începe cu qbt_) și pune-o aici la Cheie API. Orice versiune: la User pune un Username și un Password (2) și scrie-le la fel aici. Dacă ai bifat Bypass authentication for clients on localhost, lasă utilizatorul gol aici.",
                  "Further down, under Authentication. qBittorrent 5.2 or newer: under API Key press the button that generates the key (1), copy it (it starts with qbt_) and put it here under API key. Any version: set a Username and Password under User (2) and enter the same here. If you ticked Bypass authentication for clients on localhost, leave the user name empty here."),
                "qbit-auth.png"),
            new(L("Apasă Apply și verifică", "Press Apply and check"),
                L("Apasă Apply sau OK în qBittorrent. Cardul qBittorrent din Integrări arată Conectat când legătura merge; ceasul afișează progresul cât timp se descarcă ceva.",
                  "Press Apply or OK in qBittorrent. The qBittorrent card in Integrations shows Connected once the link works; the clock shows the progress while something downloads."),
                null,
                [SectionLink(ClockSettingsSection.QBittorrent, L("Deschide setările qBittorrent", "Open the qBittorrent settings"))]),
        ],
        ClockSettingsSection.MsiAfterburnerGuide =>
        [
            new(L("Pornește MSI Afterburner", "Start MSI Afterburner"),
                L("Deschide MSI Afterburner și lasă-l să ruleze; poate sta minimizat. Ca să pornească singur, deschide Settings (roata dințată) și, pe fila General, bifează Start with Windows și Start minimized (1), apoi apasă OK (2).",
                  "Open MSI Afterburner and leave it running; it can stay minimised. To have it start by itself, open Settings (the gear) and, on the General tab, tick Start with Windows and Start minimized (1), then press OK (2)."),
                "msi-general.png"),
            new(L("Bifează temperaturile", "Tick the temperatures"),
                L("Tot în Settings, pe fila Monitoring (1), în lista Active hardware monitoring graphs, bifează GPU temperature (2) - la fiecare placă video pe care o vrei - și, mai jos în listă, CPU temperature (3), apoi apasă OK (4). Doar ce este bifat acolo ajunge la Octoglow Connect.",
                  "Also in Settings, on the Monitoring tab (1), in the Active hardware monitoring graphs list, tick GPU temperature (2) - for each graphics card you want - and, further down the list, CPU temperature (3), then press OK (4). Only what is ticked there reaches Octoglow Connect."),
                "msi-monitoring.png"),
            new(L("Pentru FPS, lasă RivaTuner Statistics Server pornit", "For FPS, keep RivaTuner Statistics Server running"),
                L("RivaTuner Statistics Server se instalează odată cu MSI Afterburner și îl pornește Afterburner. FPS-ul este al jocului din prim-plan, măsurat de el; cât timp nu rulează un joc 3D, tile-ul nu apare când afișează FPS.",
                  "RivaTuner Statistics Server is installed with MSI Afterburner, which starts it. The FPS is that of the game in the foreground, as it measures it; while no 3D game runs, the tile isn't shown when it displays FPS."),
                null),
            new(L("Pentru FPS peste celelalte tile-uri, adaugă FPS în joc", "For FPS over the other tiles, add In-game FPS"),
                L("În Manager de tile-uri, la Tile-uri prioritare, adaugă FPS în joc. Apare doar cât timp joci: jocul este fereastra din față și Windows îl știe drept joc (lista din Game Bar) sau rulează pe tot ecranul. Un program 3D obișnuit, ca Bambu Studio sau un browser, nu îl pornește.",
                  "In the Tile Manager, under Priority Tiles, add In-game FPS. It shows only while you play: the game is the window in front and Windows knows it as a game (Game Bar's list) or it runs full screen. An ordinary 3D program, such as Bambu Studio or a browser, doesn't bring it up."),
                null),
            new(L("Verifică", "Check"),
                L("Pagina MSI Afterburner din Integrări arată ce valori se citesc acum și, dacă lipsește una, de ce. Ce afișează ceasul - FPS, temperatura CPU sau GPU - se alege din setările tile-ului, în Manager de tile-uri.",
                  "The MSI Afterburner page in Integrations shows which values are read now and, when one is missing, why. What the clock shows - FPS, the CPU or the GPU temperature - is chosen in the tile's settings, in the Tile Manager."),
                null,
                [SectionLink(ClockSettingsSection.MsiAfterburner, L("Deschide pagina MSI Afterburner", "Open the MSI Afterburner page"))]),
        ],
        ClockSettingsSection.BeamNgGuide =>
        [
            new(L("Deschide Options › Advanced › Protocols", "Open Options › Advanced › Protocols"),
                L("În BeamNG.drive, din meniul principal sau cu Esc în timpul jocului, deschide Options. În lista din stânga, sub Advanced, alege Protocols (1).",
                  "In BeamNG.drive, open Options from the main menu, or with Esc while playing. In the list on the left, under Advanced, choose Protocols (1)."),
                "beamng-outgauge.png"),
            new(L("Pornește OutGauge", "Turn OutGauge on"),
                L("Activează OutGauge UDP protocol (2). La Address (3) lasă 127.0.0.1 și la Port 4444 - aceleași ca în setările BeamNG.drive de aici. Dacă jocul rulează pe alt calculator, la Address pune adresa acestui calculator din rețea.",
                  "Turn on OutGauge UDP protocol (2). Keep 127.0.0.1 under Address (3) and 4444 under Port - the same as in the BeamNG.drive settings here. If the game runs on another computer, put this computer's network address under Address."),
                null),
            new(L("Reîncarcă vehiculul", "Reload the vehicle"),
                L("Jocul aplică setările OutGauge abia după ce vehiculul este reîncărcat: apasă Ctrl+R în joc.",
                  "The game only applies the OutGauge settings once the vehicle is reloaded: press Ctrl+R in the game."),
                null),
            new(L("Condu", "Drive"),
                L("Cardul BeamNG.drive din Integrări arată Activ cât timp sosesc date, iar ceasul afișează viteza cât timp vehiculul se mișcă.",
                  "The BeamNG.drive card in Integrations shows Active while data arrives, and the clock shows the speed while the vehicle moves."),
                null,
                [SectionLink(ClockSettingsSection.BeamNg, L("Deschide setările BeamNG.drive", "Open the BeamNG.drive settings"))]),
        ],
        _ =>
        [
            new(L("Deschide pagina de extensii", "Open the extensions page"),
                L("În browser, scrie chrome://extensions în bara de adrese - edge://extensions în Edge, brave://extensions în Brave, opera://extensions în Opera.",
                  "In the browser, type chrome://extensions in the address bar - edge://extensions in Edge, brave://extensions in Brave, opera://extensions in Opera."),
                null,
                [CopyLink(L("Copiază adresa", "Copy the address"), "chrome://extensions")]),
            new(L("Pornește Developer mode", "Turn on Developer mode"),
                L("Activează Developer mode (Mod dezvoltator) (1). În Chrome este sus, în dreapta; în Edge, în lista din stânga. Apare apoi butonul Load unpacked (2).",
                  "Turn on Developer mode (1). In Chrome it is at the top right; in Edge, in the list on the left. The Load unpacked button (2) then appears."),
                "browser-extensions.png"),
            new(L("Încarcă extensia", "Load the extension"),
                L("Apasă Load unpacked (Încarcă extensia neîmpachetată) și alege folderul BrowserExtension al aplicației.",
                  "Press Load unpacked and choose the app's BrowserExtension folder."),
                null,
                [
                    FolderLink(L("Deschide folderul", "Open the folder"), BrowserDownloadsBridge.ExtensionFolder),
                    CopyLink(L("Copiază calea", "Copy the path"), BrowserDownloadsBridge.ExtensionFolder),
                ]),
            new(L("Lasă Octoglow Connect pornit", "Keep Octoglow Connect running"),
                L("Extensia trimite doar numărul descărcărilor și progresul lor, numai către acest calculator. Cardul Browser Downloads din Integrări arată Activ când extensia este conectată.",
                  "The extension sends only the number of downloads and their progress, and only to this computer. The Browser Downloads card in Integrations shows Active once the extension is connected."),
                null),
        ],
    };

    private List<GuideStep> Ets2GuideSteps()
    {
        var game = FindEts2Folder();
        var plugins = game is null ? null : Path.Combine(game, "bin", "win_x64", "plugins");
        // The plugins folder isn't there until someone makes it; the one above it is.
        var open = plugins is null ? null : Directory.Exists(plugins) ? plugins : Path.GetDirectoryName(plugins);
        return
        [
            new(L("Descarcă pluginul de telemetrie", "Download the telemetry plugin"),
                L("Viteza vine din scs-sdk-plugin (RenCloud). Descarcă ultima versiune de pe GitHub și scoate din arhivă fișierul scs-telemetry.dll pentru Windows pe 64 de biți.",
                  "The speed comes from scs-sdk-plugin (RenCloud). Download the latest release from GitHub and take the 64-bit Windows scs-telemetry.dll out of the archive."),
                null,
                [UrlLink(L("Deschide pagina de descărcare", "Open the download page"), "https://github.com/RenCloud/scs-sdk-plugin/releases")]),
            new(L("Pune-l în folderul jocului", "Put it in the game's folder"),
                L("Copiază scs-telemetry.dll în Euro Truck Simulator 2\\bin\\win_x64\\plugins (1, 2). Dacă folderul plugins nu există, creează-l.",
                  "Copy scs-telemetry.dll into Euro Truck Simulator 2\\bin\\win_x64\\plugins (1, 2). If there is no plugins folder, create it."),
                "ets2-plugins.png",
                open is null ? null : [FolderLink(L("Deschide folderul jocului", "Open the game's folder"), open)]),
            new(L("Pornește jocul și apasă OK", "Start the game and press OK"),
                L("La fiecare pornire, jocul anunță că folosește funcții SDK avansate (Advanced SDK features). Apasă OK: mesajul înseamnă că pluginul a fost găsit.",
                  "Every time it starts, the game says it is using Advanced SDK features. Press OK: the message means the plugin was found."),
                "ets2-sdk.png"),
            new(L("Condu", "Drive"),
                L("Cât timp camionul merge, ceasul afișează viteza. Din setările Euro Truck Simulator 2 de aici alegi unitatea și dacă tile-ul se oprește când camionul stă pe loc.",
                  "While the truck moves, the clock shows its speed. The Euro Truck Simulator 2 settings here choose the unit and whether the tile stops when the truck stands still."),
                null,
                [SectionLink(ClockSettingsSection.Ets2, L("Deschide setările Euro Truck Simulator 2", "Open the Euro Truck Simulator 2 settings"))]),
        ];
    }

    // One step: its number, what to do, the picture of it and what can be done from here.
    private static Border GuideStepCard(int number, GuideStep step)
    {
        var card = CardBorder();
        card.Padding = new Thickness(16, 14, 16, 16);

        var layout = new Grid { ColumnSpacing = 14 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = Themed<Border>(
            $"<Border {XamlNs} Width='28' Height='28' CornerRadius='14' VerticalAlignment='Top' " +
            "Background='{ThemeResource AccentFillColorDefaultBrush}'>" +
            "<TextBlock HorizontalAlignment='Center' VerticalAlignment='Center' FontWeight='SemiBold' " +
            "Foreground='{ThemeResource TextOnAccentFillColorPrimaryBrush}'/></Border>");
        ((TextBlock)badge.Child).Text = number.ToString(Inv);
        layout.Children.Add(badge);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = step.Title,
            Style = Resource<Style>("BodyStrongTextBlockStyle"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });
        content.Children.Add(new TextBlock { Text = step.Text, TextWrapping = TextWrapping.Wrap, Opacity = 0.86 });
        if (step.Image is not null)
        {
            // Drawn at twice the size it is shown, so it stays sharp on a scaled display.
            var image = new Image
            {
                Source = new BitmapImage(new Uri("ms-appx:///Assets/Guides/" + step.Image)),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            };
            AutomationProperties.SetName(image, step.Title);
            var frame = Themed<Border>(
                $"<Border {XamlNs} CornerRadius='10' BorderThickness='1' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}'/>");
            frame.Child = image;
            frame.MaxWidth = 682;
            frame.HorizontalAlignment = HorizontalAlignment.Left;
            frame.Margin = new Thickness(0, 6, 0, 0);
            content.Children.Add(frame);
        }
        if (step.Links is { Length: > 0 } links)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var link in links)
            {
                var button = ActionButton(link.Label, link.Glyph);
                button.Click += (_, _) => link.Run(button);
                row.Children.Add(button);
            }
            content.Children.Add(row);
        }
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);
        card.Child = layout;
        return card;
    }

    // To the integration's own settings: in the page, a level deeper than the
    // guide, or in a panel of its own in place of the guide's.
    private GuideLink SectionLink(ClockSettingsSection section, string label) => new(label, "\uE713", _ =>
    {
        if (_panel is null)
        {
            OpenConfigurationSection(section);
            return;
        }
        ClosePanel();
        OpenPanel(section);
    });

    private GuideLink UrlLink(string label, string url) => new(label, "\uE8A7", _ => Launch(url));

    private GuideLink FolderLink(string label, string folder) => new(label, "\uE838", _ => Launch(folder));

    private GuideLink CopyLink(string label, string text) => new(label, "\uE8C8", button =>
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception error)
        {
            ShowError(error.Message);
            return;
        }
        // Says so on the button for a moment, where the eye already is.
        SetActionButton(button, L("Copiat", "Copied"), "\uE73E");
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => SetActionButton(button, label, "\uE8C8");
        timer.Start();
    });

    private void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    // Where Steam put Euro Truck Simulator 2, if it did: the game's uninstall
    // entry first, then every Steam library.
    private static string? FindEts2Folder()
    {
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var entry = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 227300");
                if (entry?.GetValue("InstallLocation") is string installed && Directory.Exists(installed)) return installed;
            }

            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steamKey?.GetValue("SteamPath") is not string steam) return null;
            var libraries = new List<string> { steam };
            var list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(list))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(list), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            return libraries
                .Select(library => Path.Combine(library, "steamapps", "common", "Euro Truck Simulator 2"))
                .FirstOrDefault(Directory.Exists);
        }
        catch
        {
            return null;
        }
    }
}
