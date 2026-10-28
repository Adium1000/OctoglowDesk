using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OctoglowSender;

// Names, icons and choice lists for the tiles, built on demand rather than
// once so they follow a change of language.
public sealed partial class ClockNativeSettingsPage
{
    private static string? TileGlyph(int id) => id switch
    {
        0 => "",
        1 => "",
        2 => "",
        3 => "",
        4 => "",
        5 => "",
        6 => "",
        8 => "",
        9 => "",
        10 => "",
        11 => "",
        12 => "",
        13 => "\uEBAA",
        14 => "",
        15 => "\uE8BF",
        _ => null,
    };

    private static string TileName(int id) => id switch
    {
        0 => L("Ora", "Time"),
        1 => L("Data", "Date"),
        2 => L("Temperatura", "Temperature"),
        3 => L("Redare curentă", "Now Playing"),
        4 => L("Meteo", "Weather"),
        5 => L("Memento", "Reminder"),
        6 => L("Desen", "Drawing"),
        8 => L("Presiune", "Pressure"),
        9 => L("Mini animații", "Mini Animations"),
        10 => "Watchface",
        11 => L("Monedă", "Currency"),
        12 => "YouTube Subscriber Counter",
        13 => L("Bateria dispozitivului extern", "External Device Battery"),
        14 => "MSI Afterburner",
        15 => L("Evenimente", "Events"),
        _ => $"Tile {id}",
    };

    // One line per circuit tile for the add picker - the web interface's TILE_DESC.
    private static string TileDescription(int id) => id switch
    {
        0 => L("Ceasul, în formatul ales", "The clock, in the format you chose"),
        1 => L("Data curentă", "Today's date"),
        2 => L("Temperatura din senzorul intern", "Temperature from the internal sensor"),
        3 => L("Piesa redată pe calculator", "What is playing on your computer"),
        4 => L("Vremea din localitatea aleasă", "Weather for the location you picked"),
        5 => L("Un text scris de tine", "A text of your own"),
        6 => L("Desenul tău pe matrice", "Your drawing on the matrix"),
        8 => L("Presiunea atmosferică", "Atmospheric pressure"),
        9 => L("Animații când ceasul stă", "Animations while the clock idles"),
        10 => L("Ora, cu ziua, secundele sau temperatura", "Time with the weekday, seconds or temperature"),
        11 => L("Cursul valutar ales", "The exchange rate you picked"),
        12 => L("Numărul de abonați", "Your subscriber count"),
        13 => L("Nivelul bateriei dispozitivului extern", "External device battery level"),
        14 => L("FPS sau temperatura CPU/GPU", "FPS or CPU/GPU temperature"),
        15 => L("Zilele rămase până la un eveniment al tău", "Days until your next event"),
        _ => "",
    };

    private static string PriorityGlyph(string key) => key switch
    {
        "alarm" => "",
        "notif" => "",
        "browserdownloads" => "\uE896",
        "qbittorrent" => "\uEBD3",
        "bambu" => "\uF158",
        "obs" => "\uE714",
        "beamng" => "\uE804",
        "assetto" => "\uE804",
        "nfsu2" => "\uE804",
        "nfsmw" => "\uE804",
        "nfsc" => "\uE804",
        "gamefps" => "\uE7FC",
        "volume" => "\uE767",
        "lowbattery" => "\uEBAA",
        "voicemeeter" => "\uE767",
        "ets2" => "",
        "nowplaying" => "",
        "stopwatch" => "",
        "timer" => "",
        "webaccess" => "",
        _ => "",
    };

    private static string PriorityName(string key) => key switch
    {
        "alarm" => L("Alarmă", "Alarm"),
        "notif" => L("Notificări", "Notifications"),
        "browserdownloads" => "Browser Downloads",
        "qbittorrent" => "qBittorrent",
        "bambu" => "Bambu Studio",
        "obs" => "OBS Studio",
        "beamng" => "BeamNG.drive",
        "assetto" => "Assetto Corsa",
        "nfsu2" => "NFS Underground 2",
        "nfsmw" => "NFS Most Wanted",
        "nfsc" => "NFS Carbon",
        "gamefps" => L("FPS în joc", "In-game FPS"),
        "volume" => L("Schimbare volum", "Volume Change"),
        "lowbattery" => L("Baterie scăzută pe dispozitivul extern", "External Device Low Battery"),
        "voicemeeter" => "Voicemeeter",
        "ets2" => "Euro Truck Simulator 2",
        "nowplaying" => L("Redare curentă", "Now Playing"),
        "stopwatch" => L("Cronometru", "Stopwatch"),
        "timer" => "Timer",
        "webaccess" => L("Alerte", "Alerts"),
        _ => key,
    };

