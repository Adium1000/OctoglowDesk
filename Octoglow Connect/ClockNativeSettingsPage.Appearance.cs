using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// The Tile Manager's appearance pages. Like the web interface, each kind of
// setting has a page of its own: one value for every tile at once, then one
// for each tile that is in the lists.
public sealed partial class ClockNativeSettingsPage
{
    // The setting for every tile at once sits on the card that opens the page,
    // as it does on the web interface's list; inside is what each tile has.
    private void BuildAppearanceSection(ClockDeviceState state)
    {
        // As the web interface's "Display settings": the setting for every tile
        // sits on the card, what each tile has is a page deeper.
        AddSection(L("Setări afișare", "Display Settings"));

        // The clock's own switch sets every tile's along with it.
        var hideIcons = Toggle(state.Bool("hideIcons"));
        hideIcons.Toggled += (_, _) => QueueSetting("hideIcons", hideIcons.IsOn ? "1" : "0");
        AddAppearanceLink("", L("Dezactivați iconițele tile-urilor", "Disable Tile Icons"),
            L("Ascundeți iconițele din stânga tile-urilor", "Hide the icons on the left of the tiles"), hideIcons, BuildHideIconsPage);

        AddAppearanceLink("", L("Setări iconițe", "Icon Settings"),
            L("Alegeți iconița afișată pentru fiecare tile", "Choose the icon shown for each tile"), null, BuildIconsPage);

        var scrollType = ChoiceBox(ScrollChoices(), Str(state.Int("scrollType")), 180);
        scrollType.SelectionChanged += (_, _) => QueueSetting("scrollType", ChoiceValue(scrollType));
        AddAppearanceLink("", L("Tip scroll", "Scroll Type"),
            L("Modul de scroll al textului pe tile-uri", "The text scroll mode on the tiles"),
            EffectPicker(scrollType, L("Tip scroll", "Scroll Type"), false,
                new EffectLook(StepMs: state.Int("scrollSpeed", 40), Font: state.Int("fontType"))), BuildScrollTypePage);

        var transition = ChoiceBox(TransitionChoices(), Str(state.Int("tileTransGlobal")), 180);
        transition.SelectionChanged += (_, _) => ApplyTransitionToEveryTile(ChoiceValue(transition));
        AddAppearanceLink("", L("Tranziție tile", "Tile Transition"),
            L("Efectul de tranziție între tile-uri", "The transition effect between tiles"),
            EffectPicker(transition, L("Tranziție tile", "Tile Transition"), true,
                new EffectLook(Pace: state.Int("tileTransSpd", 100), Font: state.Int("fontType"))), BuildTransitionPage);

        var font = ChoiceBox(FontChoices(), Str(state.Int("fontType")), 180);
        font.SelectionChanged += (_, _) => QueueSetting("fontType", ChoiceValue(font));
        AddAppearanceLink("", L("Tip font", "Font Type"),
            L("Fontul folosit pentru textul de pe tile-uri", "The font used for the text on the tiles"),
            FontPicker(font, L("Tip font", "Font Type")), BuildFontPage);
    }

    private void AddAppearanceLink(string glyph, string title, string description, FrameworkElement? control, Func<ClockDeviceState, Action?> build) =>
        Add(NavCard(glyph, title, description, control, () => OpenSubPage(title, description, build)));

    // As the web interface does it: the global effect is written into every
    // tile's own, which is what the clock actually plays.
    private void ApplyTransitionToEveryTile(string value)
    {
        var values = new List<(string Key, string Value)>
        {
            ("tileTransGlobal", value), ("tileTransC2P", value), ("tileTransP2C", value),
        };
        values.AddRange(AppearanceTiles().Where(tile => tile.TransitionKey is not null).Select(tile => (tile.TransitionKey!, value)));
        QueueSave("set:tileTransGlobal", token => Post(token, "/settings", values.ToArray()), 0);
    }

    private void QueueSetting(string key, string value, int delayMs = 0) =>
        QueueSave("set:" + key, token => Post(token, "/settings", (key, value)), delayMs);

    // ------------------------------------------------------------------ which tiles

    // A tile and whether its settings mean anything right now: one that is out
    // of the Tile Manager has nothing to apply them to.
    private sealed record AppearanceRow(AppearanceTile Tile, bool Available);

