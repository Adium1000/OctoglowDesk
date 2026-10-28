using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace OctoglowSender;

// What each circuit tile shows, as set from its settings dialog.
public sealed partial class ClockNativeSettingsPage
{
    private void BuildHourContent(ClockDeviceState state)
    {
        var format = ChoiceBox([("24h (13:45)", "0"), ("12h (1:45)", "1")], Str(state.Int("hourformat")));
        format.SelectionChanged += (_, _) => QueueSave("hourformat", token => Post(token, "/hourformat", ("fmt", ChoiceValue(format))), 0);
        Add(SettingsCard("", L("Format afișare", "Display format"), L("Ceas de 12 sau de 24 de ore.", "12- or 24-hour clock."), format));

        var leadingZero = Toggle(state.Bool("hourLeadingZero"));
        leadingZero.Toggled += (_, _) => QueueSave("hourlead", token => Post(token, "/hourformat", ("leadzero", leadingZero.IsOn ? "1" : "0")), 0);
        Add(SettingsCard("", L("Zero în față", "Leading zero"), L("Afișează 02:30 în loc de 2:30.", "Shows 02:30 instead of 2:30."), leadingZero));
    }

    private void BuildHourWeekContent(ClockDeviceState state)
    {
        var hasContent = state.Has("hwContent");
        var skin = ChoiceBox(
            [("Compact", "0"), (L("Pătrat", "Squarish"), "1")],
            state.Int("hwSkin") == 1 ? "1" : "0", 210);
        skin.IsEnabled = state.Has("hwSkin");
        Add(SettingsCard("\uE787", L("Aspectul cadranului", "Watchface skin"),
            state.Has("hwSkin")
                ? L("Alege aspectul cadranului.", "Choose the watchface layout.")
                : L("Actualizează firmware-ul ceasului pentru celelalte aspecte.", "Update the clock firmware to use the other skins."), skin));

        var choices = new List<(string, string)>
        {
            (L("Ziua săptămânii", "Weekday"), "0"),
            (L("Temperatura senzorului", "Sensor temperature"), "1"),
        };
        if (hasContent) choices.Add((L("Temperatura meteo", "Weather temperature"), "2"));
        var content = ChoiceBox(choices, Str(state.Int("hwContent", state.Int("hwSkin") == 2 ? 1 : 0)), 210);
        content.IsEnabled = state.Has("hwSkin");
        var contentCard = SettingsCard("\uE9CE", L("Lângă oră", "Beside the time"),
            hasContent
                ? L("Alege o singură informație. Temperatura meteo folosește localitatea și cheia API din setările Meteo.", "Choose one item. Weather temperature uses the location and API key from Weather settings.")
                : L("Actualizează firmware-ul pentru temperatura meteo.", "Update the firmware to use weather temperature."), content);
        Add(contentCard);

        var format = ChoiceBox([("24h (13:45)", "0"), ("12h (1:45)", "1")], Str(state.Int("hwFormat")));
        format.SelectionChanged += (_, _) => QueueSave("hwfmt", token => Post(token, "/hwsett", ("fmt", ChoiceValue(format))), 0);
        Add(SettingsCard("", L("Format afișare", "Display format"), L("Ceas de 12 sau de 24 de ore.", "12- or 24-hour clock."), format));

        var leadingZero = Toggle(state.Bool("hwLeadZero"));
        leadingZero.Toggled += (_, _) => QueueSave("hwlead", token => Post(token, "/hwsett", ("leadzero", leadingZero.IsOn ? "1" : "0")), 0);
        Add(SettingsCard("", L("Zero în față", "Leading zero"), L("Afișează 02:30 în loc de 2:30.", "Shows 02:30 instead of 2:30."), leadingZero));

        var swap = Toggle(state.Bool("hwSwap"));
        swap.Toggled += (_, _) => QueueSave("hwswap", token => Post(token, "/hwsett", ("swap", swap.IsOn ? "1" : "0")), 0);
        var swapCard = SettingsCard("", L("Inversează ordinea", "Swap order"),
            L("Arată ziua sau temperatura în stânga orei.", "Show the weekday or temperature to the left of the time."), swap);
        Add(swapCard);

        var barMode = ChoiceBox(
            [(L("Linie fixă", "Solid line"), "0"), (L("Progresul secundelor", "Seconds progress"), "1")],
            Str(state.Int("hwBarMode", 1)), 210);
        barMode.SelectionChanged += (_, _) => QueueSave("hwbar", token => Post(token, "/hwsett", ("barmode", ChoiceValue(barMode))), 0);
        var barCard = SettingsCard("\uE9D9", L("Bară de progres", "Progress bar"),
            L("O linie fixă sau secundele care umplu bara.", "A solid line or a bar that fills during each minute."), barMode);
        Add(barCard);

        var barPosition = ChoiceBox(
            [(L("Dedesubt", "Below"), "0"), (L("Deasupra", "Above"), "1")],
            Str(state.Int("hwBarPos")), 210);
        barPosition.SelectionChanged += (_, _) => QueueSave("hwbarpos", token => Post(token, "/hwsett", ("barpos", ChoiceValue(barPosition))), 0);
        var positionCard = SettingsCard("\uE8CB", L("Poziția barei", "Bar position"),
            L("Așază bara sub sau deasupra valorii din lateral.", "Place the bar below or above the value beside the time."), barPosition);
        Add(positionCard);

        var unit = ChoiceBox([("°C Celsius", "0"), ("°F Fahrenheit", "1")], Str(state.Int("tempunit")), 210);
        unit.SelectionChanged += (_, _) => QueueSave("hwtempunit", token => Post(token, "/tempunit", ("unit", ChoiceValue(unit))), 0);
        var unitCard = SettingsCard("\uE9CA", L("Unitatea temperaturii", "Temperature unit"),
            L("Aceeași unitate este folosită și de tile-ul Temperatura.", "The Temperature tile uses this unit too."), unit);
        Add(unitCard);

        void UpdateSkin(bool animate)
        {
            var seconds = ChoiceValue(skin) == "1";
            var temperature = ChoiceValue(content) != "0";
            SetExpanded(contentCard, !seconds, animate);
            SetExpanded(swapCard, !seconds, animate);
            SetExpanded(barCard, !seconds && !temperature, animate);
            SetExpanded(positionCard, !seconds && !temperature, animate);
            SetExpanded(unitCard, !seconds && temperature, animate);
        }
        void SaveSkin()
        {
            // Send the pair as one queued update, so a quick skin/source change cannot
            // leave the clock with half of the selection. Older firmware uses skin 2.
            var selectedSkin = ChoiceValue(skin);
            var selectedContent = ChoiceValue(content);
            QueueSave("hwskin", token => hasContent
                ? Post(token, "/hwsett", ("skin", selectedSkin), ("content", selectedContent))
                : Post(token, "/hwsett", ("skin", selectedSkin == "1" ? "1" : selectedContent == "1" ? "2" : "0")), 0);
        }
        UpdateSkin(false);
        skin.SelectionChanged += (_, _) =>
        {
            UpdateSkin(true);
            SaveSkin();
        };
        content.SelectionChanged += (_, _) =>
        {
            UpdateSkin(true);
            SaveSkin();
        };
    }

