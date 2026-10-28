using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace OctoglowSender;

// The device panels under Configuration - the hardware monitor, power and
// sleep - and backing the clock's settings up to a file and back.
public sealed partial class ClockNativeSettingsPage
{
    // ------------------------------------------------------------------ general management

    // Everything that is about the clock itself rather than about what it shows.
    // Its pages open inside this one, since a panel cannot open over a panel.
    private void BuildGeneralManagement()
    {
        // The web interface lists the hardware monitor first.
        AddSection(L("Dispozitiv", "Device"));
        AddSubPageCard(ClockSettingsSection.Hardware, BuildHardwarePage);
        AddSubPageCard(ClockSettingsSection.Power, BuildPowerPage);

        AddSection(L("Utilizator", "User"));
        AddSubPageCard(ClockSettingsSection.Preferences, BuildPreferences);

        AddSection(L("Backup și restaurare", "Backup and Restore"));
        BuildBackup();

    }

    // ------------------------------------------------------------------ preferences

    private Action? BuildPreferences(ClockDeviceState state)
    {
        AddSection(L("Interfață", "Interface"));
        var highlight = Toggle(state.Bool("liveHl", true));
        highlight.Toggled += (_, _) => QueueSetting("livehl", highlight.IsOn ? "1" : "0");
        Add(SettingsCard("\uE7E6", L("Evidențiați tile-ul activ", "Highlight the Active Tile"),
            L("Colorează în listă tile-ul aflat pe matrice", "Tints the tile that is on the matrix right now"), highlight));

        var grayed = Toggle(state.Bool("showGrayed", true));
        grayed.Toggled += (_, _) =>
        {
            QueueSetting("showgrayed", grayed.IsOn ? "1" : "0");
            ShowStatus(Surface.Status, InfoBarSeverity.Informational, L("Se aplică la următoarea deschidere", "Applies next time you open them"),
                L("Listele deja deschise se schimbă după ce le redeschideți.", "Lists that are already open change once you open them again."));
        };
        Add(SettingsCard("\uE890", L("Afișați conținutul indisponibil", "Show Unavailable Content"),
            L("Arată estompat ce nu se poate folosi acum", "Shows what cannot be used right now, dimmed"), grayed));

        AddSection(L("La pornire", "On Startup"));
        var startMode = ChoiceBox([("WiFi", "0"), ("Access Point", "1")], Str(state.Int("defaultStartMode")), 180);
        startMode.SelectionChanged += (_, _) => QueueSetting("startmode", ChoiceValue(startMode));
        Add(SettingsCard("\uE7E8", L("Mod de pornire implicit", "Default Start Mode"),
            L("Ce pornește ceasul când ia curent", "What the clock starts in when it gets power"), startMode));

        AddSection(L("Ora și data", "Time & Date"));
        var netTime = Toggle(state.Bool("netTimeSync", true));
        netTime.Toggled += (_, _) => QueueSave("nettime", token => Post(token, "/timesett",
            ("netsync", netTime.IsOn ? "1" : "0")), 0);
        // Without the network the clock has no battery behind its time, so
        // setting it by hand is the only way it knows what time it is.
        var clockTime = ValueText("-");
        var date = new CalendarDatePicker { Date = DateTimeOffset.Now, MinWidth = 180 };
        var time = new TimePicker { ClockIdentifier = "24HourClock", Time = DateTime.Now.TimeOfDay, MinWidth = 150 };
        var fromComputer = ActionButton(L("De pe dispozitiv", "From this device"), "\uE977");
        var apply = ActionButton(L("Aplicați", "Apply"), null, accent: true);
        fromComputer.Click += (_, _) =>
        {
            var now = DateTime.Now;
            date.Date = new DateTimeOffset(now);
            time.Time = now.TimeOfDay;
        };

        Add(ToggleGroup("\uE121", L("Preluați ora de pe rețea", "Get Time From Network"),
            L("Ceasul își ia singur ora și data prin internet", "The clock gets the time and date over the internet by itself"), netTime,
            [
                SubRow(L("Ora ceasului", "The clock's time"), null, clockTime),
                SubBlock(L("Setați ora manual", "Set The Time Manually"),
                    L("Ceasul nu are baterie: fără preluarea de pe rețea, ora se pierde la fiecare pornire.",
                      "The clock has no battery: without network time, it loses the time at every start."),
                    Inline(date, time), Inline(fromComputer, apply)),
            ],
            showWhenOn: false));

        // The card says what it does and the page it opens is named for what it
        // holds, as in the web interface.
        AddSection(L("Cont", "Account"));
        var (_, accountDescription) = SectionHeader(ClockSettingsSection.Account);
        Add(NavCard(SectionGlyph(ClockSettingsSection.Account), L("Schimbați datele de autentificare", "Change User Account Login Info"), accountDescription,
            () => OpenSubPage(L("Datele de autentificare", "Login Info"), accountDescription, _ =>
            {
                BuildAccount();
                return null;
            })));

        // The clock's own time is read again while this page is open, so what is
        // shown is what the clock says rather than what this computer thinks.
        var cancellation = new CancellationTokenSource();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        var busy = false;
        async void ReadClockTime()
        {
            if (busy || cancellation.IsCancellationRequested) return;
            busy = true;
            try
            {
                var now = await TryGetJsonAsync("/apstate", cancellation.Token);
                if (now is null || cancellation.IsCancellationRequested) return;
                clockTime.Text = now.Bool("timeValid") &&
                                 DateTime.TryParse(now.String("devTime"), Inv, DateTimeStyles.None, out var at)
                    ? at.ToString(AppStrings.IsEnglish ? "MMM d, yyyy HH:mm:ss" : "dd.MM.yyyy HH:mm:ss",
                        AppStrings.IsEnglish ? Inv : CultureInfo.GetCultureInfo("ro-RO"))
                    : L("Nesetată", "Not set");
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                busy = false;
            }
        }
        apply.Click += async (_, _) =>
        {
            var at = (date.Date?.DateTime.Date ?? DateTime.Now.Date) + time.Time;
            // The pickers only go down to the minute. When what they hold is this
            // computer's current minute - which is what "De pe calculator" puts
            // there - the seconds go along too, so the clock lands on the second.
            var now = DateTime.Now;
            var seconds = at == new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0) ? now.Second : 0;
            await RunActionAsync(apply, token => Post(token, "/timesett",
                    ("y", Str(at.Year)), ("mo", Str(at.Month)), ("d", Str(at.Day)),
                    ("h", Str(at.Hour)), ("mi", Str(at.Minute)), ("s", Str(seconds))),
                L("Ora ceasului a fost pusă.", "The clock's time is set."));
            ReadClockTime();
        };
        timer.Tick += (_, _) => ReadClockTime();
        timer.Start();
        ReadClockTime();
        return () =>
        {
            timer.Stop();
            cancellation.Cancel();
        };
    }

    // ------------------------------------------------------------------ power and sleep

    private Action? BuildPowerPage(ClockDeviceState state)
    {
        AddSection(L("Alimentare", "Power"));
        Add(PowerOptionsCard(state));

        AddSection(L("Somn", "Sleep"));
        var total = Math.Clamp(state.Int("autoSleepSec", 1800), 60, 86400);
        var automatic = Toggle(state.Bool("autoSleep"));
        var hours = TimePart(total / 3600, 24, L("Ore", "Hours"));
        var minutes = TimePart(total / 60 % 60, 59, L("Minute", "Minutes"));
        var seconds = TimePart(total % 60, 59, L("Secunde", "Seconds"));
        void Save()
        {
            static double Part(NumberBox box) => double.IsNaN(box.Value) ? 0 : box.Value;
            var sum = (int)(Part(hours) * 3600 + Part(minutes) * 60 + Part(seconds));
            // The clock takes a minute to a day; the boxes show what it will take.
            var accepted = Math.Clamp(sum, 60, 86400);
            if (accepted != sum)
            {
                WithoutSaving(() =>
                {
                    hours.Value = accepted / 3600;
                    minutes.Value = accepted / 60 % 60;
                    seconds.Value = accepted % 60;
                });
            }
            QueueSave("autosleep", token => Post(token, "/settings",
                ("autosleep", automatic.IsOn ? "1" : "0"), ("autosleepsec", Str(accepted))), 400);
        }
        automatic.Toggled += (_, _) => Save();
        hours.ValueChanged += (_, _) => Save();
        minutes.ValueChanged += (_, _) => Save();
        seconds.ValueChanged += (_, _) => Save();
        Add(ToggleGroup("", L("Somn automat", "Automatic Sleep"),
            L("Stinge ecranul după o perioadă fără activitate", "Turns the display off after a spell of inactivity"), automatic,
            [
                SubRow(L("Stinge după", "Sleep after"), L("Ore : minute : secunde", "Hours : minutes : seconds"),
                    Inline(hours, Colon(), minutes, Colon(), seconds)),
            ]));
        var note = Secondary(L("O notificare de la Octoglow Connect, o piesă redată sau pierderea conexiunii contează ca activitate și trezesc ecranul. La fel și senzorul tactil.", "A notification from Octoglow Connect, a track playing or a dropped connection all count as activity and wake the display. So does the touch sensor."));
        AddDidYouKnow(("\uE708", L("Ce trezește ecranul", "What wakes the display"), note.Text));
        return null;
    }

    private Button PowerOptionsCard(ClockDeviceState state)
    {
        var title = L("Opțiuni de alimentare", "Power Options");
        var card = CardButton();
        card.Content = HeaderGrid("\uE7E8", title,
            L("Opriți, reporniți sau treceți în somn", "Power off, restart or sleep"),
            new FontIcon { Glyph = "\uE76C", FontSize = 14, Opacity = 0.72 });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, title);
        card.Click += (_, _) => _ = ShowPowerOptionsAsync(state);
        return card;
    }

    private async Task ShowPowerOptionsAsync(ClockDeviceState state)
    {
        if (_tileDialog is not null || XamlRoot is null) return;
        var root = XamlRoot;
        var width = Math.Clamp(root.Size.Width - 120, 280, 680);
        var content = new StackPanel { Spacing = 4 };
        var scroller = new ScrollViewer
        {
            Width = width,
            MaxHeight = DialogBodyHeight(root),
            Content = new StackPanel
            {
                Padding = new Thickness(0, 0, 14, 0),
                Children = { content },
            },
        };
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args) => scroller.MaxHeight = DialogBodyHeight(sender);
        root.Changed += Resized;

        var title = L("Opțiuni de alimentare", "Power Options");
        var header = new Grid { ColumnSpacing = 14, Width = width };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(GlyphIcon("\uE7E8", 24));
        var heading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(Secondary(L("Opriți, reporniți sau treceți în somn", "Power off, restart or sleep")));
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = header,
            Content = scroller,
            CloseButtonText = L("Anulați", "Cancel"),
            DefaultButton = ContentDialogButton.None,
            RequestedTheme = ActualTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = width + 64;

        void CloseDialog() => dialog.Hide();
        content.Children.Add(PowerCard("\uE7E8", L("Opriți", "Power off"),
            L("Se oprește tot. Țineți apăsat senzorul tactil pentru a porni ceasul înapoi.", "Everything shuts down. Hold the touch sensor to switch the clock back on."),
            "off",
            L("Opriți ceasul?", "Turn the clock off?"),
            L("Se oprește tot și aplicația pierde legătura cu el. Îl porniți înapoi ținând apăsat senzorul tactil.",
              "Everything turns off and the app loses its connection. Hold the touch sensor to turn it back on."),
            L("Opriți", "Power off"),
            L("Ceasul s-a oprit. Țineți apăsat senzorul tactil pentru a-l porni.", "The clock is off. Hold the touch sensor to switch it on."),
            CloseDialog));
        content.Children.Add(PowerCard("\uE777", L("Reporniți", "Restart"),
            L("Ceasul repornește singur în câteva secunde.", "The clock comes back on its own in a few seconds."),
            "reboot",
            L("Reporniți ceasul?", "Restart the clock?"),
            L("Legătura cu ceasul se întrerupe câteva secunde.", "The connection to the clock drops for a few seconds."),
            L("Reporniți", "Restart"),
            L("Ceasul repornește. Reîncărcați pagina în câteva secunde.", "The clock is restarting. Reload the page in a few seconds."),
            CloseDialog));

        var sleeping = state.Bool("sleeping");
        content.Children.Add(PowerCard("\uE708",
            sleeping ? L("Treziți ecranul", "Wake the screen") : L("Somn", "Sleep"),
            sleeping ? L("Ecranul e stins acum. Se trezește și la atingere sau la o notificare.", "The display is off right now. A touch or a notification also brings it back.")
                     : L("Ecranul se stinge. Se trezește la atingere sau la o notificare.", "The display goes dark. A touch or a notification brings it back."),
            sleeping ? "wake" : "sleep",
            null, null, "",
            sleeping ? L("Ecranul s-a aprins.", "The screen is on.") : L("Ecranul s-a stins.", "The screen is out."),
            CloseDialog));

        _tileDialog = dialog;
        try
        {
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // WinUI allows only one ContentDialog at a time.
        }
        finally
        {
            root.Changed -= Resized;
            _tileDialog = null;
        }
    }

    // One tap does what the title says, so the verb is written there rather than
    // a second time on the right; it comes back on the button of the question
    // asked before something that can't be taken back.
    private Button PowerCard(string glyph, string title, string description, string action,
        string? confirmTitle, string? confirmMessage, string confirmAction, string done,
        Action? beforeAction = null)
    {
        var card = ActionCard(glyph, title, description, "", null).Card;
        card.Click += async (_, _) =>
        {
            if (beforeAction is not null)
            {
                beforeAction();
                await Task.Delay(100);
            }
            if (confirmTitle is not null && !await ConfirmAsync(confirmTitle, confirmMessage ?? "", confirmAction)) return;
            await RunActionAsync(card, token => Post(token, "/power", ("action", action)), done);
        };
        return card;
    }

    private static NumberBox TimePart(int value, int maximum, string name)
    {
        var box = new NumberBox { Minimum = 0, Maximum = maximum, Value = value, Width = 64, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, name);
        ToolTipService.SetToolTip(box, name);
        return box;
    }

    private static TextBlock Colon() => new() { Text = ":", Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };

    // ------------------------------------------------------------------ hardware monitor

    private sealed record Usage(Border Card, TextBlock Value, ProgressBar Bar);

    private Action? BuildHardwarePage(ClockDeviceState state)
    {
        var fahrenheit = state.Int("tempunit") == 1;

        AddSection(L("Stare hardware", "Hardware Status"));
        var ram = UsageCard("", "RAM", L("Memoria de lucru folosită.", "Working memory in use."));
        var psram = UsageCard("", "PSRAM", L("Memoria suplimentară a plăcii.", "The board's extra memory."));
        var flash = UsageCard("", "Firmware", L("Cât din partiția aplicației ocupă firmware-ul.", "How much of the app partition the firmware takes."));
        var temperature = UsageCard("", L("Temperatura cipului", "Chip temperature"), L("Scala merge până la 100 °C.", "The scale runs to 100 °C."));
        // Only boards that have them: until the first reading says so, they stay out.
        psram.Card.Visibility = Visibility.Collapsed;
        temperature.Card.Visibility = Visibility.Collapsed;

        AddSection(L("Detalii memorie", "Memory Details"));
        var ramFree = ValueCard("", L("RAM liber", "Free RAM"), null);
        var largestBlock = ValueCard("", L("Cel mai mare bloc alocabil", "Largest allocatable block"), null);
        var lowestFree = ValueCard("", L("Minim liber atins", "Lowest free reached"), null);
        var firmwareFree = ValueCard("", L("Spațiu firmware liber", "Free firmware space"), null);

        AddSection(L("Fluiditate afișaj", "Display Smoothness"));
        var reset = ActionCard("\uE9D9", L("Resetați recordurile", "Reset the peaks"),
            L("Începe din nou măsurarea celor mai lungi pauze.", "Starts measuring the longest pauses again."),
            "", null).Card;
        Add(reset);
        var loops = ValueCard("", L("Bucle pe secundă", "Loops per second"), L("Cât de des se redesenează matricea", "How often the matrix is redrawn"));
        var longestPause = ValueCard("", L("Cea mai lungă pauză", "Longest pause"), L("În ultima secundă", "In the last second"));
        var peakPause = ValueCard("", L("Record de la resetare", "Record since reset"), L("Cea mai lungă pauză înregistrată", "The longest pause recorded"));
        var slowestRequest = ValueCard("", L("Cea mai lentă cerere", "Slowest request"), null);
        var pageSend = ValueCard("", L("Trimiterea interfeței", "Serving the interface"),
            L("Cât ține matricea ocupată o încărcare de pagină.", "How long a page load keeps the matrix busy."));

        AddSection(L("Sistem", "System"));
        var chip = ValueCard("", L("Cip", "Chip"), null);
        var frequency = ValueCard("", L("Frecvență CPU", "CPU Frequency"), null);
        var cores = ValueCard("", L("Nuclee", "Cores"), null);
        var flashSize = ValueCard("", L("Memorie flash", "Flash Memory"), null);

        void Update(ClockDeviceState hardware)
        {
            var heapSize = hardware.Double("heapSize");
            var heapFree = hardware.Double("heapFree");
            SetUsage(ram, heapSize - heapFree, heapSize);
            var psramSize = hardware.Double("psramSize");
            psram.Card.Visibility = psramSize > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (psramSize > 0) SetUsage(psram, psramSize - hardware.Double("psramFree"), psramSize);
            var sketch = hardware.Double("sketchSize");
            var sketchFree = hardware.Double("sketchFree");
            SetUsage(flash, sketch, sketch + sketchFree);
            var hasTemperature = hardware.Bool("hasTemp");
            temperature.Card.Visibility = hasTemperature ? Visibility.Visible : Visibility.Collapsed;
            if (hasTemperature)
            {
                var celsius = hardware.Double("tempC");
                temperature.Bar.Value = Math.Clamp(celsius, 0, 100);
                temperature.Value.Text = fahrenheit ? $"{Math.Round(celsius * 9 / 5 + 32)} °F" : $"{Math.Round(celsius)} °C";
            }

            ramFree.Text = Bytes(heapFree);
            largestBlock.Text = Bytes(hardware.Double("heapMaxAlloc"));
            lowestFree.Text = Bytes(hardware.Double("heapMinFree"));
            firmwareFree.Text = Bytes(sketchFree);

            loops.Text = hardware.Int("loopsPerSec").ToString("N0", CultureInfo.CurrentCulture);
            longestPause.Text = Microseconds(hardware.Double("loopMaxUs"));
            peakPause.Text = Microseconds(hardware.Double("loopMaxUsEver"));
            var uri = hardware.String("httpMaxUri");
            slowestRequest.Text = uri.Length > 0 ? $"{Microseconds(hardware.Double("httpMaxUs"))} · {uri}" : L("Nicio cerere încă", "No request yet");
            pageSend.Text = Microseconds(hardware.Double("pageUs"));

            chip.Text = hardware.String("chip", "-");
            frequency.Text = $"{hardware.Int("cpuFreqMhz")} MHz";
            cores.Text = Str(hardware.Int("cores"));
            flashSize.Text = Bytes(hardware.Double("flashSize"));
        }

        // Every second and a half, as the web interface polls it, and only while the page is open.
        var cancellation = new CancellationTokenSource();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(1500);
        var busy = false;
        async void Tick()
        {
            if (busy || cancellation.IsCancellationRequested) return;
            busy = true;
            try
            {
                if (await TryGetJsonAsync("/hwstate", cancellation.Token) is { } hardware && !cancellation.IsCancellationRequested)
                    Update(hardware);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                busy = false;
            }
        }
        reset.Click += async (_, _) =>
        {
            await RunActionAsync(reset, token => Post(token, "/hwreset"), L("Recordurile au fost resetate.", "The peaks were reset."));
            Tick();
        };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
        return () =>
        {
            timer.Stop();
            cancellation.Cancel();
        };
    }

    private Usage UsageCard(string glyph, string title, string description)
    {
        var value = ValueText("-");
        value.MinWidth = 150;
        var bar = new ProgressBar { Width = 160, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
        var card = SettingsCard(glyph, title, description,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { value, bar } });
        Add(card);
        return new Usage(card, value, bar);
    }

    private TextBlock ValueCard(string glyph, string title, string? description)
    {
        var value = ValueText("-");
        Add(SettingsCard(glyph, title, description, value));
        return value;
    }

    private static void SetUsage(Usage usage, double used, double total)
    {
        var percent = total > 0 ? 100 * used / total : 0;
        usage.Bar.Value = Math.Clamp(percent, 0, 100);
        usage.Value.Text = $"{Math.Round(percent)}% · {Bytes(used)} / {Bytes(total)}";
    }

    private static string Bytes(double value) => value switch
    {
        >= 1048576 => (value / 1048576).ToString(value >= 10485760 ? "0" : "0.#", CultureInfo.CurrentCulture) + " MB",
        >= 1024 => (value / 1024).ToString(value >= 10240 ? "0" : "0.#", CultureInfo.CurrentCulture) + " KB",
        _ => $"{Math.Round(value)} B",
    };

    // These span three orders of magnitude between a healthy loop and a stall
    // you can see on the matrix, so each gets the unit that reads best.
    private static string Microseconds(double value) => value switch
    {
        < 1000 => $"{Math.Round(value)} µs",
        < 1e6 => (value / 1000).ToString(value < 1e4 ? "0.0" : "0", CultureInfo.CurrentCulture) + " ms",
        _ => (value / 1e6).ToString("0.00", CultureInfo.CurrentCulture) + " s",
    };

    // ------------------------------------------------------------------ backup and restore

    // The parts a backup can hold, as the web interface names them. The clock
    // writes the file itself (GET /backup), with every setting it keeps in the
    // parts asked for. Personalisation is ticked to begin with; the connection
    // and the user carry passwords, so they go in only when asked for.
    private static (string Key, string Glyph, string Title, string Description, bool Ticked)[] BackupParts() =>
    [
        ("custom", "\uE790", L("Personalizare", "Personalization"),
            L("Tile-uri, API-uri, fonturi, iconițe, sunete, senzor tactil, limbă, temă și preferințe",
              "Tiles, API keys, fonts, icons, sounds, touch sensor, language, theme and preferences"), true),
        ("conn", "\uE701", L("Conexiune", "Connection"),
            L("Rețeaua Wi-Fi și Access Point, cu parolele lor", "The Wi-Fi network and the Access Point, with their passwords"), false),
        ("users", "\uE77B", L("Utilizator", "User"),
            L("Numele de utilizator și parola interfeței web", "The web interface's user name and password"), false),
    ];

    // Three things to do, one after another, under whatever heading the page
    // that shows them puts above.
    private void BuildBackup()
    {
        var export = ActionCard("\uE896", L("Exportați setările curente", "Export Current Settings"),
            L("Descărcați un fișier cu setările curente", "Download a file with the current settings"),
            "", null).Card;
        export.Click += async (_, _) => await ExportBackupAsync(export);
        Add(export);

        var restore = ActionCard("\uE898", L("Încărcați un backup", "Upload a Backup"),
            L("Restaurați setările dintr-un fișier de backup", "Restore settings from a backup file"),
            "", null).Card;
        restore.Click += async (_, _) => await RestoreBackupAsync(restore);
        Add(restore);

        var factory = ActionCard("\uE777", L("Resetați la setările din fabrică", "Reset to Factory Settings"),
            L("Ștergeți toate setările, WiFi și contul de acces", "Erase all settings, WiFi and the access account"),
            "", null).Card;
        factory.Click += async (_, _) =>
        {
            if (!await ConfirmAsync(L("Resetați ceasul la setările din fabrică?", "Reset the clock to factory settings?"),
                    L("Se șterg toate setările, rețeaua Wi-Fi salvată și contul de acces. Ceasul repornește în modul Access Point și trebuie configurat de la zero. Nu se poate anula.",
                      "Every setting, the saved Wi-Fi network and the sign-in account are erased. The clock restarts in Access Point mode and has to be set up from scratch. This can't be undone."),
                    L("Resetați", "Reset")))
                return;
            await RunActionAsync(factory, token => Post(token, "/factoryreset"),
                L("Setările au fost șterse. Ceasul repornește în modul Access Point.", "The settings are erased. The clock is restarting in Access Point mode."));
        };
        Add(factory);
    }

    private async Task ExportBackupAsync(Button button)
    {
        Surface.Status.IsOpen = false;
        var parts = await ChooseBackupPartsAsync();
        if (parts is null) return;
        button.IsEnabled = false;
        try
        {
            string text;
            try
            {
                text = await ClockDeviceApi.GetTextAsync(_device, "/backup?" + string.Join("&", parts.Select(part => part + "=1")), CancellationToken.None);
            }
            catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ShowError(L("Firmware-ul ceasului este mai vechi și nu poate exporta toate setările. Actualizați-l din Despre.",
                            "The clock's firmware is older and can't export every setting. Update it from About."));
                return;
            }
            var file = JsonNode.Parse(text) as JsonObject
                       ?? throw new InvalidOperationException(L("Ceasul a trimis un backup pe care aplicația nu-l poate citi.", "The clock sent a backup this app can't read."));
            // The date goes in next to the version, where someone opening the file looks.
            var backup = new JsonObject
            {
                ["device"] = file["device"]?.DeepClone(),
                ["backupFormat"] = file["backupFormat"]?.DeepClone(),
                ["firmwareVersion"] = file["firmwareVersion"]?.DeepClone(),
                ["exportedAt"] = DateTime.UtcNow.ToString("o", Inv),
                ["parts"] = file["parts"]?.DeepClone(),
                ["nvs"] = file["nvs"]?.DeepClone(),
            };

            var picker = new FileSavePicker { SuggestedFileName = $"clock-backup-{DateTime.Now.ToString("yyyy-MM-dd", Inv)}" };
            picker.FileTypeChoices.Add(L("Backup Octoglow", "Octoglow backup"), new List<string> { ".json" });
            InitializePicker(picker);
            var target = await picker.PickSaveFileAsync();
            if (target is null) return;
            await File.WriteAllTextAsync(target.Path, BackupText(backup));
            ShowSuccess(L("Setările au fost salvate în ", "Settings saved to ") + target.Name);
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

    // Laid out as the web interface writes it: indented, one setting to a line.
    private static string BackupText(JsonObject backup) =>
        System.Text.RegularExpressions.Regex.Replace(
            backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            "\\[\\r?\\n\\s+(\"[a-z0-9]+\"),\\r?\\n\\s+([^\\r\\n]+)\\r?\\n\\s+\\]", "[$1, $2]");

    // Which parts go in the file, or null when the dialog is cancelled.
    private async Task<List<string>?> ChooseBackupPartsAsync()
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null) return null;
        var boxes = new List<(string Key, CheckBox Box)>();
        var rows = new StackPanel { Spacing = 4 };
        foreach (var part in BackupParts())
        {
            var box = new CheckBox { IsChecked = part.Ticked, MinWidth = 0, Padding = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, part.Title);
            boxes.Add((part.Key, box));
            rows.Children.Add(SettingsCard(part.Glyph, part.Title, part.Description, box));
        }
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = DialogHeading.Create("\uE8B5", L("Exportați setările curente", "Export Current Settings")),
            Content = rows,
            PrimaryButtonText = L("Exportați", "Export"),
            CloseButtonText = L("Anulați", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeSource.ActualTheme,
        };
        void Refresh() => dialog.IsPrimaryButtonEnabled = boxes.Any(entry => entry.Box.IsChecked == true);
        foreach (var (_, box) in boxes)
        {
            box.Checked += (_, _) => Refresh();
            box.Unchecked += (_, _) => Refresh();
        }
        Refresh();
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        return boxes.Where(entry => entry.Box.IsChecked == true).Select(entry => entry.Key).ToList();
    }

    private async Task RestoreBackupAsync(Button button)
    {
        Surface.Status.IsOpen = false;
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            JsonObject? backup;
            try
            {
                backup = JsonNode.Parse(await File.ReadAllTextAsync(file.Path)) as JsonObject;
            }
            catch (JsonException)
            {
                ShowError(L("Fișierul nu este un JSON valid.", "The file isn't valid JSON."));
                return;
            }
            if (backup is null || !(backup["device"] is JsonValue deviceValue && deviceValue.TryGetValue<string>(out var device) && device == "Octoglow"))
            {
                ShowError(L("Acest fișier nu este un backup Octoglow.", "This file isn't an Octoglow backup."));
                return;
            }
            var version = backup["firmwareVersion"] is JsonValue versionValue && versionValue.TryGetValue<string>(out var versionText) ? versionText : "?";

            // Format 2 is the clock's own file and goes back whole; format 1 is
            // the /state copy older versions exported, which the clock still takes.
            string body;
            string message;
            if (backup["nvs"] is JsonObject nvs)
            {
                var names = new List<string>();
                if (nvs.ContainsKey("settings")) names.Add(L("Personalizare", "Personalization"));
                if (nvs.ContainsKey("wifi") || nvs.ContainsKey("apcfg")) names.Add(L("Conexiune", "Connection"));
                if (nvs.ContainsKey("auth")) names.Add(L("Utilizator", "User"));
                if (names.Count == 0)
                {
                    ShowError(L("Fișierul de backup nu conține setări.", "The backup file has no settings in it."));
                    return;
                }
                body = new JsonObject { ["backupFormat"] = 2, ["nvs"] = nvs.DeepClone() }.ToJsonString();
                message = L($"Se aplică {string.Join(", ", names)} din {file.Name} (firmware {version}). Ceasul repornește după aceea.",
                            $"{string.Join(", ", names)} from {file.Name} (firmware {version}) will be applied. The clock restarts afterwards.");
                if (nvs.ContainsKey("wifi"))
                    message += L(" Se conectează apoi la rețeaua Wi-Fi din backup.", " It then joins the Wi-Fi network in the backup.");
                if (nvs.ContainsKey("auth"))
                    message += L(" Vă autentificați apoi cu utilizatorul și parola din backup.", " You then sign in with the user name and password in the backup.");
            }
            else if (backup["settings"] is JsonObject settings && settings.Count > 0)
            {
                body = settings.ToJsonString();
                message = L($"Setările ceasului sunt înlocuite cu cele din {file.Name} (firmware {version}). Ceasul repornește după aceea.",
                            $"The clock's settings are replaced with the ones in {file.Name} (firmware {version}). The clock restarts afterwards.");
            }
            else
            {
                ShowError(L("Fișierul de backup nu conține setări.", "The backup file has no settings in it."));
                return;
            }

            if (!await ConfirmAsync(L("Aplici acest backup?", "Apply this backup?"), message, L("Aplicați", "Apply")))
                return;

            await RunActionAsync(button, token => ClockDeviceApi.PostJsonAsync(_device, "/restorebackup", body, token),
                L("Backup-ul a fost aplicat. Ceasul repornește; reîncarcă pagina în câteva secunde.", "The backup was applied. The clock is restarting; refresh the page in a few seconds."));
        }
        catch (Exception error)
        {
            ShowError(FriendlyError(error));
        }
    }

    private static void InitializePicker(object picker)
    {
        if (App.MainWindow is not null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
    }
}