    // Only the tiles in the Tile Manager, the way the web interface lists them:
    // the circuit in its order, the priority tiles in theirs, then the IP
    // address, which a touch shows and which is always there. The ones that are
    // out of it come too, faded, when the clock is set to show what it cannot use.
    private static List<(string Title, List<AppearanceRow> Tiles)> AppearanceGroups(ClockDeviceState state, Func<AppearanceTile, bool> has)
    {
        var specs = AppearanceTiles().ToDictionary(tile => tile.Key);
        var nowPlayingPriority = state.Bool("nowPlayingIsPriority");
        var showUnavailable = state.Bool("showGrayed", true);
        var circuit = new List<AppearanceRow>();
        foreach (var tile in state.Tiles())
        {
            if (tile.Id == 3 && nowPlayingPriority) continue;
            var available = TileInManager(state, Str(tile.Id));
            if (!available && !showUnavailable) continue;
            if (specs.TryGetValue(Str(tile.Id), out var spec) && has(spec)) circuit.Add(new AppearanceRow(spec, available));
        }
        var priority = new List<AppearanceRow>();
        foreach (var key in PriorityKeys(state))
        {
            var specKey = key == NowPlayingPriorityKey ? NowPlayingTileKey : key;
            if (key == NowPlayingPriorityKey && !nowPlayingPriority) continue;
            var available = TileInManager(state, specKey);
            if (!available && !showUnavailable) continue;
            if (specs.TryGetValue(specKey, out var spec) && has(spec)) priority.Add(new AppearanceRow(spec, available));
        }
        var other = specs.TryGetValue("ip", out var ip) && has(ip) ? new List<AppearanceRow> { new(ip, true) } : new List<AppearanceRow>();
        return new List<(string, List<AppearanceRow>)>
            {
                (L("Tile-uri circuit", "Circuit Tiles"), circuit),
                (L("Tile-uri prioritare", "Priority Tiles"), priority),
                (L("Altele", "Other"), other),
            }
            .Where(group => group.Item2.Count > 0)
            .ToList();
    }

    private static bool TileInManager(ClockDeviceState state, string key)
    {
        if (key == "ip") return true;
        if (int.TryParse(key, NumberStyles.Integer, Inv, out var id)) return (state.Int("tileHidden") & (1 << id)) == 0;
        return (state.Int("prioHidden") & (1 << PrioBit(key))) == 0;
    }

    // `unusable` is the second reason a row can be out of use, beside the tile
    // not being in the manager: on the speed page, a tile with no transition
    // chosen has nothing to time. Hands back how many rows were shown at all,
    // so a page that comes out empty can say why.
    private int AddAppearanceRows(
        ClockDeviceState state,
        Func<AppearanceTile, bool> has,
        Func<AppearanceTile, FrameworkElement> control,
        Func<AppearanceTile, string?>? unusable = null)
    {
        var showUnavailable = state.Bool("showGrayed", true);
        var shown = 0;
        foreach (var (title, tiles) in AppearanceGroups(state, has))
        {
            var rows = new List<(AppearanceTile Tile, string? Reason)>();
            foreach (var (tile, available) in tiles)
            {
                var reason = available
                    ? unusable?.Invoke(tile)
                    : L("Adaugă-l întâi în Manager de tile-uri.", "Add it to the Tile Manager first.");
                if (reason is not null && !showUnavailable) continue;
                rows.Add((tile, reason));
            }
            // A group with nothing left in it takes its heading with it.
            if (rows.Count == 0) continue;
            AddSection(title);
            foreach (var (tile, reason) in rows)
            {
                Add(UnusableIf(SettingsCard(TileSettingsGlyph(tile.Key), TileSettingsTitle(tile.Key), reason, control(tile)), reason));
                shown++;
            }
        }
        return shown;
    }

    // The house look for a setting that cannot be used: faded and untouchable.
    private static Border UnusableIf(Border card, string? reason)
    {
        if (reason is null) return card;
        card.Opacity = 0.45;
        card.IsHitTestVisible = false;
        return card;
    }

    // ------------------------------------------------------------------ pages

