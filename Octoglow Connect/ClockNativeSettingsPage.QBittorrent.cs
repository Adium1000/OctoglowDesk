using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// qBittorrent: where this computer finds qBittorrent's WebUI and how it signs
// in (kept here, never on the clock), with the state of the link on top. The
// connection itself is the backend's (SenderBackend.QBittorrent); this page
// only reads what it last saw, so opening it never opens another.
public sealed partial class ClockNativeSettingsPage
{
    private Action? BuildQbitPage()
    {
        var config = ConfigStore.Load();
        var savedHost = config.QbitHost;
        var savedPort = config.QbitPort;
        var savedUsername = config.QbitUsername;
        var savedPassword = config.QbitPassword;
        var savedApiKey = config.QbitApiKey;

        // What the backend sees, kept current while the page is open.
        var state = ValueText(QbitShortText(QBittorrentStatus.Current));
        var stateCard = SettingsCard(PriorityGlyph("qbittorrent"), L("Stare", "Status"), QbitStateText(QBittorrentStatus.Current), state);
        var stateDescription = CardDescription(stateCard);
        Add(stateCard);

        var host = new TextBox { Text = savedHost, PlaceholderText = "127.0.0.1" };
        var hostField = SaveFieldCard("", L("Adresă", "Address"),
            L("Calculatorul pe care rulează qBittorrent; 127.0.0.1 dacă este acesta.", "The computer qBittorrent runs on; 127.0.0.1 if it is this one."),
            host,
            () => host.Text.Trim() != savedHost,
            () =>
            {
                var value = host.Text.Trim();
                if (value.Length == 0 || value.Contains(' ') || value.Contains("://") || Uri.CheckHostName(value) == UriHostNameType.Unknown)
                {
                    ShowError(L("Scrie adresa calculatorului cu qBittorrent, de exemplu 127.0.0.1.", "Enter the address of the computer running qBittorrent, for example 127.0.0.1."));
                    return Task.CompletedTask;
                }
                SaveAppConfig(saved => saved.QbitHost = value);
                savedHost = value;
                host.Text = value;
                return Task.CompletedTask;
            });
        host.TextChanged += (_, _) => hostField.Refresh();
        OnEnter(host, hostField.Submit);
        Add(hostField.Card);

        var port = new TextBox { Text = Str(savedPort), PlaceholderText = "8080" };
        var portField = SaveFieldCard("", "Port",
            L("Portul WebUI-ului, din Options › Web UI în qBittorrent; implicit 8080.", "The WebUI's port, from Options › Web UI in qBittorrent; 8080 by default."),
            port,
            () => port.Text.Trim() != Str(savedPort),
            () =>
            {
                if (!int.TryParse(port.Text.Trim(), NumberStyles.Integer, Inv, out var number) || number is < 1 or > 65535)
                {
                    ShowError(L("Portul este un număr între 1 și 65535.", "The port is a number from 1 to 65535."));
                    return Task.CompletedTask;
                }
                SaveAppConfig(saved => saved.QbitPort = number);
                savedPort = number;
                port.Text = Str(number);
                return Task.CompletedTask;
            });
        port.TextChanged += (_, _) => portField.Refresh();
        OnEnter(port, portField.Submit);
        Add(portField.Card);

        // One way to sign in at a time; only its fields are shown.
        var auth = ChoiceBox(
            [
                (L("Utilizator și parolă", "User name and password"), "password"),
                (L("Cheie API (qBittorrent 5.2+)", "API key (qBittorrent 5.2+)"), "apikey"),
            ],
            config.QbitAuth,
            240);
        Add(SettingsCard("", L("Autentificare", "Sign-in"),
            L("Cheia API merge din qBittorrent 5.2; utilizatorul și parola, cu orice versiune.",
              "The API key works from qBittorrent 5.2; the user name and password, with any version."), auth));

        var (keyView, key) = SecretField(savedApiKey, L("Cheie API qBittorrent", "qBittorrent API key"));
        var keyField = SaveFieldCard("", L("Cheie API", "API key"),
            L("Din Options › Web UI › API Key, în qBittorrent; începe cu qbt_. Rămâne criptată pe acest calculator și nu ajunge pe ceas.",
              "From Options › Web UI › API Key, in qBittorrent; it starts with qbt_. It stays encrypted on this computer and never reaches the clock."),
            keyView,
            () => key.Password.Trim() != savedApiKey,
            () =>
            {
                var secret = key.Password.Trim();
                SaveAppConfig(saved => saved.QbitApiKey = secret);
                savedApiKey = secret;
                return Task.CompletedTask;
            });
        key.PasswordChanged += (_, _) => keyField.Refresh();
        OnEnter(keyView, keyField.Submit);
        Add(keyField.Card);

        var username = new TextBox { Text = savedUsername, PlaceholderText = L("Utilizator WebUI", "WebUI user name") };
        var usernameField = SaveFieldCard("", L("Utilizator", "User"),
            L("Cel de la Authentication, în Options › Web UI. Gol dacă qBittorrent nu cere autentificare pentru acest calculator.",
              "The one under Authentication, in Options › Web UI. Empty if qBittorrent asks this computer for no sign-in."),
            username,
            () => username.Text.Trim() != savedUsername,
            () =>
            {
                var value = username.Text.Trim();
                SaveAppConfig(saved => saved.QbitUsername = value);
                savedUsername = value;
                username.Text = value;
                return Task.CompletedTask;
            });
        username.TextChanged += (_, _) => usernameField.Refresh();
        OnEnter(username, usernameField.Submit);
        Add(usernameField.Card);

        var (passwordView, password) = SecretField(savedPassword, L("Parolă WebUI", "WebUI password"));
        var passwordField = SaveFieldCard("", L("Parolă", "Password"),
            L("Rămâne criptată pe acest calculator și nu ajunge pe ceas.", "It stays encrypted on this computer and never reaches the clock."),
            passwordView,
            () => password.Password != savedPassword,
            () =>
            {
                var secret = password.Password;
                SaveAppConfig(saved => saved.QbitPassword = secret);
                savedPassword = secret;
                return Task.CompletedTask;
            });
        password.PasswordChanged += (_, _) => passwordField.Refresh();
        OnEnter(passwordView, passwordField.Submit);
        Add(passwordField.Card);

        void ShowAuthFields()
        {
            var apiKey = ChoiceValue(auth) == "apikey";
            SetExpanded(keyField.Card, apiKey);
            SetExpanded(usernameField.Card, !apiKey);
            SetExpanded(passwordField.Card, !apiKey);
        }
        auth.SelectionChanged += (_, _) =>
        {
            var value = ChoiceValue(auth);
            SaveAppConfig(saved => saved.QbitAuth = value);
            ShowAuthFields();
        };
        ShowAuthFields();

        var reconnect = ActionCard("", L("Reconectează", "Reconnect"),
            L("Închide legătura cu qBittorrent și o deschide din nou, cu datele de mai sus.",
              "Closes the link to qBittorrent and opens it again, with the details above."), "", null).Card;
        reconnect.Click += (_, _) =>
        {
            ConfigStore.RequestQbitReconnect();
            ShowStatus(Surface.Status, InfoBarSeverity.Informational, L("Se reconectează", "Reconnecting"),
                L("Octoglow Connect deschide din nou legătura cu qBittorrent.", "Octoglow Connect is opening the link to qBittorrent again."));
        };
        Add(reconnect);

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            var now = QBittorrentStatus.Current;
            state.Text = QbitShortText(now);
            if (stateDescription is not null) stateDescription.Text = QbitStateText(now);
        };
        timer.Start();
        return timer.Stop;
    }

    // The card's description line, to be rewritten as the state changes.
    private static TextBlock? CardDescription(Border card) =>
        card.Child is Grid { Children.Count: > 1 } header && header.Children[1] is Panel { Children.Count: > 1 } title
            ? title.Children[1] as TextBlock
            : null;

    // A word or two, for the Integrations card and beside the status.
    private static string QbitShortText(QbitSnapshot snapshot) => snapshot.State switch
    {
        QbitConnectionState.Connected => snapshot.Downloading ? L($"Descarcă - {snapshot.Progress}%", $"Downloading - {snapshot.Progress}%") : L("Conectat", "Connected"),
        QbitConnectionState.Connecting => L("Se conectează", "Connecting"),
        QbitConnectionState.Unavailable => L("Indisponibil", "Unavailable"),
        QbitConnectionState.NotQbittorrent => L("Adresă greșită", "Wrong address"),
        QbitConnectionState.AuthFailed => L("Autentificare eșuată", "Sign-in failed"),
        QbitConnectionState.Banned => L("Blocat de qBittorrent", "Banned by qBittorrent"),
        QbitConnectionState.ClockUnsupported => L("Firmware vechi", "Old firmware"),
        _ => L("Inactiv", "Inactive"),
    };

    // What the state means and what to do about it.
    private static string QbitStateText(QbitSnapshot snapshot) => snapshot.State switch
    {
        QbitConnectionState.Connected => snapshot.Downloading
            ? snapshot.Downloads > 1
                ? L($"Conectat. Se descarcă {snapshot.Downloads} torente; ceasul îl arată pe cel mai rapid.",
                    $"Connected. {snapshot.Downloads} torrents are downloading; the clock shows the fastest.")
                : L("Conectat. Ceasul arată progresul descărcării.", "Connected. The clock shows the download's progress.")
            : L("Conectat. Nu se descarcă nimic acum; tile-ul apare când începe o descărcare.",
                "Connected. Nothing is downloading now; the tile appears when a download starts."),
        QbitConnectionState.Connecting => L("Se conectează la WebUI-ul qBittorrent.", "Connecting to the qBittorrent WebUI."),
        QbitConnectionState.Unavailable => L("qBittorrent nu răspunde: e închis, WebUI-ul lui e oprit sau adresa și portul nu sunt cele din Options › Web UI.",
            "qBittorrent isn't answering: it is closed, its WebUI is off, or the address and port aren't those in Options › Web UI."),
        QbitConnectionState.NotQbittorrent => L("La adresa și portul de mai jos răspunde altceva decât WebUI-ul qBittorrent.",
            "Something other than the qBittorrent WebUI answers at the address and port below."),
        QbitConnectionState.AuthFailed => L("qBittorrent a respins autentificarea. Verifică datele de mai jos, apoi apasă Reconectează.",
            "qBittorrent rejected the sign-in. Check the details below, then press Reconnect."),
        QbitConnectionState.Banned => L("qBittorrent a blocat temporar acest calculator după prea multe încercări greșite. Așteaptă sau repornește qBittorrent, apoi apasă Reconectează.",
            "qBittorrent has temporarily banned this computer after too many failed attempts. Wait or restart qBittorrent, then press Reconnect."),
        QbitConnectionState.ClockUnsupported => L("Firmware-ul ceasului nu are încă tile-ul qBittorrent. Actualizează firmware-ul ceasului.",
            "The clock's firmware has no qBittorrent tile yet. Update the clock's firmware."),
        _ => L("Oprit. Pornește tile-ul qBittorrent din Manager de tile-uri; până atunci qBittorrent nu este întrebat nimic.",
            "Off. Turn the qBittorrent tile on in the Tile Manager; until then qBittorrent isn't asked anything."),
    };
}