    private void BuildDateContent(ClockDeviceState state)
    {
        var format = ChoiceBox(
            [
                ("2026.06.18", "0"), ("26.06.18", "1"), ("18.06.2026", "2"), ("18.06", "3"),
                (L("Ziua și data (Joi 18)", "Weekday and day (Thu 18)"), "4"), (L("Personalizat", "Custom"), "5"),
            ],
            Str(state.Int("dateformat")));
        Add(SettingsCard("\uE787", L("Format afișare", "Display format"), L("Cum arată data pe ecran.", "How the date looks on screen."), format));

        // The clock keeps the pattern in capitals and names the weekday in its date language.
        var savedPattern = state.String("customdatefmt", "DD.MM.YYYY").Trim().ToUpperInvariant();
        var romanian = state.Int("datelang") == 1;
        var pattern = new TextBox
        {
            Text = savedPattern,
            MaxLength = 20,
            PlaceholderText = "DD.MM.YYYY",
            CharacterCasing = CharacterCasing.Upper,
        };
        var preview = new TextBlock { Style = Resource<Style>("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap };
        var custom = SaveFieldCard("\uE70F", L("Format personalizat", "Custom Format"),
            L("Scrie formatul din literele de mai jos; ceasul pune data de azi în locul lor.", "Build the format from the letters below; the clock puts today's date in their place."),
            pattern,
            () => DatePatternError(pattern.Text) is null && pattern.Text.Trim().ToUpperInvariant() != savedPattern,
            () =>
            {
                var value = pattern.Text.Trim().ToUpperInvariant();
                QueueSave("dateformat", token => Post(token, "/datecustomfmt", ("pattern", value)), 0);
                savedPattern = value;
                return Task.CompletedTask;
            },
            preview, DatePatternLegend());
        void UpdatePreview()
        {
            var error = DatePatternError(pattern.Text);
            preview.Text = error ?? L("Previzualizare: ", "Preview: ") + DatePatternPreview(pattern.Text.Trim().ToUpperInvariant(), DateTime.Now, romanian);
            preview.Opacity = error is null ? 1 : 0.72;
            custom.Refresh();
        }
        pattern.TextChanged += (_, _) => UpdatePreview();
        OnEnter(pattern, custom.Submit);
        UpdatePreview();
        // The pattern only means something with the Custom format, so it only shows then.
        SetExpanded(custom.Card, ChoiceValue(format) == "5", false);
        Add(custom.Card);

        format.SelectionChanged += (_, _) =>
        {
            var value = ChoiceValue(format);
            SetExpanded(custom.Card, value == "5");
            // Custom goes back to the saved pattern; the other formats are a number.
            if (value == "5")
                QueueSave("dateformat", token => Post(token, "/datecustomfmt", ("pattern", savedPattern)), 0);
            else
                QueueSave("dateformat", token => Post(token, "/dateformat", ("fmt", value)), 0);
        };
    }

    // The same rules the clock checks a pattern against (Octoglow.ino,
    // dateValidateCustomFormat), so a pattern it would refuse can't be saved.
    private static string? DatePatternError(string text)
    {
        var pattern = text.Trim().ToUpperInvariant();
        if (pattern.Length == 0) return L("Scrie un format.", "Enter a format.");
        if (pattern.Length > 20) return L("Formatul are cel mult 20 de caractere.", "The format has at most 20 characters.");
        var hasContent = false;
        for (var i = 0; i < pattern.Length;)
        {
            var letter = pattern[i];
            var end = i;
            while (end < pattern.Length && pattern[end] == letter) end++;
            var count = end - i;
            switch (letter)
            {
                case 'Y':
                    if (count is not (2 or 4)) return L("Anul se scrie YY sau YYYY.", "The year is written YY or YYYY.");
                    hasContent = true;
                    break;
                case 'M':
                    if (count != 2) return L("Luna se scrie MM.", "The month is written MM.");
                    hasContent = true;
                    break;
                case 'D':
                    if (count != 2) return L("Ziua se scrie DD.", "The day is written DD.");
                    hasContent = true;
                    break;
                case 'W':
                    hasContent = true;
                    break;
                case '/' or '.' or '(' or ')' or ' ':
                    break;
                default:
                    return L($"„{letter}” nu e una dintre literele sau separatorii de mai jos.", $"\"{letter}\" isn't one of the letters or separators below.");
            }
            i = end;
        }
        return hasContent ? null : L("Formatul are nevoie de cel puțin Y, M, D sau W.", "The format needs at least Y, M, D or W.");
    }

    // What the clock would show for a valid pattern (Octoglow.ino, dateRenderCustom).
    private static string DatePatternPreview(string pattern, DateTime date, bool romanian)
    {
        string[] weekdays = romanian
            ? ["Duminica", "Luni", "Marti", "Miercuri", "Joi", "Vineri", "Sambata"]
            : ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length;)
        {
            var letter = pattern[i];
            var end = i;
            while (end < pattern.Length && pattern[end] == letter) end++;
            var count = end - i;
            switch (letter)
            {
                case 'Y':
                    text.Append(count == 2 ? (date.Year % 100).ToString("00", Inv) : date.Year.ToString("0000", Inv));
                    break;
                case 'M':
                    text.Append(date.Month.ToString("00", Inv));
                    break;
                case 'D':
                    text.Append(date.Day.ToString("00", Inv));
                    break;
                case 'W':
                    var name = weekdays[(int)date.DayOfWeek];
                    text.Append(count >= name.Length ? name : name[..count]);
                    break;
                default:
                    text.Append(pattern, i, count);
                    break;
            }
            i = end;
        }
        return text.ToString();
    }

    // What each letter of a custom pattern stands for.
    private static Grid DatePatternLegend()
    {
        var rows = new (string Letters, string Meaning)[]
        {
            ("YY, YYYY", L("anul, cu 2 sau 4 cifre", "the year, in 2 or 4 digits")),
            ("MM", L("luna, cu 2 cifre", "the month, in 2 digits")),
            ("DD", L("ziua, cu 2 cifre", "the day, in 2 digits")),
            ("W, WW, WWW…", L("ziua săptămânii: atâtea litere din nume câte W pui; destule W arată numele întreg", "the weekday: as many letters of its name as there are Ws; enough Ws show the whole name")),
            ("/  .  (  )", L("separatori, la fel ca spațiul", "separators, as is a space")),
        };
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < rows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var letters = new TextBlock
            {
                Text = rows[row].Letters,
                Style = Resource<Style>("CaptionTextBlockStyle"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            Grid.SetRow(letters, row);
            grid.Children.Add(letters);
            var meaning = Secondary(rows[row].Meaning);
            Grid.SetRow(meaning, row);
            Grid.SetColumn(meaning, 1);
            grid.Children.Add(meaning);
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var examples = Secondary(L("Exemple: DD.MM.YYYY · DD/MM/YY · (WWW) DD.MM · WWWWWWWW", "Examples: DD.MM.YYYY · DD/MM/YY · (WWW) DD.MM · WWWWWWWW"));
        examples.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(examples, rows.Length);
        Grid.SetColumnSpan(examples, 2);
        grid.Children.Add(examples);
        return grid;
    }

    private void BuildTemperatureContent(ClockDeviceState state)
    {
        var unit = ChoiceBox([("°C Celsius", "0"), ("°F Fahrenheit", "1")], Str(state.Int("tempunit")));
        unit.SelectionChanged += (_, _) => QueueSave("tempunit", token => Post(token, "/tempunit", ("unit", ChoiceValue(unit))), 0);
        Add(SettingsCard("", L("Unitate de măsură", "Unit of measurement"), L("Grade Celsius sau Fahrenheit.", "Degrees Celsius or Fahrenheit."), unit));
    }

    // Which of the three values the tile shows; degrees in the unit the
    // Temperature and Weather tiles use, which only matters for a temperature.
    private void BuildAfterburnerContent(ClockDeviceState state)
    {
        var mode = ChoiceBox([("FPS", "0"), (L("Temperatura CPU", "CPU temperature"), "1"), (L("Temperatura GPU", "GPU temperature"), "2")],
            Str(state.Int("abMode")));
        Add(SettingsCard("", L("Valoare afișată", "Value shown"),
            L("Ce arată tile-ul pe matrice. Cât timp valoarea lipsește, tile-ul nu apare.", "What the tile shows on the matrix. While the value is missing, the tile isn't shown."), mode));
        var unit = ChoiceBox([("°C Celsius", "0"), ("°F Fahrenheit", "1")], Str(state.Int("tempunit")));
        unit.SelectionChanged += (_, _) => QueueSave("tempunit", token => Post(token, "/tempunit", ("unit", ChoiceValue(unit))), 0);
        var unitCard = SettingsCard("", L("Unitate de măsură", "Unit of measurement"),
            L("Aceeași ca la tile-urile Temperatură și Meteo.", "The same as the Temperature and Weather tiles'."), unit);
        SetExpanded(unitCard, ChoiceValue(mode) != "0");
        mode.SelectionChanged += (_, _) =>
        {
            QueueSave("abmode", token => Post(token, "/abmode", ("mode", ChoiceValue(mode))), 0);
            SetExpanded(unitCard, ChoiceValue(mode) != "0");
        };
        Add(unitCard);
    }

    private void BuildNowPlayingContent(ClockDeviceState state)
    {
        var mode = ChoiceBox(
            [(L("Artist și titlu", "Artist and title"), "0"), (L("Doar artistul", "Artist only"), "1"), (L("Doar titlul", "Title only"), "2")],
            Str(_nowPlayingState?.Int("npmode") ?? 0));
        mode.SelectionChanged += (_, _) => QueueSave("npmode", token => Post(token, "/npmode", ("mode", ChoiceValue(mode))), 0);
        Add(SettingsCard("", L("Text afișat", "Text shown"), L("Ce scrie pe matrice despre piesa care rulează.", "What the matrix says about what is playing."), mode));
    }

    private void BuildScreenSaverContent(ClockDeviceState state)
    {
        // The numbers are the clock's, and what it has depends on its firmware:
        // the one that reports "webAlNet" has Bricks and Runner and neither
        // Meteor Shower (4) nor Game of Life (6); the one before it, which
        // reports "cdFull", has Game of Life, Snake and Helix; older ones the
        // first six.
        (string Label, string Value)[] animations = state.Has("webAlNet")
            ? [(L("Aleatoriu", "Random"), "0"), ("Pong", "1"), (L("Artificii", "Fireworks"), "2"), (L("Egalizator", "Equalizer"), "3"),
               (L("Ploaie digitală", "Digital Rain"), "5"), (L("Șarpe", "Snake"), "7"), (L("Elice", "Helix"), "8"),
               (L("Cărămizi", "Bricks"), "9"), (L("Alergător", "Runner"), "10")]
            : state.Has("cdFull")
            ? [(L("Aleatoriu", "Random"), "0"), ("Pong", "1"), (L("Artificii", "Fireworks"), "2"), (L("Egalizator", "Equalizer"), "3"),
               (L("Ploaie digitală", "Digital Rain"), "5"), (L("Jocul vieții", "Game of Life"), "6"), (L("Șarpe", "Snake"), "7"),
               (L("Elice", "Helix"), "8")]
            : [(L("Aleatoriu", "Random"), "0"), ("Pong", "1"), (L("Artificii", "Fireworks"), "2"), (L("Egalizator", "Equalizer"), "3"),
               (L("Ploaie de meteori", "Meteor Shower"), "4"), (L("Ploaie digitală", "Digital Rain"), "5")];
        var saved = Str(state.Int("ssAnim"));
        var animation = ChoiceBox(animations, animations.Any(a => a.Value == saved) ? saved : "0");
        animation.SelectionChanged += (_, _) => QueueSave("ssanim", token => Post(token, "/screensaversett", ("anim", ChoiceValue(animation))), 0);
        Add(SettingsCard("", L("Animație", "Animation"), L("Ce rulează pe ecran.", "What plays on the screen."), animation));
    }

    // What the Alerts tile announces, each with a switch, as in the web
    // interface's dialog; the tile's own switch turns them off together.
    private void BuildWebAlertsContent(ClockDeviceState state)
    {
        void Alert(string stateKey, string arg, string title, string description)
        {
            var toggle = Toggle(state.Bool(stateKey, true));
            toggle.Toggled += (_, _) => QueueSave("web-alert-" + arg,
                token => Post(token, "/websett", (arg, toggle.IsOn ? "1" : "0")), 0);
            Add(SettingsCard(arg switch { "access" => "\uE774", "login" => "\uE77B", "locked" => "\uE72E", _ => "\uE871" }, title, description, toggle));
        }

        Alert("webAlAccess", "access", L("Acces la interfața web", "Web interface access"),
            L("Când interfața e deschisă într-un browser", "When the interface is opened in a browser"));
        Alert("webAlLogin", "login", L("Logare nouă", "New login"),
            L("Când cineva se conectează dintr-un browser", "When someone signs in from a browser"));
        Alert("webAlLocked", "locked", L("Logare blocată", "Login blocked"),
            L("Când prea multe parole greșite închid logarea", "When too many wrong passwords close the sign-in"));
        if (state.Has("webAlNet"))
            Alert("webAlNet", "net", L("Fără internet", "No internet"),
                L("Când ceasul pierde conexiunea WiFi", "When the clock loses its WiFi connection"));
    }

    private void BuildMementoContent(ClockDeviceState state)
    {
        var savedMemento = state.String("mementoText").Trim();
        var memento = new TextBox
        {
            Text = savedMemento,
            MaxLength = 120,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = L("Textul care derulează pe ceas", "The text that scrolls on the clock"),
        };
        var field = SaveFieldCard("\uE70B", L("Mesaj", "Message"),
            L("Un mesaj scurt, de cel mult 120 de caractere, care derulează pe ceas.", "A short message of up to 120 characters that scrolls on the clock."),
            memento,
            () => memento.Text.Trim() != savedMemento,
            () =>
            {
                var value = memento.Text.Trim();
                QueueSave("memento", token => Post(token, "/mementosett", ("text", value)), 0);
                savedMemento = value;
                return Task.CompletedTask;
            });
        memento.TextChanged += (_, _) => field.Refresh();
        OnEnter(memento, field.Submit);
        Add(field.Card);
    }

    // The countdown's events, as the web interface's dialog has them: up to five,
    // each a name, a date and "every year"; the clock counts to the nearest.
    // Every change goes out at once (the whole list), like the rest of the dialog.
    private sealed class CountdownRow
    {
        public string Name = "";
        public DateTimeOffset? Date;
        public bool Yearly = true;
        // The event's icon (Icon Settings); kept here so saving the list keeps it.
        public int Icon;
    }

    private void BuildCountdownContent(ClockDeviceState state)
    {
        var rows = state.Objects("cdEvents").Select(e => new CountdownRow
        {
            Name = e.String("n"),
            Date = DateTime.TryParseExact(e.String("d"), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? new DateTimeOffset(d) : null,
            Yearly = e.Bool("y"),
            Icon = e.Int("i"),
        }).ToList();
        if (state.Has("cdFull"))
        {
            var full = Toggle(state.Bool("cdFull"));
            full.Toggled += (_, _) => QueueSave("countdown-full",
                token => Post(token, "/countdownopt", ("full", full.IsOn ? "1" : "0")), 0);
            Add(SettingsCard("\uE8D2", L("Unitate scrisă complet", "Unit written out"),
                L("De exemplu, „89 de zile” în loc de „89z”.", "For example, “89 days” instead of “89d”."), full));
        }
        AddSection(L("Evenimente", "Events"),
            L("Adaugă până la 5 evenimente. Ceasul arată câte zile mai sunt până la cel mai apropiat.",
              "Add up to 5 events. The clock shows the days left until the nearest one."));
        var list = new StackPanel
        {
            Spacing = 8,
        };
        void EnableListAnimation() => list.ChildrenTransitions = AnimationsEnabled
            ? new TransitionCollection { new AddDeleteThemeTransition() } : null;
        var add = ActionButton(L("Adăugați un eveniment", "Add an event"), "\uE710");
        UIElement? emptyState = null;
        void Save()
        {
            // Keep incomplete edits local; never replace saved events with a half-written row.
            if (rows.Any(row => string.IsNullOrWhiteSpace(row.Name) || row.Date is null)) return;
            var values = rows.SelectMany((row, i) => new (string Key, string Value)[]
            {
                ("n" + Str(i), row.Name.Trim()),
                ("d" + Str(i), row.Date!.Value.ToString("yyyy-MM-dd", Inv)),
                ("y" + Str(i), row.Yearly ? "1" : "0"),
                ("i" + Str(i), Str(row.Icon)),
            }).ToArray();
            QueueSave("countdown", token => Post(token, "/countdownsett", values), 600);
        }
        void AddEventRow(CountdownRow row, bool expanded)
        {
            if (emptyState is not null) { list.Children.Remove(emptyState); emptyState = null; }
            var expander = SettingsExpander("\uE787",
                string.IsNullOrWhiteSpace(row.Name) ? L("Eveniment nou", "New event") : row.Name,
                row.Date?.ToString("dd.MM.yyyy", Inv) ?? L("Alegeți data", "Pick the date"), null, [], expanded);
            var headerText = ((Grid)expander.Header).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().ToArray();
            var editorBuilt = false;
            void EnsureEditor()
            {
                if (editorBuilt) return;
                editorBuilt = true;
                // Existing events start as lightweight headers. Construct the calendar
                // and editable fields only if their row is opened.
                var name = new TextBox { Text = row.Name, MaxLength = 23, PlaceholderText = L("Numele evenimentului", "Event name") };
                AutomationProperties.SetName(name, L("Numele evenimentului", "Event name"));
                var date = new CalendarDatePicker
                {
                    Date = row.Date, PlaceholderText = L("Alegeți data", "Pick the date"),
                    MinWidth = 210, HorizontalAlignment = HorizontalAlignment.Right,
                };
                var yearly = new CheckBox { Content = L("Anual", "Yearly"), IsChecked = row.Yearly };
                var remove = ActionButton(L("Ștergeți evenimentul", "Delete the event"), "\uE74D");
                var hint = Secondary(L("Completează numele și data pentru a salva evenimentul.", "Enter a name and date to save the event."));
                var body = (StackPanel)expander.Content;
                void AddEditorRow(UIElement child)
                {
                    if (body.Children.Count > 0) body.Children.Add(Divider());
                    body.Children.Add(child);
                }
                AddEditorRow(SubRow(L("Nume", "Name"), null, name));
                AddEditorRow(SubRow(L("Data", "Date"), null, date));
                AddEditorRow(SubRow(L("Repetare", "Repeat"), null, yearly));
                AddEditorRow(SubBlock(null, null, hint));
                AddEditorRow(SubRow("", null, remove));
                void Refresh()
                {
                    headerText[0].Text = string.IsNullOrWhiteSpace(row.Name) ? L("Eveniment nou", "New event") : row.Name;
                    headerText[1].Text = row.Date?.ToString("dd.MM.yyyy", Inv) ?? L("Alegeți data", "Pick the date");
                    hint.Visibility = string.IsNullOrWhiteSpace(row.Name) || row.Date is null ? Visibility.Visible : Visibility.Collapsed;
                }
                name.TextChanged += (_, _) => { row.Name = name.Text; Refresh(); Save(); };
                date.DateChanged += (_, args) => { row.Date = args.NewDate; Refresh(); Save(); };
                yearly.Click += (_, _) => { row.Yearly = yearly.IsChecked == true; Save(); };
                remove.Click += (_, _) =>
                {
                    EnableListAnimation();
                    rows.Remove(row);
                    list.Children.Remove(expander);
                    if (rows.Count == 0) ShowEmpty();
                    add.IsEnabled = true;
                    Save();
                };
                Refresh();
            }
            expander.Expanding += (_, _) => EnsureEditor();
            if (expanded) EnsureEditor();
            list.Children.Add(expander);
        }
        void ShowEmpty()
        {
            emptyState = SettingsCard("\uE787", L("Niciun eveniment", "No events yet"),
                L("Adaugă o aniversare sau o dată importantă.", "Add a birthday or an important date."));
            list.Children.Add(emptyState);
        }
        add.Click += (_, _) =>
        {
            if (rows.Count >= 5) return;
            EnableListAnimation();
            var row = new CountdownRow();
            rows.Add(row);
            AddEventRow(row, expanded: true);
            add.IsEnabled = rows.Count < 5;
        };
        if (rows.Count == 0) ShowEmpty();
        else foreach (var row in rows) AddEventRow(row, expanded: false);
        add.IsEnabled = rows.Count < 5;
        Add(list);
        Add(add);
    }

    // A place from the clock's location search (OpenWeatherMap geocoding).
    private sealed class WeatherPlace
    {
        public required string Name { get; init; }
        public required string Label { get; init; }
        public required double Latitude { get; init; }
        public required double Longitude { get; init; }
        public override string ToString() => Label;
    }

    private void BuildWeatherContent(ClockDeviceState state)
    {
        var savedKey = _weatherKey;
        var hasKey = _weatherState?.Bool("hasKey") ?? state.Bool("wxHasKey");
        var city = _weatherState?.String("city", state.String("wxCity")) ?? state.String("wxCity");

        // The key comes first, as in the web interface: the place search below needs it.
        AddSection(L("Cheie API", "API key"));
        var (keyView, key) = SecretField(savedKey ?? "", L("Cheie OpenWeatherMap", "OpenWeatherMap key"));
        var keyField = SaveFieldCard("\uE8D7", L("Cheie OpenWeatherMap", "OpenWeatherMap key"),
            savedKey is null && hasKey
                ? L("O cheie e salvată pe ceas, dar nu a putut fi citită. Scrie-o din nou doar dacă vrei s-o schimbi.", "A key is saved on the clock but couldn't be read. Only enter it again to change it.")
                : L("Cu ea ceasul citește vremea și caută localitatea. O găsești în contul tău OpenWeatherMap.", "The clock reads the weather and searches for places with it. It's in your OpenWeatherMap account."),
            keyView,
            // The clock keeps its key when sent an empty one, so only a new, non-empty key is something to save.
            () => key.Password.Trim() is { Length: > 0 } typed && typed != savedKey,
            () =>
            {
                var typed = key.Password.Trim();
                QueueSave("weather-key", token => Post(token, "/weathersett", ("apikey", typed)), 0);
                savedKey = typed;
                hasKey = true;
                return Task.CompletedTask;
            });
        key.PasswordChanged += (_, _) => keyField.Refresh();
        OnEnter(key, keyField.Submit);
        Add(keyField.Card);

        AddSection(L("Localitate", "Location"));
        // The place already saved shows in the box; typing another one searches for it.
        var search = new AutoSuggestBox
        {
            Text = city,
            PlaceholderText = L("Caută orașul…", "Search for a city…"),
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        var (locationCard, locationStatus) = StackedCard("\uE707", L("De unde e vremea", "Where the weather is for"),
            CityText(city), search);
        Add(locationCard);

        CancellationTokenSource? searching = null;
        var places = new List<WeatherPlace>();
        search.TextChanged += async (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            searching?.Cancel();
            var query = sender.Text.Trim();
            if (query.Length < 2 || query == city)
            {
                sender.ItemsSource = null;
                locationStatus.Text = CityText(city);
                return;
            }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_dialogToken);
            searching = cancellation;
            try
            {
                // Waits for a pause in typing, as the web interface does, rather than asking on every letter.
                await Task.Delay(400, cancellation.Token);
                var path = "/weathersearch?q=" + Uri.EscapeDataString(query);
                // A key typed but not saved yet is tried for the search too.
                var typedKey = key.Password.Trim();
                if (typedKey.Length > 0 && typedKey != savedKey) path += "&key=" + Uri.EscapeDataString(typedKey);
                var result = await ClockDeviceApi.GetJsonAsync(_device, path, cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                places = result.Items()
                    .Select(place => new { Place = place, Latitude = place.Double("lat", double.NaN), Longitude = place.Double("lon", double.NaN) })
                    .Where(found => !double.IsNaN(found.Latitude) && !double.IsNaN(found.Longitude) && found.Place.String("name").Length > 0)
                    .Select(found => new WeatherPlace
                    {
                        Name = found.Place.String("name"),
                        Label = PlaceLabel(found.Place.String("name"), found.Place.String("state"), found.Place.String("country")),
                        Latitude = found.Latitude,
                        Longitude = found.Longitude,
                    })
                    .ToList();
                sender.ItemsSource = places;
                locationStatus.Text = places.Count > 0
                    ? CityText(city)
                    : L($"Nicio localitate găsită pentru „{query}”.", $"No place found for \"{query}\".");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested)
            {
                sender.ItemsSource = null;
                locationStatus.Text = hasKey || key.Password.Trim().Length > 0
                    ? L("Căutarea nu a reușit: ", "The search failed: ") + FriendlyError(error)
                    : L("Salvează mai întâi cheia API de mai jos; căutarea are nevoie de ea.", "Save the API key below first; the search needs it.");
            }
        };
        search.QuerySubmitted += (sender, args) =>
        {
            // Enter with nothing picked takes the first place found.
            var place = args.ChosenSuggestion as WeatherPlace ?? places.FirstOrDefault();
            if (place is null) return;
            searching?.Cancel();
            city = place.Label;
            QueueSave("weather-place", token => Post(token, "/weathersett",
                ("lat", place.Latitude.ToString("0.####", Inv)),
                ("lon", place.Longitude.ToString("0.####", Inv)),
                ("name", place.Label)), 0);
            sender.Text = city;
            sender.ItemsSource = null;
            places = new List<WeatherPlace>();
            locationStatus.Text = CityText(city);
        };

        AddSection(L("Ce afișează tile-ul", "What the tile shows"));
        var preset = ChoiceBox(WeatherPresets(), Str(state.Int("wxPreset", 7)), 300);
        preset.SelectionChanged += (_, _) =>
            QueueSave("wxpreset", token => Post(token, "/weathersett", ("preset", ChoiceValue(preset))), 0);
        Add(SettingsCard("\uE71D", L("Informații afișate", "Information shown"),
            L("Ce trece pe ecran din vremea de acum.", "Which parts of the current weather go on screen."), preset));
        var unit = ChoiceBox([("°C Celsius", "0"), ("°F Fahrenheit", "1")], Str(state.Int("tempunit")));
        unit.SelectionChanged += (_, _) => QueueSave("tempunit", token => Post(token, "/tempunit", ("unit", ChoiceValue(unit))), 0);
        Add(SettingsCard("\uE9CA", L("Unitatea temperaturii", "Temperature unit"),
            L("Aceeași unitate o folosește și tile-ul Temperatură.", "The Temperature tile uses the same unit."), unit));

    }

    // The combinations the web interface offers, as the clock's bit flags:
    // 1 temperature, 2 humidity, 4 the weather itself.
    private static (string, string)[] WeatherPresets() =>
    [
        (L("Doar temperatura", "Temperature only"), "1"),
        (L("Doar umiditatea", "Humidity only"), "2"),
        (L("Doar starea vremii", "Conditions only"), "4"),
        (L("Temperatură și umiditate", "Temperature and humidity"), "3"),
        (L("Temperatură și starea vremii", "Temperature and conditions"), "5"),
        (L("Umiditate și starea vremii", "Humidity and conditions"), "6"),
        (L("Temperatură, umiditate și starea vremii", "Temperature, humidity and conditions"), "7"),
    ];

    private static string CityText(string city) => city.Length > 0
        ? L("Ca s-o schimbi, scrie alt oraș și alege-l din listă.", "To change it, type another city and pick it from the list.")
        : L("Nicio localitate aleasă încă. Scrie numele orașului și alege-l din listă.", "No place chosen yet. Type the city's name and pick it from the list.");

    // "Name, State, Country", as the web interface saves it, cut to what the
    // clock's 64-byte field holds without splitting a character.
    private static string PlaceLabel(string name, string region, string country)
    {
        var label = string.Join(", ", new[] { name, region, country }.Where(part => part.Length > 0));
        while (System.Text.Encoding.UTF8.GetByteCount(label) > 63) label = label[..^1];
        return label.TrimEnd(',', ' ');
    }

    // A card whose control sits under its text across the full width, for a
    // search box or a key that needs the room.
    private static (Border Card, TextBlock Description) StackedCard(string glyph, string title, string description, FrameworkElement content)
    {
        var header = HeaderGrid(glyph, title, null, null);
        var text = Secondary(description);
        ((Panel)header.Children[1]).Children.Add(text);
        content.Margin = new Thickness(36, 0, 0, 0);
        var card = CardBorder();
        card.Padding = new Thickness(16, 12, 16, 14);
        card.Child = new StackPanel { Spacing = 10, Children = { header, content } };
        return (card, text);
    }

    private Border WeatherLanguageCard(ClockDeviceState state)
    {
        var current = state.String("wxLang", "ro");
        var choices = WeatherLanguages().ToList();
        // A code set some other way still shows, rather than the list quietly picking another.
        if (!choices.Any(choice => string.Equals(choice.Item2, current, StringComparison.OrdinalIgnoreCase)))
            choices.Insert(0, (current, current));
        var box = ChoiceBox(choices, current, 240);
        box.SelectionChanged += (_, _) => QueueSave("wxlang", token => Post(token, "/wxlang", ("lang", ChoiceValue(box))), 0);
        return SettingsCard("", L("Limba OpenWeatherMap", "OpenWeatherMap Language"),
            null, box);
    }

    private void BuildCanvasContent(ClockDeviceState state)
    {
        var bytes = ParseCanvas(state.String("canvasBmp"));
        void Changed() => QueueSave("canvas", token => Post(token, "/canvassett", ("bmp", Convert.ToHexString(bytes).ToLowerInvariant())), 500);
        var grid = BuildCanvasGrid(bytes, Changed);
        var clear = ActionButton(L("Ștergeți tot", "Clear all"), "");
        clear.Click += (_, _) =>
        {
            Array.Clear(bytes);
            WithoutSaving(() => UpdateCanvasButtons(grid, bytes));
            Changed();
        };
        Add(SettingsExpander("", L("Desen", "Drawing"),
            L("Desenează direct pe matricea de 32×8; fiecare punct apare pe ceas.", "Draw straight onto the 32×8 matrix; every dot shows up on the clock."), null,
            [
                SubBlock(null, null,
                    new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        Child = grid,
                        MaxWidth = 720,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    },
                    clear),
            ], expanded: true));
    }

    private void BuildCurrencyContent(ClockDeviceState state)
    {
        var baseBox = CurrencyBox(state.String("currencyBase", "EUR"));
        baseBox.SelectionChanged += (_, _) =>
            QueueSave("currency-base", token => Post(token, "/currencysett", ("base", ChoiceValue(baseBox))), 0);
        Add(SettingsCard("\uE8C7", L("Moneda principală", "Primary currency"),
            L("Moneda al cărei curs îl arată ceasul.", "The currency whose rate the clock shows."), baseBox));

        var compare = Toggle(state.Bool("currencyCompare"));
        var quoteBox = CurrencyBox(state.String("currencyQuote", "RON"));
        var quoteCard = SettingsCard("\uE8AB", L("Moneda comparată", "Compared currency"),
            L("Cursul monedei principale se arată față de aceasta.", "The main currency's rate is shown against this one."), quoteBox);
        // A second currency only matters once the comparison is on.
        SetExpanded(quoteCard, compare.IsOn);
        compare.Toggled += (_, _) =>
        {
            SetExpanded(quoteCard, compare.IsOn);
            QueueSave("currency-compare", token => Post(token, "/currencysett",
                ("compare", compare.IsOn ? "1" : "0"), ("quote", ChoiceValue(quoteBox))), 0);
        };
        quoteBox.SelectionChanged += (_, _) =>
            QueueSave("currency-quote", token => Post(token, "/currencysett", ("quote", ChoiceValue(quoteBox))), 0);
        Add(SettingsCard("\uE9D9", L("Compară cu altă monedă", "Compare with another currency"),
            L("Arată cursul față de o a doua monedă.", "Shows the rate against a second currency."), compare));
        Add(quoteCard);
    }

    // The currencies the web interface offers. One set some other way still
    // shows, rather than the list quietly picking another.
    private static ComboBox CurrencyBox(string selected)
    {
        var choices = new List<(string, string)>
        {
            ("EUR · Euro", "EUR"),
            ("USD · " + L("Dolar american", "US dollar"), "USD"),
            ("GBP · " + L("Liră sterlină", "Pound sterling"), "GBP"),
            ("CHF · " + L("Franc elvețian", "Swiss franc"), "CHF"),
            ("JPY · " + L("Yen japonez", "Japanese yen"), "JPY"),
            ("CAD · " + L("Dolar canadian", "Canadian dollar"), "CAD"),
            ("AUD · " + L("Dolar australian", "Australian dollar"), "AUD"),
            ("RON · " + L("Leu românesc", "Romanian leu"), "RON"),
        };
        var code = selected.Trim().ToUpperInvariant();
        if (code.Length == 3 && !choices.Any(choice => choice.Item2 == code)) choices.Insert(0, (code, code));
        return ChoiceBox(choices, code, 240);
    }

    private static byte[] ParseCanvas(string value)
    {
        if (value.Length == 64)
        {
            try { return Convert.FromHexString(value); }
            catch (FormatException) { }
        }
        return new byte[32];
    }

    private Grid BuildCanvasGrid(byte[] bytes, Action changed)
    {
        var grid = new Grid
        {
            Width = 640, Height = 160,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        var dots = new ToggleButton[32, 8];
        for (var x = 0; x < 32; x++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        for (var y = 0; y < 8; y++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 32; x++)
        {
            var column = x;
            var row = y;
            var dot = new ToggleButton
            {
                IsChecked = (bytes[x] & (1 << y)) != 0,
                Width = 16, Height = 16, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(0), CornerRadius = new CornerRadius(8), Tag = (x, y),
            };
            AutomationProperties.SetName(dot, L($"Pixel {x + 1}, {y + 1}", $"Pixel {x + 1}, {y + 1}"));
            dot.Checked += (_, _) => { bytes[column] |= (byte)(1 << row); if (!_suppressSaves) changed(); };
            dot.Unchecked += (_, _) => { bytes[column] &= (byte)~(1 << row); if (!_suppressSaves) changed(); };
            Grid.SetColumn(dot, x);
            Grid.SetRow(dot, y);
            grid.Children.Add(dot);
            dots[x, y] = dot;
        }

        // Route pointer strokes through one surface. A child ToggleButton must
        // not capture the pointer or toggle again when the stroke is released.
        // The buttons remain keyboard-accessible underneath this input layer.
        var inputSurface = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Grid.SetColumnSpan(inputSurface, 32);
        Grid.SetRowSpan(inputSurface, 8);
        grid.Children.Add(inputSurface);

        uint? pointer = null;
        var paintOn = true;
        var lastX = 0;
        var lastY = 0;
        void Paint(int x, int y)
        {
            var edited = false;
            // Fill the line between pointer samples, even with a fast mouse.
            var dx = Math.Abs(x - lastX);
            var dy = -Math.Abs(y - lastY);
            var sx = lastX < x ? 1 : -1;
            var sy = lastY < y ? 1 : -1;
            var error = dx + dy;
            WithoutSaving(() =>
            {
                while (true)
                {
                    if (dots[lastX, lastY].IsChecked != paintOn)
                    {
                        dots[lastX, lastY].IsChecked = paintOn;
                        edited = true;
                    }
                    if (lastX == x && lastY == y) break;
                    var twice = 2 * error;
                    if (twice >= dy) { error += dy; lastX += sx; }
                    if (twice <= dx) { error += dx; lastY += sy; }
                }
            });
            if (edited) changed();
        }
        grid.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            if (pointer is not null) return;
            var point = args.GetCurrentPoint(grid);
            if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed) return;
            lastX = Math.Clamp((int)(point.Position.X / 20), 0, 31);
            lastY = Math.Clamp((int)(point.Position.Y / 20), 0, 7);
            paintOn = !point.Properties.IsRightButtonPressed && dots[lastX, lastY].IsChecked != true;
            if (!grid.CapturePointer(args.Pointer)) return;
            pointer = args.Pointer.PointerId;
            dots[lastX, lastY].Focus(FocusState.Pointer);
            Paint(lastX, lastY);
            args.Handled = true;
        }), true);
        grid.PointerMoved += (_, args) =>
        {
            if (pointer != args.Pointer.PointerId) return;
            var position = args.GetCurrentPoint(grid).Position;
            Paint(Math.Clamp((int)(position.X / 20), 0, 31), Math.Clamp((int)(position.Y / 20), 0, 7));
            args.Handled = true;
        };
        grid.PointerReleased += (_, args) =>
        {
            if (pointer != args.Pointer.PointerId) return;
            pointer = null;
            grid.ReleasePointerCapture(args.Pointer);
            args.Handled = true;
        };
        grid.PointerCanceled += (_, _) => pointer = null;
        grid.PointerCaptureLost += (_, _) => pointer = null;
        grid.Unloaded += (_, _) => { pointer = null; grid.ReleasePointerCaptures(); };
        return grid;
    }

    private static void UpdateCanvasButtons(Grid grid, byte[] bytes)
    {
        foreach (var dot in grid.Children.OfType<ToggleButton>())
            if (dot.Tag is ValueTuple<int, int> point)
                dot.IsChecked = (bytes[point.Item1] & (1 << point.Item2)) != 0;
    }
}
