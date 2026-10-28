using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// OBS Studio: where this computer finds OBS (kept here, never on the clock),
// on the integration's page, and the one thing the clock keeps about the tile,
// whether it shows the duration, in the tile's own settings. The connection
// itself is the backend's (SenderBackend.ObsStudio); these pages only read what
// it last saw, so opening them never opens another.
public sealed partial class ClockNativeSettingsPage
{
    // Address, port and password, each saved on its own the way text is saved
    // everywhere in the app; saving any of them reconnects.
    private Action? BuildObsPage()
    {
        var config = ConfigStore.Load();
        var savedHost = config.ObsHost;
        var savedPort = config.ObsPort;
        var savedPassword = config.ObsPassword;

        var host = new TextBox { Text = savedHost, PlaceholderText = "127.0.0.1" };
        var hostField = SaveFieldCard("\uE968", L("Adresă", "Address"),
            L("Calculatorul pe care rulează OBS; 127.0.0.1 dacă este acesta.", "The computer OBS runs on; 127.0.0.1 if it is this one."),
            host,
            () => host.Text.Trim() != savedHost,
            () =>
            {
                var value = host.Text.Trim();
                if (value.Length == 0 || value.Contains(' ') || value.Contains("://") || Uri.CheckHostName(value) == UriHostNameType.Unknown)
                {
                    ShowError(L("Scrie adresa calculatorului cu OBS, de exemplu 127.0.0.1.", "Enter the address of the computer running OBS, for example 127.0.0.1."));
                    return Task.CompletedTask;
                }
                SaveAppConfig(saved => saved.ObsHost = value);
                savedHost = value;
                host.Text = value;
                return Task.CompletedTask;
            });
        host.TextChanged += (_, _) => hostField.Refresh();
        OnEnter(host, hostField.Submit);
        Add(hostField.Card);

        var port = new TextBox { Text = Str(savedPort), PlaceholderText = "4455" };
        var portField = SaveFieldCard("\uE71B", "Port",
            L("Portul serverului WebSocket; implicit 4455.", "The WebSocket server's port; 4455 by default."),
            port,
            () => port.Text.Trim() != Str(savedPort),
            () =>
            {
                if (!int.TryParse(port.Text.Trim(), NumberStyles.Integer, Inv, out var number) || number is < 1 or > 65535)
                {
                    ShowError(L("Portul este un număr între 1 și 65535.", "The port is a number from 1 to 65535."));
                    return Task.CompletedTask;
                }
                SaveAppConfig(saved => saved.ObsPort = number);
                savedPort = number;
                port.Text = Str(number);
                return Task.CompletedTask;
            });
        port.TextChanged += (_, _) => portField.Refresh();
        OnEnter(port, portField.Submit);
        Add(portField.Card);

        var (passwordView, password) = SecretField(savedPassword, L("Parolă OBS WebSocket", "OBS WebSocket password"));
        var passwordField = SaveFieldCard("\uE72E", L("Parolă", "Password"),
            L("Din Show Connect Info, în OBS. Rămâne criptată pe acest calculator și nu ajunge pe ceas.",
              "From Show Connect Info, in OBS. It stays encrypted on this computer and never reaches the clock."),
            passwordView,
            () => password.Password != savedPassword,
            () =>
            {
                var secret = password.Password;
                SaveAppConfig(saved => saved.ObsPassword = secret);
                savedPassword = secret;
                return Task.CompletedTask;
            });
        password.PasswordChanged += (_, _) => passwordField.Refresh();
        OnEnter(passwordView, passwordField.Submit);
        Add(passwordField.Card);

        var reconnect = ActionCard("\uE72C", L("Reconectează", "Reconnect"),
            L("Închide legătura cu OBS Studio și o deschide din nou.", "Closes the link to OBS Studio and opens it again."), "", null).Card;
        reconnect.Click += (_, _) =>
        {
            ConfigStore.RequestObsReconnect();
            ShowStatus(Surface.Status, InfoBarSeverity.Informational, L("Se reconectează", "Reconnecting"),
                L("Octoglow Connect deschide din nou legătura cu OBS Studio.", "Octoglow Connect is opening the link to OBS Studio again."));
        };
        Add(reconnect);
        return null;
    }

    // In the tile's own settings, the part the clock keeps.
    private void BuildObsContent(ClockDeviceState state)
    {
        Add(ObsShowElapsedCard(state));
        var note = Secondary(L("Adresa, portul și parola OBS WebSocket se setează în Integrări.",
                               "The OBS WebSocket address, port and password are set in Integrations."));
        note.Margin = new Thickness(4, 8, 4, 0);
        Add(note);
    }

    private Border ObsShowElapsedCard(ClockDeviceState state)
    {
        var toggle = Toggle(state.Bool("obsShowElapsed", true));
        toggle.Toggled += (_, _) => QueueSave("obs-elapsed",
            token => Post(token, "/obssett", ("showElapsed", toggle.IsOn ? "1" : "0")), 0);
        return SettingsCard("", L("Afișează durata înregistrării/transmisiei", "Show recording/stream duration"),
            L("REC 5:42 în loc de REC. Setarea este păstrată pe ceas.", "REC 5:42 instead of REC. The setting is kept on the clock."), toggle);
    }

    private static string ObsActiveText() => ActiveText(ObsStudioStatus.Current.State == ObsConnectionState.Connected);
}