    private Action? BuildIconsPage(ClockDeviceState state)
    {
        // The low battery alert's icon is newer than the alert: an older clock has none to set.
        // A clock whose events carry an icon each has no icon for the Events tile as a whole.
        var eventIcons = state.Has("wxErr");
        AddAppearanceRows(state, tile => tile.IconState is not null && tile.IconForm is not null && state.Has(tile.IconState) &&
                                         !(eventIcons && tile.Key == "15"), tile =>
        {
            var form = tile.IconForm!;
            // MSI Afterburner's own icon follows what it shows: Info for FPS, Chip for a temperature.
            var fallback = tile.Key == "14" && state.Int("abMode") == 0 ? 43 : DefaultIconSelection(tile.Key);
            var box = IconChoiceBox(state.Int(tile.IconState!), fallback,
                tile.Key is "13" or "lowbattery" ? L("Dinamică după nivelul bateriei", "Dynamic with battery level") : null);
            box.SelectionChanged += (_, _) => QueueSetting(form, ChoiceValue(box));
            return box;
        });

        if (TileInManager(state, "4"))
        {
            AddSection(TileName(4));
            Add(SettingsExpander("", L("Iconițe meteo", "Weather icons"),
                L("Câte o iconiță pentru fiecare fel de vreme.", "One icon for each kind of weather."), null, WeatherIconRows(state)));
        }
        if (eventIcons && TileInManager(state, "15") && EventIconRows(state) is { Count: > 0 } eventRows)
        {
            AddSection(TileName(15));
            Add(SettingsExpander(TileSettingsGlyph("15"), L("Iconițe evenimente", "Event icons"),
                L("Câte o iconiță pentru fiecare eveniment. Ceasul o arată cât timp numără zilele până la el.",
                  "One icon for each event. The clock shows it while it counts the days to that event."), null, eventRows));
        }
        if (TileInManager(state, NowPlayingTileKey))
        {
            AddSection(TileName(3));
            var adaptive = Toggle(state.Bool("npAdaptiveIcon", true));
            adaptive.Toggled += (_, _) => QueueSetting("npAdaptiveIcon", adaptive.IsOn ? "1" : "0");
            var music = IconChoiceBox(state.Int("iconSelNpMusic"), 4);
            music.SelectionChanged += (_, _) => QueueSetting("iconSelNpMusic", ChoiceValue(music));
            var video = IconChoiceBox(state.Int("iconSelNpVideo"), 24);
            video.SelectionChanged += (_, _) => QueueSetting("iconSelNpVideo", ChoiceValue(video));
            Add(ToggleGroup("", L("Iconiță adaptivă", "Adaptive Icon"),
                L("Alege singură între iconița de muzică și cea de video.", "Picks between the music and the video icon on its own."), adaptive,
                [
                    SubRow(L("Iconiță pentru muzică", "Music icon"), null, music),
                    SubRow(L("Iconiță pentru video", "Video icon"), null, video),
                ]));
            // Pausing only keeps the tile up when Octoglow Connect is set to leave
            // it there, and then this is the icon it shows.
            var paused = IconChoiceBox(state.Int("iconSelNpPaused"), 19);
            paused.SelectionChanged += (_, _) => QueueSetting("iconSelNpPaused", ChoiceValue(paused));
            Add(SettingsCard("", L("Muzică în pauză", "Paused music"),
                L("Iconița afișată cât timp redarea e oprită, dacă tile-ul rămâne pe ecran.",
                  "The icon shown while playback is held, if the tile stays on screen."), paused));
        }
        return null;
    }

    // Only the events that exist, by name. The clock takes its events whole,
    // so a new icon goes out with the whole list.
    private List<UIElement> EventIconRows(ClockDeviceState state)
    {
        var events = state.Objects("cdEvents")
            .Select(e => (Name: e.String("n"), Date: e.String("d"), Yearly: e.Bool("y"), Icon: e.Int("i")))
            .ToList();
        var rows = new List<UIElement>();
        for (var index = 0; index < events.Count; index++)
        {
            var at = index;
            var box = IconChoiceBox(events[at].Icon, DefaultIconSelection("15"));
            box.SelectionChanged += (_, _) =>
            {
                _ = int.TryParse(ChoiceValue(box), NumberStyles.Integer, Inv, out var icon);
                events[at] = events[at] with { Icon = icon };
                var values = events.SelectMany((e, i) => new (string Key, string Value)[]
                {
                    ("n" + Str(i), e.Name), ("d" + Str(i), e.Date), ("y" + Str(i), e.Yearly ? "1" : "0"), ("i" + Str(i), Str(e.Icon)),
                }).ToArray();
                QueueSave("countdown", token => Post(token, "/countdownsett", values), 0);
            };
            rows.Add(SubRow(events[at].Name, null, box));
        }
        return rows;
    }