    private static string PriorityDescription(string key) => key switch
    {
        "lowbattery" => L("Apare când bateria dispozitivului extern scade la 20% și nu se încarcă", "Shows up when the external device's battery drops to 20% and isn't charging"),
        "alarm" => L("Până la trei alarme, pe zilele alese", "Up to three alarms, on the days you pick"),
        "notif" => L("Mesajele trimise de Octoglow Connect", "Messages sent by Octoglow Connect"),
        "volume" => L("Volumul Windows, timp de 3 secunde după ultima schimbare", "Windows volume, for 3 seconds after the last change"),
        "browserdownloads" => L("Progresul descărcărilor din browsere Chromium", "Download progress from Chromium browsers"),
        "qbittorrent" => L("Progresul unui torent activ", "Progress of an active torrent"),
        "bambu" => L("Progresul imprimării 3D din imprimanta Bambu", "3D print progress from the Bambu printer"),
        "obs" => L("Starea înregistrării sau transmisiei live", "Recording or live stream status"),
        "voicemeeter" => L("Animația audio a busului Voicemeeter ales", "Audio animation of the selected Voicemeeter bus"),
        "ets2" => L("Vitezometrul și viteza camionului pe matrice", "Truck speed and speedometer on the matrix"),
        "beamng" => L("Vitezometrul și viteza vehiculului pe matrice", "Vehicle speed and speedometer on the matrix"),
        "assetto" => L("Vitezometrul și viteza mașinii pe matrice", "Car speed and speedometer on the matrix"),
        "nfsu2" => L("Vitezometrul și viteza mașinii pe matrice", "Car speed and speedometer on the matrix"),
        "nfsmw" => L("Vitezometrul și viteza mașinii pe matrice", "Car speed and speedometer on the matrix"),
        "nfsc" => L("Vitezometrul și viteza mașinii pe matrice", "Car speed and speedometer on the matrix"),
        "gamefps" => L("FPS-ul jocului din RivaTuner, cât timp joci", "The game's FPS from RivaTuner, while you play"),
        "nowplaying" => L("Piesa redată, peste restul tile-urilor", "What is playing, over the other tiles"),
        "stopwatch" => L("Timpul măsurat, în timp real", "Elapsed time, live"),
        "timer" => L("Temporizator cu alarmă sau Pomodoro", "A timer with an alarm, or Pomodoro"),
        "webaccess" => L("Afișează alerte despre interfața web și conexiune", "Shows alerts about the web interface and the connection"),
        _ => "",
    };

    // The clock's prioHidden bits (Octoglow.ino, PRIORITY_ID_*). Now Playing's is
    // never used: whether it is in the manager is bit 3 of tileHidden.
    private static int PrioBit(string key) => key switch
    {
        "notif" => 0,
        "ets2" => 1,
        "nowplaying" => 2,
        "stopwatch" => 3,
        "timer" => 4,
        "webaccess" => 5,
        "alarm" => 6,
        "voicemeeter" => 7,
        "browserdownloads" => 8,
        "obs" => 9,
        "lowbattery" => 10,
        "beamng" => 11,
        "assetto" => 12,
        "qbittorrent" => 13,
        "bambu" => 14,
        "gamefps" => 15,
        "volume" => 19,
        "nfsu2" => 16,
        "nfsmw" => 17,
        "nfsc" => 18,
        _ => 9,
    };

    // The priority tiles this clock has, in its order: a tile its firmware
    // predates is left out rather than offered and then refused.
    private static List<string> PriorityKeys(ClockDeviceState state)
    {
        var keys = ParsePriorities(state.String("priorityOrder"));
        if (!state.Has("obsEnabled")) keys.Remove("obs");
        if (!state.Has("beamngEnabled")) keys.Remove("beamng");
        if (!state.Has("assettoEnabled")) keys.Remove("assetto");
        if (!state.Has("qbitEnabled")) keys.Remove("qbittorrent");
        if (!state.Has("bambuEnabled")) keys.Remove("bambu");
        if (!state.Has("gameFpsEnabled")) keys.Remove("gamefps");
        if (!state.Has("volumeEnabled")) keys.Remove("volume");
        if (!state.Has("nfsu2Enabled")) keys.Remove("nfsu2");
        if (!state.Has("nfsmwEnabled")) keys.Remove("nfsmw");
        if (!state.Has("nfscEnabled")) keys.Remove("nfsc");
        if (!state.Has("iconSelBattery")) keys.Remove("lowbattery");
        return keys;
    }

