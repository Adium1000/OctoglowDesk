using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// The line under each tile in the Tile Manager, as the web interface's lists
// have it: what the tile is showing - the time, the temperature, the song -
// or, for a tile with nothing live to show, what it is for. The time a tile
// stays on screen is in its settings, not here.
public sealed partial class ClockNativeSettingsPage
{
    // "c:" + circuit id or "p:" + priority key, like _rowToggles.
    private readonly Dictionary<string, TextBlock> _tileLines = new();
    // The online tiles' error line and the mark on their icon (see TileError).
    private readonly Dictionary<string, TextBlock> _tileErrors = new();
    private readonly Dictionary<string, FrameworkElement> _tileWarns = new();
    private static readonly HashSet<string> ApiTileIds = ["c:4", "c:11", "c:12"];
    // /state, read when the Tile Manager opens and again after a tile's settings close.
    private ClockDeviceState? _linesState;
    private DateTime _linesStateUpdatedAt;
    // The polled parts: /npstate also carries the temperature and the weather.
    private ClockDeviceState? _linesPlaying;
    private ClockDeviceState? _linesEts2;
    private ClockDeviceState? _linesBeamng;
    private ClockDeviceState? _linesAssetto;
    private ClockDeviceState? _linesNfsu2;
    private ClockDeviceState? _linesNfsMw;
    private ClockDeviceState? _linesNfsCarbon;
    private ClockDeviceState? _linesVoicemeeter;
    private ClockDeviceState? _linesBrowserDownloads;
    private ClockDeviceState? _linesQbit;
    private ClockDeviceState? _linesBambu;
    private ClockDeviceState? _linesAb;
    private ClockDeviceState? _linesStopwatch;
    private ClockDeviceState? _linesAlarm;
    private ClockDeviceState? _linesTimer;
    private ClockDeviceState? _linesObs;
    private CancellationTokenSource? _linesCancellation;
    private CancellationTokenSource? _tileLineReadCancellation;
    private CancellationTokenSource? _linesReloadCancellation;
    private SettingsSurface? _linesOwner;

    private void PauseTileLineReads()
    {
        _tileLineReadCancellation?.Cancel();
        _linesReloadCancellation?.Cancel();
    }

    private bool CanPollTileLines() => _tileDialog is null && _pendingSaves.Count == 0 && _savesRunning == 0 &&
        (_panel is null || ReferenceEquals(_panel, _linesOwner)) &&
        (_circuitList?.Host.IsLoaded == true || _priorityList?.Host.IsLoaded == true);