    private List<UIElement> WeatherIconRows(ClockDeviceState state)
    {
        var rows = new List<UIElement>();
        foreach (var (key, label, fallback) in new[]
                 {
                     ("iconWxSunny", L("Senin (zi)", "Clear (day)"), 9),
                     ("iconWxNight", L("Senin (noapte)", "Clear (night)"), 15),
                     ("iconWxCloud", L("Înnorat", "Cloudy"), 10),
                     ("iconWxRain", L("Ploaie", "Rain"), 11),
                     ("iconWxStorm", L("Furtună", "Storm"), 12),
                     ("iconWxSnow", L("Ninsoare", "Snow"), 13),
                     ("iconWxWind", L("Vânt", "Wind"), 14),
                 })
        {
            var box = IconChoiceBox(state.Int(key), fallback);
            box.SelectionChanged += (_, _) => QueueSetting(key, ChoiceValue(box));
            rows.Add(SubRow(label, null, box));
        }
        return rows;
    }

    private Action? BuildHideIconsPage(ClockDeviceState state)
    {
        AddAppearanceRows(state, tile => tile.HideKey is not null, tile =>
        {
            var key = tile.HideKey!;
            var toggle = Toggle(state.Bool(key));
            toggle.Toggled += (_, _) => QueueSetting(key, toggle.IsOn ? "1" : "0");
            return toggle;
        });
        return null;
    }

    private Action? BuildScrollTypePage(ClockDeviceState state)
    {
        // Speed belongs with the kind of scroll, the way the web interface has
        // it: the overall pace here, each tile's own a page deeper.
        AddSection(L("Viteză scroll", "Scroll Speed"));
        var speed = SpeedSlider(10, 120, 5, state.Int("scrollSpeed", 40));
        speed.ValueChanged += (_, args) => QueueSetting("scrollSpeed", Str((int)Math.Round(args.NewValue)), 300);
        Add(NavCard("", L("Viteză scroll", "Scroll Speed"),
            L("Cât de repede se derulează textul", "How fast the text scrolls"),
            SliderWithValue(speed, Milliseconds),
            () => OpenSubPage(L("Viteză scroll", "Scroll Speed"),
                L("Cât de repede se derulează textul fiecărui tile.", "How fast each tile's text scrolls."), BuildScrollSpeedPage)));

        AddAppearanceRows(state, tile => tile.ScrollKey is not null, tile =>
        {
            var key = tile.ScrollKey!;
            var box = ChoiceBox(ScrollChoices(), Str(state.Int(key)));
            box.SelectionChanged += (_, _) =>
            {
                // The timer's scroll type lives with the rest of the timer, not in /settings.
                if (tile.ScrollViaTimer)
                    QueueSave("timer-scroll", token => Post(token, "/timersett", ("scrollType", ChoiceValue(box))), 0);
                else
                    QueueSetting(key, ChoiceValue(box));
            };
            // The preview moves the tile's own icon and font at the tile's own pace.
            var icon = tile.IconState is not null && state.Int(tile.IconState) > 0 ? state.Int(tile.IconState) : DefaultIconSelection(tile.Key);
            return EffectPicker(box, TileSettingsTitle(tile.Key), false, new EffectLook(
                StepMs: tile.SpeedKey is not null ? state.Int(tile.SpeedKey, 40) : state.Int("scrollSpeed", 40),
                Font: tile.FontKey is not null && state.Has(tile.FontKey) ? state.Int(tile.FontKey) : state.Int("fontType"),
                Icon: icon > 0 ? icon : 4));
        });
        return null;
    }

    private static string Milliseconds(double value) => $"{Math.Round(value)} ms";

    private Action? BuildScrollSpeedPage(ClockDeviceState state)
    {
        AddAppearanceRows(state, tile => tile.SpeedKey is not null, tile =>
        {
            var key = tile.SpeedKey!;
            var slider = SpeedSlider(10, 120, 5, state.Int(key, 40));
            slider.ValueChanged += (_, args) => QueueSetting(key, Str((int)Math.Round(args.NewValue)), 300);
            return SliderWithValue(slider, Milliseconds);
        });
        return null;
    }

    private Action? BuildFontPage(ClockDeviceState state)
    {
        AddAppearanceRows(state, tile => tile.FontKey is not null && state.Has(tile.FontKey), tile =>
        {
            var key = tile.FontKey!;
            var box = ChoiceBox(FontChoices(), Str(state.Int(key)));
            box.SelectionChanged += (_, _) => QueueSetting(key, ChoiceValue(box));
            return FontPicker(box, TileSettingsTitle(tile.Key));
        });
        return null;
    }

