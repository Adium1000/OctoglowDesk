using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
namespace OctoglowSender;
public static class AppLog
{
    private const int MaxEntries = 500;
    private static DispatcherQueue? _dispatcher;
    public static ObservableCollection<ActivityLogEntry> Entries { get; } = [];
    public static ObservableCollection<ActivityLogEntry> DeskClockEntries { get; } = [];
    public static void Initialize(DispatcherQueue dispatcher) => _dispatcher = dispatcher;
    public static void Write(string message)
    {
        if (IsIntegrationDiagnostic(message)) return;
        var entry = ActivityLogEntry.FromMessage(message);
        if (_dispatcher is not null) _dispatcher.TryEnqueue(() => Add(Entries, entry));
        else Add(Entries, entry);
    }

    public static void WriteDeskClock(string message)
    {
        // Integration discovery/retry chatter is diagnostic, not activity on the
        // clock link. Keep it available to ConnectionStatus, but out of both
        // visible logs; authentication and transport failures remain visible.
        if (IsIntegrationDiagnostic(message)) return;
        void AddToBoth()
        {
            Add(Entries, ActivityLogEntry.FromMessage(message));
            Add(DeskClockEntries, ActivityLogEntry.FromMessage(message));
        }

        if (_dispatcher is not null) _dispatcher.TryEnqueue(AddToBoth);
        else AddToBoth();
    }