    private string TileLine(string id)
    {
        if (_linesState is not { } state) return "";
        var fahrenheit = state.Int("tempunit") == 1;
        return id switch
        {
            "c:0" => ClockLine(state.Int("hourformat") == 1, state.Bool("hourLeadingZero")),
            "c:1" => DateLine(state),
            "c:2" => TemperatureLine(_linesPlaying?.Int("lastTemp", state.Int("lastTemp")) ?? state.Int("lastTemp"), fahrenheit),
            "c:3" or "p:" + NowPlayingPriorityKey => _linesPlaying is { } playing && playing.Bool("active")
                ? playing.String("text")
                : PriorityDescription(NowPlayingPriorityKey),
            "c:4" => WeatherLine(state, fahrenheit),
            "c:5" => state.String("mementoText") is { Length: > 0 } memo && !string.IsNullOrWhiteSpace(memo)
                ? memo
                : TileDescription(5),
            "c:6" => L("Desen personalizat 8×32", "Custom drawing 8×32"),
            "c:8" => $"{state.Int("pressureHpa")} hPa {TrendArrow(state.Int("pressureTrend"))}",
            "c:9" => state.Int("ssAnim") switch
            {
                1 => "Pong",
                2 => L("Artificii", "Fireworks"),
                3 => L("Egalizator", "Equalizer"),
                4 => L("Ploaie de meteori", "Meteor Shower"),
                5 => L("Ploaie digitală", "Digital Rain"),
                6 => L("Jocul vieții", "Game of Life"),
                7 => L("Șarpe", "Snake"),
                8 => L("Elice", "Helix"),
                9 => L("Cărămizi", "Bricks"),
                10 => L("Alergător", "Runner"),
                _ => L("Aleatoriu", "Random"),
            },
            "c:10" => HourWeekLine(state,
                _linesPlaying?.Int("lastTemp", state.Int("lastTemp", -999)) ?? state.Int("lastTemp", -999), _linesPlaying),
            "c:11" => CurrencyLine(state),
            "c:13" => _linesPlaying is { } battery && battery.Bool("batteryValid")
                ? Str(battery.Int("batteryPercent")) + "%" : TileDescription(13),
            "c:14" => AfterburnerLine(_linesAb, state),
            "c:15" => state.String("cdText") is { Length: > 0 } countdown
                ? countdown
                : L("Niciun eveniment în viitor", "No upcoming event"),
            // The count comes with the poll on a clock that sends it there.
            "c:12" => (_linesPlaying is { } counter && counter.Has("soc0Valid") ? counter : state) is var social && social.Bool("soc0Valid")
                ? ((long)social.Double("soc0Count")).ToString(Inv)
                : L("Fără date", "No data"),
            "p:notif" => L("Afișează notificări primite de Octoglow Connect", "Shows notifications received from Octoglow Connect"),
            "p:ets2" => FeatureLine("ets2", _linesEts2, state.Bool("ets2Enabled", true), SpeedLine),
            "p:beamng" => FeatureLine("beamng", _linesBeamng, state.Bool("beamngEnabled"), SpeedLine),
            "p:assetto" => FeatureLine("assetto", _linesAssetto, state.Bool("assettoEnabled"), SpeedLine),
            "p:nfsu2" => FeatureLine("nfsu2", _linesNfsu2, state.Bool("nfsu2Enabled"), SpeedLine),
            "p:nfsmw" => FeatureLine("nfsmw", _linesNfsMw, state.Bool("nfsmwEnabled"), SpeedLine),
            "p:nfsc" => FeatureLine("nfsc", _linesNfsCarbon, state.Bool("nfscEnabled"), SpeedLine),
            "p:gamefps" => GameFpsLine(_linesAb, state),
            "p:volume" => PriorityDescription("volume"),
            "p:voicemeeter" => FeatureLine("voicemeeter", _linesVoicemeeter, state.Bool("voicemeeterEnabled", true),
                _ => L("Activ - audio live", "Active - live audio")),
            "p:qbittorrent" => FeatureLine("qbittorrent", _linesQbit, state.Bool("qbitEnabled"),
                qbit => L("Activ - ", "Active - ") + Str(qbit.Int("progress")) + "%"),
            "p:bambu" => FeatureLine("bambu", _linesBambu, state.Bool("bambuEnabled"),
                bambu => L("Imprimă - ", "Printing - ") + Str(bambu.Int("progress")) + "%"),
            "p:browserdownloads" => FeatureLine("browserdownloads", _linesBrowserDownloads, state.Bool("browserDownloadsEnabled"),
                downloads => L("Activ - ", "Active - ") + Str(downloads.Int("progress")) + "%"),
            "p:stopwatch" => _linesStopwatch is { } stopwatch && stopwatch.Bool("running")
                ? L("Rulează - ", "Running - ") + stopwatch.String("text", "00:00:00:00")
                : PriorityDescription("stopwatch"),
            "p:alarm" when _linesAlarm is { } alarm => AlarmLine(alarm),
            "p:obs" => ObsLine(_linesObs, state),
            "p:timer" => TimerLine(_linesTimer, state.Int("timerDurationSec", 300)),
            _ when id.StartsWith("p:", StringComparison.Ordinal) => PriorityDescription(id[2..]),
            _ => "",
        };
    }

    // What the tile shows, as the clock draws it - "CPU 58°C" - or which
    // value is missing.
    private static string AfterburnerLine(ClockDeviceState? polled, ClockDeviceState state)
    {
        var mode = polled?.Int("mode", state.Int("abMode")) ?? state.Int("abMode");
        var label = mode switch { 1 => "CPU", 2 => "GPU", _ => "FPS" };
        if (polled is null || !polled.Bool("live")) return TileDescription(14);
        var value = Str(polled.Int("value"));
        return mode == 0 ? "FPS " + value : label + " " + value + (polled.Bool("fahrenheit") ? "°F" : "°C");
    }

    // The game's frame rate while one is played, as the clock draws it.
    private static string GameFpsLine(ClockDeviceState? polled, ClockDeviceState state)
    {
        var enabled = state.Bool("gameFpsEnabled");
        if (!(polled?.Bool("gameFpsEnabled", enabled) ?? enabled)) return PriorityDescription("gamefps");
        return polled is not null && polled.Bool("gameLive") ? "FPS " + Str(polled.Int("fps")) : PriorityDescription("gamefps");
    }