    private Action? BuildTransitionPage(ClockDeviceState state)
    {
        AddSection(L("Viteză tranziție", "Transition Speed"));
        var speed = SpeedSlider(50, 250, 25, state.Int("tileTransSpd", 100));
        speed.ValueChanged += (_, args) => QueueSetting("tileTransSpd", Str((int)Math.Round(args.NewValue)), 300);
        Add(NavCard("", L("Viteză tranziție tile", "Tile Transition Speed"),
            L("Cât de repede trece un tile în altul", "How quickly one tile gives way to the next"),
            SliderWithValue(speed, Pace),
            () => OpenSubPage(L("Viteză tranziție tile", "Tile Transition Speed"),
                L("Cât de repede trece fiecare tile în altul.", "How quickly each tile turns into the next."), BuildTransitionSpeedPage)));

        // Each picker with its preview, played at the pace that row is set to.
        FrameworkElement TransitionBox(string key, string title, string? speedKey)
        {
            var box = ChoiceBox(TransitionChoices(), Str(state.Int(key)));
            box.SelectionChanged += (_, _) => QueueSetting(key, ChoiceValue(box));
            return EffectPicker(box, title, true, new EffectLook(
                Pace: speedKey is not null && state.Has(speedKey) ? state.Int(speedKey, 100) : state.Int("tileTransSpd", 100),
                Font: state.Int("fontType")));
        }
        AddAppearanceRows(state, tile => tile.TransitionKey is not null,
            tile => TransitionBox(tile.TransitionKey!, TileSettingsTitle(tile.Key), tile.TransitionSpeedKey));

        AddSection(L("Tranziții speciale", "Special Transitions"));
        Add(SettingsCard("", L("Din circuit în prioritar", "Circuit to priority"),
            L("Când apare un tile prioritar.", "When a priority tile appears."),
            TransitionBox("tileTransC2P", L("Din circuit în prioritar", "Circuit to priority"), "tileTransSpdC2P")));
        Add(SettingsCard("", L("Din prioritar în circuit", "Priority to circuit"),
            L("Când un tile prioritar dispare.", "When a priority tile goes away."),
            TransitionBox("tileTransP2C", L("Din prioritar în circuit", "Priority to circuit"), "tileTransSpdP2C")));
        return null;
    }

    // 125 reads as 1,25× (1.25× in English): the pace as a multiple of normal.
    private static string Pace(double value) =>
        (Math.Round(value) / 100).ToString("0.##", AppStrings.IsEnglish ? Inv : CultureInfo.GetCultureInfo("ro-RO")) + "×";

    private Action? BuildTransitionSpeedPage(ClockDeviceState state)
    {
        Slider PaceSlider(string key)
        {
            var slider = SpeedSlider(50, 250, 25, state.Int(key, 100));
            slider.ValueChanged += (_, args) => QueueSetting(key, Str((int)Math.Round(args.NewValue)), 300);
            return slider;
        }

        // Nothing to time where nothing happens: a tile left on "Fără" says so
        // instead of offering a speed that changes nothing.
        var noEffect = L("Alege o tranziție în Tranziție.", "Choose a transition in Transition first.");
        var showUnavailable = state.Bool("showGrayed", true);
        var shown = AddAppearanceRows(state, tile => tile.TransitionSpeedKey is not null,
            tile => SliderWithValue(PaceSlider(tile.TransitionSpeedKey!), Pace),
            tile => state.Int(tile.TransitionKey!) == 0 ? noEffect : null);

        var between = new (string Title, string Effect, string Speed)[]
        {
            (L("Din circuit în prioritar", "Circuit to priority"), "tileTransC2P", "tileTransSpdC2P"),
            (L("Din prioritar în circuit", "Priority to circuit"), "tileTransP2C", "tileTransSpdP2C"),
        };
        var group = between
            .Select(row => (row.Title, row.Speed, Reason: state.Int(row.Effect) == 0 ? noEffect : null))
            .Where(row => row.Reason is null || showUnavailable)
            .ToList();
        if (group.Count > 0)
        {
            AddSection(L("Tranziții speciale", "Special Transitions"));
            foreach (var (title, speed, reason) in group)
            {
                Add(UnusableIf(SettingsCard("", title, reason, SliderWithValue(PaceSlider(speed), Pace)), reason));
                shown++;
            }
        }

        if (shown == 0)
        {
            var empty = Secondary(L("Niciun tile nu are o tranziție aleasă, deci nu e nicio viteză de reglat. Alege una în Tranziție.",
                "No tile has a transition chosen, so there is no speed to set. Choose one in Transition."));
            empty.Margin = new Thickness(2, 4, 0, 0);
            Add(empty);
        }
        return null;
    }

    private static Slider SpeedSlider(double minimum, double maximum, double step, double value)
    {
        var slider = NewSlider(minimum, maximum, value, 200);
        slider.StepFrequency = step;
        slider.SmallChange = step;
        return slider;
    }
}