    private static bool IsIntegrationDiagnostic(string message)
    {
        var end = message.IndexOf(']');
        if (end <= 1 || message[0] != '[') return false;
        var tag = message[1..end].Trim();
        return tag.Equals("OBS", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("OBS ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("VOICEMEETER", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("VOICEMEETER ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("BEAMNG", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("BEAMNG ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("ASSETTO", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("ASSETTO ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("NFSU2", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("NFSU2 ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("NFSMW", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("NFSMW ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("NFSC", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("NFSC ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("QBIT", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("AFTERBURNER", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("AFTERBURNER ", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("QBIT ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("ETS2", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("ETS2 ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("NOTIF", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("NOTIF ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("NP", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("NP ", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("BROWSER DOWNLOADS", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("BROWSER DOWNLOADS ", StringComparison.OrdinalIgnoreCase);
    }

    private static void Add(ObservableCollection<ActivityLogEntry> target, ActivityLogEntry entry)
    {
        // Dacă ultima intrare raportează exact aceeași activitate (același context/mesaj),
        // nu o mai afișăm din nou - doar extindem intervalul de timp afișat pentru ea.
        if (target.Count > 0)
        {
            var last = target[^1];
            if (last.RawMessage == entry.RawMessage)
            {
                last.LastTimestamp = entry.Timestamp;
                return;
            }
        }

        target.Add(entry);
        while (target.Count > MaxEntries) target.RemoveAt(0);
    }
}

public sealed class ActivityLogEntry : System.ComponentModel.INotifyPropertyChanged
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    private DateTime _lastTimestamp;
    public DateTime LastTimestamp
    {
        get => _lastTimestamp;
        set
        {
            if (_lastTimestamp == value) return;
            _lastTimestamp = value;
            OnPropertyChanged(nameof(Time));
        }
    }
    public string Time => LastTimestamp > Timestamp
        ? $"{Timestamp:HH:mm:ss} - {LastTimestamp:HH:mm:ss}"
        : Timestamp.ToString("HH:mm:ss");
    public string RawMessage { get; init; } = "";
    public string Glyph => ActivityLogFormatter.GetGlyph(RawMessage);
    public string Title => ActivityLogFormatter.GetTitle(RawMessage);
    public string Description => ActivityLogFormatter.GetDescription(RawMessage);
    public static ActivityLogEntry FromMessage(string message)
    {
        var now = DateTime.Now;
        return new ActivityLogEntry { Timestamp = now, LastTimestamp = now, RawMessage = message };
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}

// An entry reads like a settings card: what it is about, with that thing's own
// icon - the one it has in Integrations or the Tile Manager - over what
// happened. Both come from the tag the backend puts in front of each message,
// such as "[NOWPLAYING] Song" or "[NP WARN] ...".
public static class ActivityLogFormatter
{
    // Payload: the rest of the message is what the clock was sent - a song, a
    // notification - and is shown as it is, never translated.
    private sealed record Category(string Glyph, string Romanian, string English, bool Payload = false);

    private static Category CategoryOf(string message) => Tag(message) switch
    {
        "NOWPLAYING" => new("\uE8D6", "Now Playing", "Now Playing", true),
        "NP" => new("\uE8D6", "Now Playing", "Now Playing"),
        "NOTIFICATION" => new("\uE8BD", "Notificări", "Notifications", true),
        "NOTIF" => new("\uE8BD", "Notificări", "Notifications"),
        "ETS2SPEED" or "ETS2" => new("\uE804", "Euro Truck Simulator 2", "Euro Truck Simulator 2"),
        "OBS" => new("\uE714", "OBS Studio", "OBS Studio"),
        "BEAMNG" => new("\uE804", "BeamNG.drive", "BeamNG.drive"),
        "ASSETTO" => new("\uE804", "Assetto Corsa", "Assetto Corsa"),
        "NFSU2" => new("\uE804", "NFS Underground 2", "NFS Underground 2"),
        "NFSMW" => new("\uE804", "NFS Most Wanted", "NFS Most Wanted"),
        "NFSC" => new("\uE804", "NFS Carbon", "NFS Carbon"),
        "QBIT" => new("\uEBD3", "qBittorrent", "qBittorrent"),
        "AFTERBURNER" => new("\uE950", "MSI Afterburner", "MSI Afterburner"),
        "VOICEMEETER" => new("\uE767", "Voicemeeter", "Voicemeeter"),
        "BROWSER DOWNLOADS" => new("\uE896", "Browser Downloads", "Browser Downloads"),
        "AUTH" => new("\uE72E", "Autentificare", "Sign-in"),
        "SCREEN" => new("\uE121", "Afișaj în timp real", "Live display"),
        "CUSTOMIZE" => new("\uE774", "Interfață web", "Web interface"),
        // Plain [WARN] / [ERR] and untagged messages are about the link to the clock.
        _ => new("\uE71B", "Conexiune", "Connection"),
    };

    // "[NP WARN] ..." -> "NP": a warning or an error belongs to what it is about.
    private static string Tag(string message)
    {
        var text = message.TrimStart();
        var end = text.IndexOf(']');
        if (!text.StartsWith('[') || end < 1) return "";
        var tag = text[1..end].Trim().ToUpperInvariant();
        foreach (var suffix in new[] { " WARN", " ERR" })
            if (tag.EndsWith(suffix)) tag = tag[..^suffix.Length];
        return tag is "WARN" or "ERR" ? "" : tag;
    }

    public static string GetGlyph(string message) => CategoryOf(message).Glyph;

    public static string GetTitle(string message)
    {
        var category = CategoryOf(message);
        return AppStrings.IsEnglish ? category.English : category.Romanian;
    }

    public static string GetDescription(string message)
    {
        var description = message.Trim();
        if (description.StartsWith('['))
        {
            var end = description.IndexOf(']');
            if (end > 1) description = description[(end + 1)..].Trim();
        }
        switch (Tag(message))
        {
            case "NOWPLAYING" when description == "Playback Stopped":
                return AppStrings.IsEnglish ? "Playback stopped" : "Redare oprită";
            case "ETS2SPEED":
                return description + " km/h";
        }
        if (CategoryOf(message).Payload) return description;
        return AppStrings.IsEnglish ? TranslateToEnglish(description) : description;
    }
    private static string TranslateToEnglish(string description)
    {
        var replacements = new Dictionary<string, string>
        {
            ["Firmware-ul ceasului nu are /getscreen."] = "The clock firmware does not provide /getscreen.",
            ["Cadrele UDP nu ajung (firewall?) - folosesc HTTP."] = "UDP frames aren't arriving (firewall?) - using HTTP.",
            ["Ceasul nu răspunde."] = "The clock isn't responding.",
            ["Ceasul nu răspunde."] = "The clock is not responding.",
            ["Aplicația a fost pornită."] = "Application started.", ["Oprit."] = "Stopped.",
            ["Autentificat cu succes ca"] = "Authenticated successfully as", ["Login eșuat după toate încercările."] = "Login failed after all attempts.",
            ["Utilizator sau parolă incorectă."] = "Incorrect username or password.", ["Răspuns neașteptat:"] = "Unexpected response:",
            ["reîncerc"] = "retrying", ["Nu mă pot conecta la"] = "Cannot connect to", ["încercarea"] = "attempt",
            ["Timeout la login"] = "Login timed out", ["Sesiune expirată - reautentificare..."] = "Session expired - signing in again...",
            ["după reautentificare"] = "after signing in again", ["Timeout la"] = "Timed out at",
            ["Acces REFUZAT de Windows."] = "Access was DENIED by Windows.", ["Ascultător activ."] = "Listener active.",
            ["Ascultătorul s-a oprit:"] = "Listener stopped:", ["Eroare la interogare:"] = "Query error:",
            ["Modul detecție pornit, aștept eurotrucks2.exe..."] = "Detection module started; waiting for eurotrucks2.exe...",
            ["eurotrucks2.exe detectat."] = "eurotrucks2.exe detected.", ["eurotrucks2.exe închis - deconectat."] = "eurotrucks2.exe closed - disconnected.",
            ["Telemetria scs-sdk-plugin găsită (revizia"] = "scs-sdk-plugin telemetry found (revision",
            [") - viteză reală activă."] = ") - real speed active.",
            ["Telemetria scs-sdk-plugin lipsește - se detectează doar rularea jocului, fără viteză. Pune scs-telemetry.dll în bin\\win_x64\\plugins al jocului."] =
                "The scs-sdk-plugin telemetry is missing - only whether the game runs is detected, without speed. Put scs-telemetry.dll in the game's bin\\win_x64\\plugins.",
            ["Nu pot citi telemetria:"] = "Can't read the telemetry:",
            ["Nu pot citi viteza din SDK:"] = "Can't read the speed from the SDK:",
            ["Trec pe verificare de rezervă cu 'tasklist'."] = "Falling back to checking with 'tasklist'.", ["a eșuat"] = "failed",
            ["Firmware-ul ceasului nu ofera inca tile-ul Browser Downloads."] = "The clock firmware does not provide the Browser Downloads tile yet.",
            ["Firmware-ul ceasului nu oferă încă tile-ul Voicemeeter."] = "The clock firmware does not provide the Voicemeeter tile yet.",
            ["Nu găsesc ieșirea"] = "Cannot find output", ["Pornește Voicemeeter și verifică busul ales."] = "Start Voicemeeter and check the selected bus.",
            ["Analizor audio pornit pe"] = "Audio analyzer started on",
            ["Primesc date OutGauge de la BeamNG.drive."] = "Receiving OutGauge data from BeamNG.drive.",
            ["Nu mai sosesc date OutGauge."] = "OutGauge data stopped arriving.",
            ["Ascult OutGauge pe"] = "Listening for OutGauge on",
            ["Am ignorat un pachet care nu este OutGauge"] = "Ignored a packet that isn't OutGauge",
            ["se așteaptă 92 sau 96"] = "92 or 96 expected",
            ["Adresa OutGauge"] = "The OutGauge address", ["nu poate fi folosită:"] = "can't be used:",
            ["Firmware-ul ceasului nu are încă tile-ul BeamNG.drive. Actualizează firmware-ul ceasului."] = "The clock firmware has no BeamNG.drive tile yet. Update the clock firmware.",
            ["Primesc viteza din Assetto Corsa."] = "Receiving the speed from Assetto Corsa.",
            ["Nu mai sosește viteza din Assetto Corsa."] = "The speed from Assetto Corsa stopped arriving.",
            ["Nu pot citi telemetria Assetto Corsa:"] = "Can't read the Assetto Corsa telemetry:",
            ["Firmware-ul ceasului nu are încă tile-ul Assetto Corsa. Actualizează firmware-ul ceasului."] = "The clock firmware has no Assetto Corsa tile yet. Update the clock firmware.",
            ["Primesc viteza din NFS Underground 2."] = "Receiving the speed from NFS Underground 2.",
            ["Nu mai sosește viteza din NFS Underground 2."] = "The speed from NFS Underground 2 stopped arriving.",
            ["SPEED2.EXE nu este versiunea acceptată (v1.2 NTSC): viteza nu se citește."] = "SPEED2.EXE isn't the supported version (v1.2 NTSC): the speed isn't read.",
            ["Jocul rulează ca administrator, iar Octoglow Connect nu: viteza nu se poate citi."] = "The game runs as administrator and Octoglow Connect doesn't: the speed can't be read.",
            ["Datele citite din joc nu trec verificarea"] = "The data read from the game fails its checks",
            ["viteza nu se afișează."] = "the speed isn't shown.",
            ["Firmware-ul ceasului nu are încă tile-ul NFS Underground 2. Actualizează firmware-ul ceasului."] = "The clock firmware has no NFS Underground 2 tile yet. Update the clock firmware.",
            ["Primesc viteza din NFS Most Wanted."] = "Receiving the speed from NFS Most Wanted.",
            ["Nu mai sosește viteza din NFS Most Wanted."] = "The speed from NFS Most Wanted stopped arriving.",
            ["speed.exe nu este versiunea acceptată (v1.3): viteza nu se citește."] = "speed.exe isn't the supported version (v1.3): the speed isn't read.",
            ["Firmware-ul ceasului nu are încă tile-ul NFS Most Wanted. Actualizează firmware-ul ceasului."] = "The clock firmware has no NFS Most Wanted tile yet. Update the clock firmware.",
            ["Primesc viteza din NFS Carbon."] = "Receiving the speed from NFS Carbon.",
            ["Nu mai sosește viteza din NFS Carbon."] = "The speed from NFS Carbon stopped arriving.",
            ["NFSC.exe nu este versiunea acceptată (v1.4): viteza nu se citește."] = "NFSC.exe isn't the supported version (v1.4): the speed isn't read.",
            ["Firmware-ul ceasului nu are încă tile-ul NFS Carbon. Actualizează firmware-ul ceasului."] = "The clock firmware has no NFS Carbon tile yet. Update the clock firmware.",
            ["Conectat la qBittorrent."] = "Connected to qBittorrent.", ["Reconectat la qBittorrent."] = "Reconnected to qBittorrent.",
            ["qBittorrent nu răspunde (închis sau cu WebUI-ul oprit)."] = "qBittorrent isn't answering (closed, or its WebUI is off).",
            ["qBittorrent a respins autentificarea. Verifică datele din Integrări și apasă Reconectează."] = "qBittorrent rejected the sign-in. Check the details in Integrations and press Reconnect.",
            ["qBittorrent a blocat temporar acest calculator după prea multe autentificări greșite."] = "qBittorrent has temporarily banned this computer after too many failed sign-ins.",
            ["La adresa setată răspunde altceva decât WebUI-ul qBittorrent."] = "Something other than the qBittorrent WebUI answers at the address set.",
            ["Conexiunea cu qBittorrent s-a întrerupt."] = "The connection to qBittorrent dropped.",
            ["Firmware-ul ceasului nu are încă tile-ul qBittorrent. Actualizează firmware-ul ceasului."] = "The clock firmware has no qBittorrent tile yet. Update the clock firmware.",
            ["Conectat la OBS Studio."] = "Connected to OBS Studio.", ["Reconectat la OBS Studio."] = "Reconnected to OBS Studio.",
            ["OBS Studio nu este disponibil (închis sau cu serverul WebSocket oprit)."] = "OBS Studio isn't available (closed, or its WebSocket server is off).",
            ["OBS Studio a respins autentificarea. Verifică parola OBS WebSocket din Integrări."] = "OBS Studio rejected the sign-in. Check the OBS WebSocket password in Integrations.",
            ["Conexiunea cu OBS Studio s-a întrerupt."] = "The connection to OBS Studio dropped.",
            ["Firmware-ul ceasului nu are încă tile-ul OBS Studio. Actualizează firmware-ul ceasului."] = "The clock firmware has no OBS Studio tile yet. Update the clock firmware.",
        };
        foreach (var (romanian, english) in replacements) description = description.Replace(romanian, english, StringComparison.OrdinalIgnoreCase);
        return description;
    }
}