    // The next alarm, in minutes below the hour, hours below the day and days
    // beyond that, as the web interface counts it.
    private static string AlarmLine(ClockDeviceState alarm)
    {
        if (alarm.Bool("ringing")) return L("Sună!", "Ringing!");
        var minutes = alarm.Int("nextMin", -1);
        if (minutes < 0) return PriorityDescription("alarm");
        if (minutes == 0) return L("Alarmă în mai puțin de un minut", "Alarm in under a minute");
        var (count, unit) = minutes < 60 ? (minutes, L(RomanianPlural(minutes, "minut", "minute", "de minute"), minutes == 1 ? "minute" : "minutes"))
            : minutes < 1440 ? (minutes / 60, L(RomanianPlural(minutes / 60, "oră", "ore", "de ore"), minutes / 60 == 1 ? "hour" : "hours"))
            : (minutes / 1440, L(RomanianPlural(minutes / 1440, "zi", "zile", "de zile"), minutes / 1440 == 1 ? "day" : "days"));
        return L("Alarmă în ", "Alarm in ") + Str(count) + " " + unit + ", " + alarm.String("nextTime");
    }

    // One thing, a few things, and - from twenty up, and again at every round
    // hundred - "de" things.
    private static string RomanianPlural(int count, string one, string few, string many)
    {
        if (count == 1) return one;
        var rest = count % 100;
        return rest == 0 || rest >= 20 ? many : few;
    }

    private static string TimerLine(ClockDeviceState? timer, int durationSeconds)
    {
        if (timer is not null && timer.Bool("finished")) return L("Sună!", "Ringing!");
        if (timer is not null && timer.Int("mode") == 1)
            return timer.Bool("running")
                ? PomodoroPart(timer) + " - " + TimerText(timer.Int("remainingSec"))
                : timer.Bool("inCycle")
                    ? L("Pauză - ", "Paused - ") + PomodoroPart(timer) + " - " + TimerText(timer.Int("remainingSec"))
                    : PriorityDescription("timer");
        return timer is not null && timer.Bool("running")
            ? L("Rulează - ", "Running - ") + TimerText(timer.Int("remainingSec"))
            : PriorityDescription("timer");
    }

    private static string TimerText(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var clock = TimeSpan.FromSeconds(seconds);
        return seconds >= 3600
            ? $"{(int)clock.TotalHours:00}:{clock.Minutes:00}:{clock.Seconds:00}"
            : $"{clock.Minutes:00}:{clock.Seconds:00}";
    }

    // What the clock is showing: REC or LIVE with the duration, as the web
    // interface puts it. When it shows nothing and this computer's own link to
    // OBS is down, that is the likelier reason, so it is said instead.
    private static string ObsLine(ClockDeviceState? polled, ClockDeviceState state)
    {
        var enabled = polled?.Bool("enabled", state.Bool("obsEnabled")) ?? state.Bool("obsEnabled");
        if (!enabled) return PriorityDescription("obs");
        if (polled is not null && polled.Bool("active"))
        {
            var label = polled.Bool("streaming") ? "LIVE" : "REC";
            return polled.Bool("showElapsed", true)
                ? label + " · " + ObsStudioStatus.FormatElapsed((long)polled.Double("elapsedMs"))
                : label;
        }
        return ObsStudioStatus.Current.State is ObsConnectionState.Unavailable or ObsConnectionState.AuthFailed
            ? L("OBS nu este conectat", "OBS is not connected")
            : PriorityDescription("obs");
    }

    // A speed as the panel draws it - "Active - 57 km/h", or mph when the clock
    // is set so. A firmware from before the unit sends km/h only.
    private static string SpeedLine(ClockDeviceState polled)
    {
        var known = polled.Has("shown");
        var value = known ? polled.Int("shown") : polled.Int("speed");
        return L("Activ - ", "Active - ") + Str(value) + (known && polled.Bool("mph") ? " mph" : " km/h");
    }

    // ETS2 and Voicemeeter: switched off, sending, or waiting for Connect to send.
    private static string FeatureLine(string key, ClockDeviceState? polled, bool enabled, Func<ClockDeviceState, string> active)
    {
        if (!(polled?.Bool("enabled", enabled) ?? enabled)) return PriorityDescription(key);
        return polled is not null && polled.Bool("active") ? active(polled) : PriorityDescription(key);
    }