    private static List<string> ParsePriorities(string value)
    {
        // The clock's own default order (Octoglow.ino, priorityOrder).
        var known = new[] { "alarm", "lowbattery", "volume", "notif", "browserdownloads", "qbittorrent", "bambu", "obs", "voicemeeter", "ets2", "beamng", "assetto", "nfsu2", "nfsmw", "nfsc", "gamefps", "nowplaying", "stopwatch", "timer", "webaccess" };
        var result = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(known.Contains)
            .Distinct()
            .ToList();
        foreach (var item in known)
            if (!result.Contains(item)) result.Add(item);
        return result;
    }

    private static string TileSettingsTitle(string key) => key switch
    {
        "ip" => L("Adresă IP", "IP address"),
        NowPlayingPriorityKey => TileName(3),
        _ when int.TryParse(key, NumberStyles.Integer, Inv, out var id) => TileName(id),
        _ => PriorityName(key),
    };

    private static string TileSettingsGlyph(string key) => key switch
    {
        "ip" => "",
        _ when int.TryParse(key, NumberStyles.Integer, Inv, out var id) => TileGlyph(id) ?? "",
        _ => PriorityGlyph(key),
    };

    // What each tile can have changed about its look, under the names /state
    // and /settings use. A null key is a setting the tile doesn't have - the
    // low battery alert has no transition of its own (it plays Notifications').
    // A key the clock's /state doesn't carry is a setting its firmware predates,
    // and is not offered either.
    private sealed record AppearanceTile(
        string Key,
        string? HideKey,
        string? ScrollKey,
        string? SpeedKey,
        string? FontKey,
        string? IconState,
        string? IconForm,
        string? TransitionKey,
        string? TransitionSpeedKey,
        bool ScrollViaTimer = false);