    private static string TrendArrow(int trend) => trend > 0 ? "↑" : trend < 0 ? "↓" : "-";

    private static string ClockLine(bool twelveHour, bool leadingZero)
    {
        var now = DateTime.Now;
        var hour = now.Hour;
        if (twelveHour)
        {
            hour %= 12;
            if (hour == 0) hour = 12;
        }
        return (leadingZero && hour < 10 ? "0" : "") + Str(hour) + ":" + now.Minute.ToString("00", Inv);
    }

    private static string HourWeekLine(ClockDeviceState state, int celsius, ClockDeviceState? polled)
    {
        var now = DateTime.Now;
        var time = ClockLine(state.Int("hwFormat") == 1, state.Bool("hwLeadZero"));
        if (state.Int("hwSkin") == 1)
            return time + ":" + now.Second.ToString("00", Inv);
        var content = state.Int("hwContent", state.Int("hwSkin") == 2 ? 1 : 0);
        if (content != 0)
        {
            var unit = state.Int("tempunit") == 1 ? "°F" : "°C";
            var weather = polled ?? state;
            var valid = content == 2 ? weather.Bool("wxValid") : celsius != -999;
            var degrees = content == 2 ? weather.Double("wxTemp", -999) : celsius;
            var value = !valid || !double.IsFinite(degrees) ? "--" :
                ((int)Math.Round(state.Int("tempunit") == 1 ? degrees * 9.0 / 5 + 32 : degrees, MidpointRounding.AwayFromZero)).ToString(Inv);
            var temperature = value + unit;
            return state.Bool("hwSwap") ? temperature + " · " + time : time + " · " + temperature;
        }
        string[] days = state.Int("datelang") == 1
            ? ["DUM", "LUN", "MAR", "MIE", "JOI", "VIN", "SAM"]
            : ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];
        var day = days[(int)now.DayOfWeek];
        return state.Bool("hwSwap") ? day + " · " + time : time + " · " + day;
    }

    // Today's date in the format the clock is set to.
    private static string DateLine(ClockDeviceState state)
    {
        var today = DateTime.Now;
        var romanian = state.Int("datelang") == 1;
        return state.Int("dateformat", 2) switch
        {
            0 => today.ToString("yyyy.MM.dd", Inv),
            1 => today.ToString("yy.MM.dd", Inv),
            3 => today.ToString("dd.MM", Inv),
            4 => (romanian
                    ? new[] { "Dum", "Lun", "Mar", "Mie", "Joi", "Vin", "Sam" }
                    : new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" })[(int)today.DayOfWeek]
                + " " + today.Day.ToString("00", Inv),
            5 => DatePatternPreview(state.String("customdatefmt", "DD.MM.YYYY").Trim().ToUpperInvariant(), today, romanian),
            _ => today.ToString("dd.MM.yyyy", Inv),
        };
    }

    private static string TemperatureLine(int celsius, bool fahrenheit)
    {
        var unit = fahrenheit ? "°F" : "°C";
        // The sensor reports far outside this when it has no reading.
        if (celsius is < -60 or > 100) return "--" + unit;
        return Str(fahrenheit ? (int)Math.Round(celsius * 9 / 5.0 + 32) : celsius) + unit;
    }

    private string WeatherLine(ClockDeviceState state, bool fahrenheit)
    {
        if (_linesPlaying is not { } weather || !weather.Bool("wxValid"))
        {
            // Nothing fetched yet: where it is for, and whether it can fetch at all.
            var city = state.String("wxCity");
            if (city.Length == 0) return L("Se încarcă...", "Loading...");
            return city + (state.Bool("wxHasKey") ? L(" - API OK", " - API OK") : L("  (fără cheie API)", "  (no API key)"));
        }
        var celsius = weather.Double("wxTemp");
        var temperature = Str((int)Math.Round(fahrenheit ? celsius * 9 / 5 + 32 : celsius)) + (fahrenheit ? "°F" : "°C");
        var humidity = weather.Int("wxHumidity", -1);
        var description = weather.String("wxDesc");
        var preset = state.Int("wxPreset", 7);
        var parts = new List<string>();
        if ((preset & 1) != 0) parts.Add(temperature);
        if ((preset & 2) != 0 && humidity >= 0) parts.Add(Str(humidity) + "%");
        if ((preset & 4) != 0 && description.Length > 0) parts.Add(description);
        return parts.Count > 0 ? string.Join("  ", parts) : $"{temperature}  {humidity}%  {description}";
    }

    private static string CurrencyLine(ClockDeviceState state)
    {
        if (!state.Bool("currencyValid")) return L("Se încarcă...", "Loading...");
        var from = state.String("currencyBase");
        var arrow = TrendArrow(state.Int("currencyTrend"));
        if (!state.Bool("currencyCompare")) return from + "  " + arrow;
        var rate = state.Double("currencyRate");
        var shown = rate.ToString(Math.Abs(rate) >= 1 ? "0.00" : "0.0000", Inv);
        return $"1 {from} = {shown} {state.String("currencyQuote")}  {arrow}";
    }

    private void SetTileLine(string id, TextBlock? line)
    {
        if (line is null) return;
        // One line, cut short like the web interface's: a long song or reminder
        // shouldn't make its row taller than the rest.
        line.TextWrapping = TextWrapping.NoWrap;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        _tileLines[id] = line;
    }

    // Why an online tile has nothing to show, as the clock reports it (the
    // firmware's API_ERR_*: 1 no key, 2 no place / channel, 3 key refused,
    // 4 place / channel not found, 5 no answer), in the web interface's words.
    // Null while nothing is wrong, and on a clock that does not report it.
    private string? TileError(string id)
    {
        if (_linesState is not { } state) return null;
        var field = id switch { "c:4" => "wxErr", "c:12" => "soc0Err", "c:11" => "currencyErr", _ => null };
        if (field is null) return null;
        var code = _linesPlaying is { } polled && polled.Has(field) ? polled.Int(field) : state.Int(field);
        var weather = id == "c:4";
        return code switch
        {
            1 => L("Eroare: cheie API lipsă", "Error: API key missing"),
            2 => weather ? L("Eroare: localitate lipsă", "Error: place missing") : L("Eroare: canal lipsă", "Error: channel missing"),
            3 => L("Eroare: cheie API refuzată", "Error: API key refused"),
            4 => weather ? L("Eroare: localitate negăsită", "Error: place not found") : L("Eroare: canal negăsit", "Error: channel not found"),
            5 => L("Eroare: fără răspuns", "Error: no answer"),
            _ => null,
        };
    }

    private void RefreshTileLines()
    {
        RefreshTileRunButtons();
        foreach (var (id, line) in _tileLines)
        {
            var error = _tileErrors.TryGetValue(id, out var failure) ? TileError(id) : null;
            if (failure is not null)
            {
                // The error takes the line's place, in the error colour, and puts
                // the mark on the tile's icon.
                if (error is not null && failure.Text != error) failure.Text = error;
                failure.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
                line.Visibility = error is null ? Visibility.Visible : Visibility.Collapsed;
                if (_tileWarns.TryGetValue(id, out var mark)) mark.Visibility = failure.Visibility;
            }
            var text = TileLine(id);
            if (line.Text != text) line.Text = text;
        }
    }

    private bool TileLineShown(string id)
    {
        var list = id.StartsWith("c:", StringComparison.Ordinal) ? _circuitList : _priorityList;
        var key = id[2..];
        return list is not null && _tileLines.ContainsKey(id) && !list.Hidden.Contains(key) && !IsRemoved(list, key);
    }

    // Reads what changes on its own every few seconds, as the web interface
    // does, while the Tile Manager is open; the time is this computer's, so it
    // just moves on each second. Hands back what stops it.
    private Action StartTileLines()
    {
        _linesOwner = Surfaces().FirstOrDefault(surface => ReferenceEquals(surface.Content, BuildTarget)) ?? Surface;
        _linesCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _linesCancellation = cancellation;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        var busy = false;
        // When each part was last read. Each keeps the web interface's pace:
        // every second while something counts, slower while nothing moves.
        var lastRead = new Dictionary<string, DateTime>();
        bool Due(string id, double seconds)
        {
            if (!CanPollTileLines() || !TileLineShown(id)) return false;
            var now = DateTime.UtcNow;
            if (lastRead.TryGetValue(id, out var last) && (now - last).TotalSeconds < seconds - 0.25) return false;
            lastRead[id] = now;
            return true;
        }

        async void Poll()
        {
            if (busy || cancellation.IsCancellationRequested || !CanPollTileLines()) return;
            RefreshTileLines();
            busy = true;
            using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            _tileLineReadCancellation = read;
            try
            {
                var token = read.Token;
                // One read serves the song, the temperature and the weather.
                // ... and what the YouTube counter and the exchange rate ran into.
                var playingId = new[] { "c:3", "p:" + NowPlayingPriorityKey, "c:2", "c:4", "c:10", "c:12", "c:11", "c:13" }.FirstOrDefault(TileLineShown);
                if (playingId is not null && Due(playingId, 3))
                    _linesPlaying = await TryGetJsonAsync("/npstate", token) ?? _linesPlaying;
                if (Due("p:ets2", 3))
                    _linesEts2 = await TryGetJsonAsync("/ets2state", token) ?? _linesEts2;
                if (Due("p:beamng", 2))
                    _linesBeamng = await TryGetJsonAsync("/beamngstate", token) ?? _linesBeamng;
                if (Due("p:assetto", 2))
                    _linesAssetto = await TryGetJsonAsync("/assettostate", token) ?? _linesAssetto;
                if (Due("p:nfsu2", 2))
                    _linesNfsu2 = await TryGetJsonAsync("/nfsu2state", token) ?? _linesNfsu2;
                if (Due("p:nfsmw", 2))
                    _linesNfsMw = await TryGetJsonAsync("/nfsmwstate", token) ?? _linesNfsMw;
                if (Due("p:nfsc", 2))
                    _linesNfsCarbon = await TryGetJsonAsync("/nfscstate", token) ?? _linesNfsCarbon;
                if (Due("p:obs", 2))
                    _linesObs = await TryGetJsonAsync("/obsstate", token) ?? _linesObs;
                if (Due("p:voicemeeter", 2))
                    _linesVoicemeeter = await TryGetJsonAsync("/voicemeeterstate", token) ?? _linesVoicemeeter;
                if (Due("p:browserdownloads", 2))
                    _linesBrowserDownloads = await TryGetJsonAsync("/browserdownloadstate", token) ?? _linesBrowserDownloads;
                if (Due("p:qbittorrent", 2))
                    _linesQbit = await TryGetJsonAsync("/qbitstate", token) ?? _linesQbit;
                if (Due("p:bambu", 2))
                    _linesBambu = await TryGetJsonAsync("/bambustate", token) ?? _linesBambu;
                // One read serves both MSI Afterburner lines.
                var afterburnerDue = Due("c:14", 2);
                var gameFpsDue = Due("p:gamefps", 2);
                if (afterburnerDue || gameFpsDue)
                    _linesAb = await TryGetJsonAsync("/abstate", token) ?? _linesAb;
                if (Due("p:stopwatch", _linesStopwatch?.Bool("running") == true ? 1 : 4))
                    _linesStopwatch = await TryGetJsonAsync("/swstate", token) ?? _linesStopwatch;
                if (Due("p:timer", _linesTimer is { } timer && (timer.Bool("running") || timer.Bool("finished")) ? 1 : 4))
                    _linesTimer = await TryGetJsonAsync("/timerstate", token) ?? _linesTimer;
                if (Due("p:alarm", _linesAlarm?.Bool("ringing") == true ? 2 : 15))
                    _linesAlarm = await TryGetJsonAsync("/alarmstate", token) ?? _linesAlarm;
                if (!token.IsCancellationRequested && CanPollTileLines()) RefreshTileLines();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_tileLineReadCancellation, read)) _tileLineReadCancellation = null;
                busy = false;
            }
        }

        timer.Tick += (_, _) => Poll();
        timer.Start();
        Poll();
        return () =>
        {
            timer.Stop();
            cancellation.Cancel();
            if (_linesCancellation == cancellation) _linesCancellation = null;
        };
    }

    // A tile's settings dialog just closed: once what it changed has reached
    // the clock, read the clock again so the lines show the new format, text or city.
    private async Task ReloadTileLinesAsync()
    {
        if (_linesCancellation is not { } cancellation || _tileLines.Count == 0) return;
        _linesReloadCancellation?.Cancel();
        using var reload = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        _linesReloadCancellation = reload;
        try
        {
            while (_pendingSaves.Count > 0 || _savesRunning > 0)
                await Task.Delay(100, reload.Token);
            if (_tileDialog is not null) return;
            var state = await TryGetJsonAsync("/state", reload.Token);
            if (state is null || reload.IsCancellationRequested || _pendingSaves.Count > 0 || _savesRunning > 0) return;
            _linesState = state;
            _linesStateUpdatedAt = DateTime.UtcNow;
            RefreshTileLines();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_linesReloadCancellation, reload)) _linesReloadCancellation = null;
        }
    }
}