    private static AppearanceTile[] AppearanceTiles() =>
    [
        new("0", null, null, null, null, null, null, "tileTransHour", "tileTransSpdHour"),
        new("1", "hideIconDate", "scrollTypeDate", "scrollSpeedDate", "fontTypeDate", "iconSelDate", "iconSelDate", "tileTransDate", "tileTransSpdDate"),
        new("2", "hideIconTemp", "scrollTypeTemp", "scrollSpeedTemp", "fontTypeTemp", "iconSelTemp", "iconSelTemp", "tileTransTemp", "tileTransSpdTemp"),
        new(NowPlayingTileKey, "hideIconNowPlaying", "scrollTypeNowPlaying", "scrollSpeedNowPlaying", "fontTypeNowPlaying", null, null, "tileTransNp", "tileTransSpdNp"),
        new("4", "hideIconWeather", "scrollTypeWeather", "scrollSpeedWeather", "fontTypeWeather", null, null, "tileTransWx", "tileTransSpdWx"),
        new("5", "hideIconReminder", "scrollTypeReminder", "scrollSpeedReminder", "fontTypeReminder", "iconSelReminder", "iconSelRem", "tileTransRem", "tileTransSpdRem"),
        new("6", null, null, null, null, null, null, "tileTransCanvas", "tileTransSpdCanvas"),
        new("8", "hideIconPressure", "scrollTypePressure", "scrollSpeedPressure", "fontTypePressure", "iconSelPressure", "iconSelPress", "tileTransPress", "tileTransSpdPress"),
        new("9", null, null, null, null, null, null, "tileTransSs", "tileTransSpdSs"),
        new("10", null, null, null, null, null, null, "tileTransHw", "tileTransSpdHw"),
        new("11", "hideIconCurrency", "scrollTypeCurrency", "scrollSpeedCurrency", "fontTypeCurrency", "iconSelCurrency", "iconSelCurr", "tileTransCurr", "tileTransSpdCurr"),
        new("12", "hideIconYoutube", "scrollTypeYoutube", "scrollSpeedYoutube", "fontTypeYoutube", "iconSelYoutube", "iconSelYt", "tileTransYt", "tileTransSpdYt"),
        new("13", null, null, null, "fontTypeBattery", "iconSelBattery", "iconSelBattery", "tileTransHour", "tileTransSpdHour"),
        new("14", "hideIconAfterburner", "scrollTypeAfterburner", "scrollSpeedAfterburner", "fontTypeAfterburner", "iconSelAfterburner", "iconSelAfterburner", "tileTransAfterburner", "tileTransSpdAfterburner"),
        new("15", "hideIconCountdown", "scrollTypeCountdown", "scrollSpeedCountdown", "fontTypeCountdown", "iconSelCountdown", "iconSelCountdown", "tileTransCountdown", "tileTransSpdCountdown"),
        new("notif", "hideIconNotif", "scrollTypeNotif", "scrollSpeedNotif", "fontTypeNotif", "iconSelNotif", "iconSelNotif", "tileTransNotif", "tileTransSpdNotif"),
        new("webaccess", "hideIconWebAccess", "scrollTypeWebAccess", "scrollSpeedWebAccess", "fontTypeWebAccess", null, null, "tileTransWeb", "tileTransSpdWeb"),
        new("ets2", null, null, null, "fontTypeEts2", null, null, "tileTransEts2", "tileTransSpdEts2"),
        new("beamng", null, null, null, "fontTypeBeamng", null, null, "tileTransBeamng", "tileTransSpdBeamng"),
        new("assetto", null, null, null, "fontTypeAssetto", null, null, "tileTransAssetto", "tileTransSpdAssetto"),
        new("nfsu2", null, null, null, "fontTypeNfsu2", null, null, "tileTransNfsu2", "tileTransSpdNfsu2"),
        new("nfsmw", null, null, null, "fontTypeNfsmw", null, null, "tileTransNfsmw", "tileTransSpdNfsmw"),
        new("nfsc", null, null, null, "fontTypeNfsc", null, null, "tileTransNfsc", "tileTransSpdNfsc"),
        new("gamefps", "hideIconGameFps", "scrollTypeGameFps", "scrollSpeedGameFps", "fontTypeGameFps", "iconSelGameFps", "iconSelGameFps", "tileTransGameFps", "tileTransSpdGameFps"),
        new("volume", "hideIconVolume", "scrollTypeVolume", "scrollSpeedVolume", "fontTypeVolume", "iconSelVolume", "iconSelVolume", "tileTransVolume", "tileTransSpdVolume"),
        new("voicemeeter", "hideIconVoicemeeter", null, null, null, "iconSelVoicemeeter", "iconSelVoicemeeter", "tileTransVm", "tileTransSpdVm"),
        new("browserdownloads", "hideIconBrowserDownloads", null, null, null, "iconSelBrowserDownloads", "iconSelBrowserDownloads", "tileTransBrowserDownloads", "tileTransSpdBrowserDownloads"),
        new("qbittorrent", "hideIconQbit", null, null, null, "iconSelQbit", "iconSelQbit", "tileTransQbit", "tileTransSpdQbit"),
        new("bambu", "hideIconBambu", null, null, null, "iconSelBambu", "iconSelBambu", "tileTransBambu", "tileTransSpdBambu"),
        new("obs", "hideIconObs", "scrollTypeObs", "scrollSpeedObs", "fontTypeObs", "iconSelObs", "iconSelObs", "tileTransObs", "tileTransSpdObs"),
        new("alarm", null, null, null, "fontTypeAlarm", null, null, "tileTransAlarm", "tileTransSpdAlarm"),
        new("lowbattery", null, null, null, "fontTypeLowBattery", "iconSelLowBattery", "iconSelLowBattery", null, null),
        new("stopwatch", null, "scrollTypeStopwatch", "scrollSpeedStopwatch", "fontTypeStopwatch", null, null, "tileTransSw", "tileTransSpdSw"),
        new("timer", null, "scrollTypeTimer", "scrollSpeedTimer", "fontTypeTimer", null, null, "tileTransTmr", "tileTransSpdTmr", ScrollViaTimer: true),
        new("ip", "hideIconIp", "scrollTypeIp", "scrollSpeedIp", "fontTypeIp", "iconSelIp", "iconSelIp", "tileTransIp", "tileTransSpdIp"),
    ];

    private static (string, string)[] ScrollChoices() =>
        [("Bounce", "0"), ("Wrap", "1"), ("Bounce + Icon", "2"), ("Wrap + Icon", "3")];

    private static (string, string)[] FontChoices() =>
    [
        ("Marymba", "0"), ("Tiko", "1"), ("Mako", "2"), (L("Segmente", "Segments"), "3"),
        (L("Segmente pline", "Solid Segments"), "4"), ("Bold", "5"), ("Italic", "6"),
        (L("Țopăit", "Bouncy"), "7"), (L("Bloc", "Block"), "10"),
    ];

    private static (string, string)[] TransitionChoices() =>
    [
        (L("Fără", "None"), "0"), (L("Derulare stânga", "Slide Left"), "1"), (L("Derulare dreapta", "Slide Right"), "2"),
        (L("Derulare sus", "Slide Up"), "3"), (L("Derulare jos", "Slide Down"), "4"), (L("Transformare", "Morph"), "5"),
        (L("Apariție și dispariție", "Fade In and Out"), "6"), (L("Extindere stânga", "Expand from Left"), "7"),
        (L("Extindere dreapta", "Expand from Right"), "8"), (L("Extindere din centru", "Expand from Center"), "9"),
    ];

    // The tones an alarm can ring with; the ids are the firmware's.
    private static (string, string)[] AlarmTones() =>
    [
        (L("Clasic", "Classic"), "classic"), ("Carillon", "chimes"), (L("Dimineață", "Morning"), "morning"),
        ("Radar", "radar"), ("Insistent", "insistent"), (L("Sirenă", "Siren"), "siren"), (L("Melodie", "Melody"), "melody"),
        (L("Răsărit", "Sunrise"), "sunrise"), (L("Cutie muzicală", "Music Box"), "musicbox"), (L("Arpegii", "Arpeggios"), "arpeggio"),
        (L("Ciripit", "Birdsong"), "birds"), ("Zen", "zen"), (L("Clopoței de vânt", "Wind Chimes"), "windchime"),
        (L("Trezire blândă", "Gentle Wake"), "gentle"),
    ];

    // The languages OpenWeatherMap describes the weather in, with the codes it
    // wants - the same list the web interface offers.
    private static IEnumerable<(string, string)> WeatherLanguages()
    {
        (string Code, string Ro, string En)[] languages =
        [
            ("af", "Afrikaans", "Afrikaans"), ("al", "Albaneză", "Albanian"), ("ar", "Arabă", "Arabic"),
            ("az", "Azeră", "Azerbaijani"), ("bg", "Bulgară", "Bulgarian"), ("ca", "Catalană", "Catalan"),
            ("cz", "Cehă", "Czech"), ("da", "Daneză", "Danish"), ("de", "Germană", "German"),
            ("el", "Greacă", "Greek"), ("en", "Engleză", "English"), ("eu", "Bască", "Basque"),
            ("fa", "Persană", "Persian"), ("fi", "Finlandeză", "Finnish"), ("fr", "Franceză", "French"),
            ("gl", "Galiciană", "Galician"), ("he", "Ebraică", "Hebrew"), ("hi", "Hindi", "Hindi"),
            ("hr", "Croată", "Croatian"), ("hu", "Maghiară", "Hungarian"), ("id", "Indoneziană", "Indonesian"),
            ("it", "Italiană", "Italian"), ("ja", "Japoneză", "Japanese"), ("kr", "Coreeană", "Korean"),
            ("la", "Letonă", "Latvian"), ("lt", "Lituaniană", "Lithuanian"), ("mk", "Macedoneană", "Macedonian"),
            ("nl", "Neerlandeză", "Dutch"), ("no", "Norvegiană", "Norwegian"), ("pl", "Poloneză", "Polish"),
            ("pt", "Portugheză", "Portuguese"), ("pt_br", "Portugheză (Brazilia)", "Portuguese (Brazil)"),
            ("ro", "Română", "Romanian"), ("ru", "Rusă", "Russian"), ("sk", "Slovacă", "Slovak"),
            ("sl", "Slovenă", "Slovenian"), ("sp", "Spaniolă", "Spanish"), ("sr", "Sârbă", "Serbian"),
            ("sv", "Suedeză", "Swedish"), ("th", "Thailandeză", "Thai"), ("tr", "Turcă", "Turkish"),
            ("ua", "Ucraineană", "Ukrainian"), ("vi", "Vietnameză", "Vietnamese"),
            ("zh_cn", "Chineză simplificată", "Chinese (Simplified)"), ("zh_tw", "Chineză tradițională", "Chinese (Traditional)"),
            ("zu", "Zulu", "Zulu"),
        ];
        var order = StringComparer.Create(CultureInfo.GetCultureInfo(AppStrings.IsEnglish ? "en-US" : "ro-RO"), ignoreCase: true);
        return languages
            .Select(language => ($"{L(language.Ro, language.En)} ({language.Code})", language.Code))
            .OrderBy(language => language.Item1, order);
    }

    // The preview lives inside the ComboBox item. That keeps each settings row
    // at its normal height and also shows every icon while the list is open.
    // Selection 0 means "use the tile's native icon", supplied by the caller.
    private static ComboBox IconChoiceBox(int selected, int defaultSelection, string? automaticLabel = null)
    {
        (string, string)[] names =
        [
            (automaticLabel ?? L("Implicită", "Default"), "0"), (L("Data", "Date"), "1"), (L("Temperatura", "Temperature"), "2"), (L("Memento", "Reminder"), "3"),
            (L("Notă muzicală", "Musical Note"), "4"), (L("Plic", "Envelope"), "5"), (L("Săgeată sus", "Arrow Up"), "6"),
            (L("Săgeată jos", "Arrow Down"), "7"), ("Minus", "8"), (L("Soare", "Sun"), "9"), (L("Nor", "Cloud"), "10"),
            (L("Ploaie", "Rain"), "11"), (L("Furtună", "Storm"), "12"), (L("Ninsoare", "Snow"), "13"), (L("Vânt", "Wind"), "14"),
            (L("Lună", "Moon"), "15"), (L("Semnal", "Signal"), "16"), ("Access Point", "17"), (L("Exclamare", "Exclamation"), "18"),
            (L("Pauză", "Pause"), "19"), ("Euro", "20"), ("Bluetooth", "21"), (L("Blocat", "Locked"), "22"), ("WiFi", "23"),
            ("Video", "24"), (L("Difuzor", "Speaker"), "25"), (L("Săgeată de descărcare", "Download arrow"), "26"),
            (L("Cameră de înregistrare", "Recording camera"), "27"),
            ("Laptop", "28"), (L("Telefon", "Phone"), "29"), (L("Notă muzicală B", "Music note B"), "30"),
            (L("Baterie goală", "Battery Empty"), "31"), (L("Baterie 1/5", "Battery 1/5"), "32"), (L("Baterie 2/5", "Battery 2/5"), "33"),
            (L("Baterie 3/5", "Battery 3/5"), "34"), (L("Baterie 4/5", "Battery 4/5"), "35"), (L("Baterie 5/5", "Battery 5/5"), "36"),
            (L("Cablu", "Cord"), "37"), (L("Boxă", "Speaker cabinet"), "38"), (L("Căști cu microfon", "Headset with mic"), "39"),
            (L("Căști", "Headphones"), "40"),
            (L("Avertizare", "Warning"), "41"), (L("Focalizare obiect", "Object focus"), "42"), (L("Informație", "Info"), "43"),
            (L("Cip", "Chip"), "44"),
        ];
        var box = new ComboBox
        {
            MinWidth = 230,
            MaxDropDownHeight = 420,
            Padding = new Thickness(5, 0, 8, 0),
        };
        var visuals = new Dictionary<ComboBoxItem, (string Label, int Icon)>();
        foreach (var (label, value) in names)
        {
            _ = int.TryParse(value, NumberStyles.Integer, Inv, out var selection);
            var resolvedIcon = selection == 0 ? defaultSelection : selection;
            var item = new ComboBoxItem
            {
                Content = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                Tag = value,
                MinHeight = 34,
                Padding = new Thickness(5, 0, 8, 0),
            };
            visuals[item] = (label, resolvedIcon);
            box.Items.Add(item);
            if (selection == selected) box.SelectedItem = item;
        }
        if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;

        void AddPreview(ComboBoxItem item)
        {
            if (item.Content is StackPanel || !visuals.TryGetValue(item, out var visual)) return;
            item.Content = IconChoiceContent(visual.Label, visual.Icon);
        }
        if (box.SelectedItem is ComboBoxItem selectedItem) AddPreview(selectedItem);
        box.DropDownOpened += (_, _) =>
        {
            foreach (var item in box.Items.OfType<ComboBoxItem>()) AddPreview(item);
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem item) AddPreview(item);
        };
        return box;
    }

    private static StackPanel IconChoiceContent(string label, int selection)
    {
        var pattern = selection >= 1 && selection <= IconPatterns.Length
            ? IconPatterns[selection - 1]
            : AutoIconPattern;
        var icon = new Grid { Width = 26, Height = 26 };
        icon.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = PixelIconGeometry(pattern),
            Fill = Resource<Brush>("AccentFillColorDefaultBrush"),
            Stretch = Stretch.Uniform,
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var previewLabel = L("Previzualizare iconiță", "Icon preview");
        ToolTipService.SetToolTip(icon, previewLabel);
        AutomationProperties.SetName(icon, previewLabel);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                icon,
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }

    // A fresh geometry belongs to one preview only. WinUI rejects sharing the
    // same Geometry instance between several Path elements.
    private static Geometry PixelIconGeometry(byte[] pattern)
    {
        var geometry = new GeometryGroup();
        const double step = 3;
        const double pixel = 2.55;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if ((pattern[y] & (0x80 >> x)) == 0) continue;
                geometry.Children.Add(new RectangleGeometry
                {
                    Rect = new Windows.Foundation.Rect(x * step + (step - pixel) / 2,
                        y * step + (step - pixel) / 2, pixel, pixel),
                });
            }
        }
        return geometry;
    }

    private static int DefaultIconSelection(string key) => key switch
    {
        "1" => 1,
        "2" => 2,
        "5" => 3,
        "8" => 8,
        "11" => 8,
        "12" => 24,
        "notif" => 5,
        "voicemeeter" => 25,
        "browserdownloads" => 26,
        "qbittorrent" => 26,
        "bambu" => 42,
        "obs" => 27,
        "13" => 36,
        // Chip; the icons page makes it Info (43) while the tile shows FPS.
        "14" => 44,
        // The calendar, as the Date tile has.
        "15" => 1,
        // Info, as the MSI Afterburner tile has for its FPS.
        "gamefps" => 43,
        "volume" => 38,
        "lowbattery" => 36,
        "ip" => 16,
        _ => 0,
    };

    // Rows are MSB-first, exactly like ICON_CATALOG in the firmware.
    private static readonly byte[] AutoIconPattern =
        [0b00000000, 0b00111100, 0b01000010, 0b00000100, 0b00001000, 0b00000000, 0b00001000, 0b00000000];

    private static readonly byte[][] IconPatterns =
    [
        [0b01111110, 0b11111111, 0b11111111, 0b10000001, 0b10000001, 0b10000001, 0b10000001, 0b01111110],
        [0b00011000, 0b00011000, 0b00011000, 0b00011000, 0b00011000, 0b00101100, 0b00100100, 0b00011000],
        [0b00100100, 0b01011010, 0b00111100, 0b00111100, 0b00111100, 0b01111110, 0b00011000, 0b00000000],
        [0b11100000, 0b10011100, 0b10000100, 0b10000100, 0b11000100, 0b11100110, 0b11000111, 0b00000110],
        [0b00000000, 0b01111110, 0b11000011, 0b10100101, 0b10011001, 0b10000001, 0b01111110, 0b00000000],
        [0b00011000, 0b00111100, 0b01111110, 0b11011011, 0b10011001, 0b00011000, 0b00011000, 0b00011000],
        [0b00011000, 0b00011000, 0b00011000, 0b10011001, 0b11011011, 0b01111110, 0b00111100, 0b00011000],
        [0b00000000, 0b00000000, 0b00000000, 0b01111110, 0b01111110, 0b00000000, 0b00000000, 0b00000000],
        [0b00011000, 0b01000010, 0b00011000, 0b10111101, 0b10111101, 0b00011000, 0b01000010, 0b00011000],
        [0b00000000, 0b00111000, 0b01000110, 0b10000001, 0b10000001, 0b01111110, 0b00000000, 0b00000000],
        [0b00000000, 0b00111000, 0b01000110, 0b10000001, 0b10000001, 0b01111110, 0b01010100, 0b00101010],
        [0b00111000, 0b01000110, 0b10000001, 0b10000001, 0b01111110, 0b00100100, 0b01101100, 0b01001000],
        [0b00111000, 0b01000110, 0b10000001, 0b10010001, 0b00111000, 0b01010010, 0b11100111, 0b01000010],
        [0b00001100, 0b00010010, 0b00000010, 0b11111100, 0b00000000, 0b11111000, 0b00000100, 0b00001000],
        [0b00111100, 0b01110000, 0b11100000, 0b11100000, 0b11100000, 0b11100000, 0b01110000, 0b00111100],
        [0b00000001, 0b00000011, 0b00001011, 0b00011011, 0b01011011, 0b11011011, 0b11011011, 0b00000000],
        [0b00011000, 0b01000010, 0b00011000, 0b10111101, 0b10111101, 0b00011000, 0b01000010, 0b00011000],
        [0b00011000, 0b00011000, 0b00011000, 0b00011000, 0b00011000, 0b00000000, 0b00011000, 0b00011000],
        [0b00000000, 0b01100110, 0b01100110, 0b01100110, 0b01100110, 0b01100110, 0b01100110, 0b00000000],
        [0b00011000, 0b00100100, 0b01110000, 0b00100000, 0b01110000, 0b00100100, 0b00011000, 0b00000000],
        [0b00001000, 0b00101100, 0b00011010, 0b00001100, 0b00001100, 0b00011010, 0b00101100, 0b00001000],
        [0b00000000, 0b00111100, 0b01000110, 0b01001010, 0b01010010, 0b01100010, 0b00111100, 0b00000000],
        [0b00000010, 0b00001001, 0b00000101, 0b00110101, 0b00110101, 0b00000101, 0b00001001, 0b00000010],
        [0b00000000, 0b01111110, 0b11101111, 0b11100111, 0b11100111, 0b11101111, 0b01111110, 0b00000000],
        [0b00010000, 0b00110010, 0b11110001, 0b11110101, 0b11110101, 0b11110001, 0b00110010, 0b00010000],
        [0b00011000, 0b00011000, 0b00011000, 0b00011000, 0b01111110, 0b00111100, 0b10011001, 0b11111111],
        [0b00000000, 0b10011111, 0b11011111, 0b11111111, 0b11111111, 0b11011111, 0b10011111, 0b00000000],
        [0x00, 0x7E, 0x42, 0x42, 0x42, 0x7E, 0xFF, 0x00],
        [0x3C, 0x42, 0x42, 0x42, 0x42, 0x42, 0x5A, 0x3C],
        [0x10, 0x30, 0x50, 0x10, 0x18, 0x1C, 0x08, 0x00],
        [0x00, 0xFE, 0x82, 0x83, 0x83, 0x82, 0xFE, 0x00],
        [0x00, 0xFE, 0xC2, 0xC3, 0xC3, 0xC2, 0xFE, 0x00],
        [0x00, 0xFE, 0xE2, 0xE3, 0xE3, 0xE2, 0xFE, 0x00],
        [0x00, 0xFE, 0xF2, 0xF3, 0xF3, 0xF2, 0xFE, 0x00],
        [0x00, 0xFE, 0xFA, 0xFB, 0xFB, 0xFA, 0xFE, 0x00],
        [0x00, 0xFE, 0xFE, 0xFF, 0xFF, 0xFE, 0xFE, 0x00],
        [0x24, 0x24, 0x24, 0x7E, 0x3C, 0x18, 0x18, 0x18],
        [0x7E, 0x5A, 0x42, 0x5A, 0x66, 0x66, 0x5A, 0x7E],
        [0x7E, 0xC3, 0x81, 0x81, 0x81, 0xE7, 0xE7, 0xEF],
        [0x7E, 0xC3, 0x81, 0x81, 0x81, 0xE7, 0xE7, 0xE7],
        [0x18, 0x18, 0x24, 0x24, 0x66, 0x7E, 0xE7, 0xFF],
        [0xC3, 0x81, 0x18, 0x18, 0x00, 0x3C, 0x81, 0xC3],
        [0x00, 0x18, 0x18, 0x00, 0x18, 0x18, 0x18, 0x00],
        [0x24, 0x7E, 0xC3, 0x5A, 0x5A, 0xC3, 0x7E, 0x24],
    ];
}
