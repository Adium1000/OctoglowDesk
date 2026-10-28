using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace OctoglowSender;

// A tile's own settings, in a dialog over the Tile Manager so the lists stay
// exactly as they were, scrolled where they were.
public sealed partial class ClockNativeSettingsPage
{
    private ContentDialog? _tileDialog;
    private CancellationTokenSource? _tileDialogCancellation;
    private SettingsSurface? _tileDialogOwner;
    private InfoBar? _dialogStatus;
    // The API keys the clock has saved, read when their tile's settings open;
    // null when they couldn't be read.
    private string? _weatherKey;
    private string? _youtubeKey;
    private CancellationToken _dialogToken;
    // What stops the dialog's polls once it closes.
    private readonly List<Action> _dialogCleanups = new();

    private void DismissTileDialog(SettingsSurface owner)
    {
        if (!ReferenceEquals(owner, _tileDialogOwner)) return;
        _tileDialogCancellation?.Cancel();
        _tileDialog?.Hide();
    }

    private async Task ShowTileSettingsAsync(string key, Button? sourceButton = null)
    {
        if (_tileDialog is not null || XamlRoot is null) return;
        var root = XamlRoot;
        var owner = Surface;
        PauseTileLineReads();

        // Most tiles only need room for a settings card and the 32x8 preview.
        // Keep the two editors with genuinely wide rows from being squeezed.
        var preferredWidth = key == "6" ? 640 : 500;
        var width = Math.Clamp(root.Size.Width - 96, 240, preferredWidth);
        var status = new InfoBar { IsOpen = false, IsClosable = true };
        var loading = new ProgressRing { IsActive = true, Width = 32, Height = 32, Margin = new Thickness(0, 48, 0, 48) };
        var content = new StackPanel { Spacing = 4, Children = { loading } };
        // The dialog's own content area never scrolls (VerticalScrollBarVisibility
        // is Disabled in its template), so the settings bring a scroller of their own.
        var scroller = new ScrollViewer
        {
            Width = width,
            MaxHeight = DialogBodyHeight(root),
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Padding = new Thickness(0, 0, 12, 0), Children = { status, content } },
        };

        var saveIndicator = NewSaveIndicator();
        // Reserve the status slot so saves cannot reflow the title.
        var saveSlot = new Grid { Width = 104, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, Children = { saveIndicator.Panel } };
        var header = new Grid { ColumnSpacing = 14, Width = width };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(GlyphIcon(TileSettingsGlyph(key), 24));
        var heading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock { Text = TileSettingsTitle(key), TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(Secondary(L("Schimbările se salvează imediat pe ceas.", "Changes are saved to the clock right away.")));
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        Grid.SetColumn(saveSlot, 2);
        header.Children.Add(saveSlot);
        void FitHeader(double available)
        {
            var compact = available < 420;
            Grid.SetColumnSpan(heading, compact ? 2 : 1);
            Grid.SetRow(saveSlot, compact ? 1 : 0);
            Grid.SetColumn(saveSlot, compact ? 1 : 2);
            Grid.SetColumnSpan(saveSlot, compact ? 2 : 1);
            header.RowSpacing = compact ? 6 : 0;
        }
        FitHeader(width);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = header,
            Content = scroller,
            CloseButtonText = L("Închideți", "Close"),
            // Enter in a text box shouldn't close the dialog.
            DefaultButton = ContentDialogButton.None,
            RequestedTheme = ActualTheme,
            Language = UiLanguage,
        };
        dialog.Resources["ContentDialogMaxWidth"] = width + 64;
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            var available = Math.Clamp(sender.Size.Width - 96, 240, preferredWidth);
            header.Width = scroller.Width = available;
            FitHeader(available);
            scroller.MaxHeight = DialogBodyHeight(sender);
            dialog.Resources["ContentDialogMaxWidth"] = available + 64;
        }
        root.Changed += Resized;
        scroller.Opacity = 0;
        dialog.Opened += (_, _) =>
        {
            if (!AnimationsEnabled) { scroller.Opacity = 1; return; }
            var fade = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(fade, scroller);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var entrance = new Storyboard();
            entrance.Children.Add(fade);
            entrance.Completed += (_, _) => scroller.Opacity = 1;
            entrance.Begin();
        };

        // A surface's load token can already be cancelled after returning from
        // a subpage. Dialog lifetime follows explicit navigation/close instead.
        using var cancellation = new CancellationTokenSource();
        _tileDialogCancellation = cancellation;
        _tileDialogOwner = owner;
        _tileDialog = dialog;
        _dialogStatus = status;
        _dialogSaveIndicator = saveIndicator;
        _dialogToken = cancellation.Token;
        var previousButtonContent = sourceButton?.Content;
        var buttonProgress = new ProgressRing { IsActive = true, Width = 16, Height = 16 };
        void RestoreButton()
        {
            buttonProgress.IsActive = false;
            if (sourceButton is not null && ReferenceEquals(sourceButton.Content, buttonProgress))
            {
                sourceButton.Content = previousButtonContent;
                sourceButton.IsEnabled = true;
            }
        }
        if (sourceButton is not null)
        {
            sourceButton.IsEnabled = false;
            sourceButton.Content = buttonProgress;
        }
        try
        {
            // Open at the final size, with controls already populated. The gear
            // provides loading feedback without showing a resizable empty popup.
            await FillTileSettingsAsync(key, content, loading, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            RestoreButton();
            await dialog.ShowAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog was already open; only one can be at a time.
        }
        finally
        {
            cancellation.Cancel();
            RestoreButton();
            root.Changed -= Resized;
            foreach (var cleanup in _dialogCleanups) cleanup();
            _dialogCleanups.Clear();
            _tileDialog = null;
            _tileDialogCancellation = null;
            _tileDialogOwner = null;
            _dialogStatus = null;
            _dialogSaveIndicator = null;
            _ = ReloadTileLinesAsync();
        }
    }

    // Room for the title and the Close button, within the dialog's own 756px cap.
    private static double DialogBodyHeight(XamlRoot root) => Math.Clamp(root.Size.Height - 260, 160, 540);

    private async Task FillTileSettingsAsync(string key, StackPanel content, ProgressRing loading, CancellationToken token)
    {
        try
        {
            ClockDeviceState state;
            while (true)
            {
                // The list remains usable while the gear is loading. An edit
                // made during these reads invalidates the whole snapshot too.
                while (_pendingSaves.Count > 0 || _savesRunning > 0)
                    await Task.Delay(50, token);
                token.ThrowIfCancellationRequested();
                var revision = _settingsRevision;
                state = _linesState is { } recent &&
                    DateTime.UtcNow - _linesStateUpdatedAt < TimeSpan.FromSeconds(3)
                        ? recent : await ClockDeviceApi.GetStateAsync(_device, token);
                _nowPlayingState = key is NowPlayingTileKey or NowPlayingPriorityKey ? await TryGetJsonAsync("/npstate", token) : null;
                _weatherState = key == "4" ? await TryGetJsonAsync("/weatherstate", token) : null;
                _weatherKey = key == "4" ? (await TryGetJsonAsync("/weatherkey", token))?.String("key") : null;
                _youtubeKey = key == "12" ? (await TryGetJsonAsync("/socialkey?i=0", token))?.String("key") : null;
                token.ThrowIfCancellationRequested();
                if (revision == _settingsRevision && _pendingSaves.Count == 0 && _savesRunning == 0) break;
            }
            content.Children.Remove(loading);
            _buildTarget = content;
            try
            {
                BuildTileSettings(key, state);
            }
            finally
            {
                _buildTarget = null;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            content.Children.Remove(loading);
            ShowError(FriendlyError(error));
        }
    }

    private async Task<ClockDeviceState?> TryGetJsonAsync(string path, CancellationToken token)
    {
        try
        {
            var state = await ClockDeviceApi.GetJsonAsync(_device, path, token);
            token.ThrowIfCancellationRequested();
            return state;
        }
        catch when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    private void BuildTileSettings(string key, ClockDeviceState state)
    {
        if (key == NowPlayingPriorityKey) key = NowPlayingTileKey;

        // How long a tile stays up only means something while it takes turns in the circuit.
        if (_circuitTiles?.TryGetValue(key, out var tile) == true && !(key == NowPlayingTileKey && _nowPlayingIsPriority))
        {
            AddSection(L("Afișare", "Display"));
            Add(DurationCard(key, tile.Duration));
        }

        Action<ClockDeviceState>? content = key switch
        {
            "0" => BuildHourContent,
            "1" => BuildDateContent,
            "2" => BuildTemperatureContent,
            NowPlayingTileKey => BuildNowPlayingContent,
            "4" => BuildWeatherContent,
            "5" => BuildMementoContent,
            "6" => BuildCanvasContent,
            "9" => BuildScreenSaverContent,
            "10" => BuildHourWeekContent,
            "11" => BuildCurrencyContent,
            "12" => BuildYoutubeContent,
            "14" => BuildAfterburnerContent,
            "15" => BuildCountdownContent,
            "alarm" => BuildAlarmContent,
            "stopwatch" => BuildStopwatchContent,
            "timer" => BuildTimerContent,
            "volume" => BuildVolumeContent,
            "voicemeeter" => BuildVoicemeeterContent,
            "obs" => BuildObsContent,
            "webaccess" => BuildWebAlertsContent,
            "ets2" => state => BuildSpeedTileContent(state, "ets2"),
            "beamng" => state => BuildSpeedTileContent(state, "beamng"),
            "assetto" => state => BuildSpeedTileContent(state, "assetto"),
            "nfsu2" => state => BuildSpeedTileContent(state, "nfsu2"),
            "nfsmw" => state => BuildSpeedTileContent(state, "nfsmw"),
            "nfsc" => state => BuildSpeedTileContent(state, "nfsc"),
            "browserdownloads" or "qbittorrent" or "bambu" => state => BuildBarStyleContent(state, key),
            _ => null,
        };
        if (content is not null)
        {
            var headingIndex = BuildTarget.Children.Count;
            AddSection(L("Conținut", "Content"));
            content(state);
            // Settings that bring headings of their own don't need the general one over them,
            // which would otherwise sit empty above the first of theirs.
            if (BuildTarget.Children.Count > headingIndex + 1 &&
                BuildTarget.Children[headingIndex + 1] is FrameworkElement { Tag: SectionTag })
            {
                BuildTarget.Children.RemoveAt(headingIndex);
                if (headingIndex == 0 && BuildTarget.Children[0] is TextBlock first)
                    first.Margin = new Thickness(first.Margin.Left, 4, first.Margin.Right, first.Margin.Bottom);
            }
        }
        if (BuildTarget.Children.Count == 0)
            Add(Secondary(L("Tile-ul acesta nu are alte setări. Cum arată se schimbă din Aspect, în Manager de tile-uri.",
                            "This tile has no other settings. How it looks is changed under Appearance, in the Tile Manager.")));
        // Always the last part - except for the canvas, whose editor is the panel itself.
        if (key != "6") AddTilePreview(key);
    }

    // Browser Downloads', qBittorrent's and Bambu Studio's bar, as the web
    // interface has it behind the tile's gear.
    private static string BarStyleKey(string tile) => tile switch
    {
        "browserdownloads" => "bdBarStyle",
        "qbittorrent" => "qbitBarStyle",
        _ => "bambuBarStyle",
    };

    private void BuildBarStyleContent(ClockDeviceState state, string tile)
    {
        var key = BarStyleKey(tile);
        var style = ChoiceBox(
            [
                (L("Progres", "Progress"), "0"),
                (L("Progres cu chenar", "Framed progress"), "1"),
                (L("Progres cu chenar fără colțuri", "Framed progress without corners"), "2"),
            ],
            Str(state.Int(key)), 260);
        style.SelectionChanged += (_, _) => QueueSave("barstyle:" + key, token => Post(token, "/barstyle", (key, ChoiceValue(style))), 0);
        Add(SettingsCard("\uE9D9", L("Stil bară", "Bar style"),
            L("Două rânduri care se umplu pe măsură ce avansează progresul. Chenarul arată limita de 100% și poate avea colțurile decupate.",
              "Two rows fill as progress increases. An optional frame marks 100% and can have its corners cut out."),
            style));
    }

    private Border DurationCard(string key, int seconds)
    {
        var box = new NumberBox
        {
            Minimum = 1,
            Maximum = 3600,
            SmallChange = 1,
            LargeChange = 5,
            Value = seconds,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 120,
        };
        AutomationProperties.SetName(box, L("Durată în secunde", "Duration in seconds"));
        box.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue))
            {
                box.Value = _circuitTiles?.GetValueOrDefault(key)?.Duration ?? seconds;
                return;
            }
            SetCircuitDuration(key, Math.Clamp((int)Math.Round(args.NewValue), 1, 3600));
        };
        // The clock takes a tile list only with every duration in this range.
        return SettingsCard("", L("Timp afișare", "Display Time"),
            L("Între 1 și 3600 de secunde", "Between 1 and 3600 seconds"),
            Inline(box, new TextBlock { Text = "s", Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center }));
    }

    // Asks the clock again every so often for as long as the dialog is open.
    private void PollWhileOpen(int intervalMs, Func<CancellationToken, Task> poll)
    {
        var token = _dialogToken;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        var busy = false;
        async void Tick()
        {
            if (busy || token.IsCancellationRequested) return;
            busy = true;
            try
            {
                await poll(token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                busy = false;
            }
        }
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
        _dialogCleanups.Add(timer.Stop);
    }

    // A one-off command such as start or stop, followed by a fresh look at the result.
    private async Task RunCommandAsync(Button button, string path, Func<CancellationToken, Task> refresh)
    {
        button.IsEnabled = false;
        try
        {
            // A just-edited duration must reach the clock before Start does.
            while (_pendingSaves.Count > 0 || _savesRunning > 0)
                await Task.Delay(50, _dialogToken);
            _dialogToken.ThrowIfCancellationRequested();
            await Post(CancellationToken.None, path);
            await refresh(_dialogToken);
        }
        catch (OperationCanceledException)
        {
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

    // ------------------------------------------------------------------ stopwatch and timer

    private void BuildStopwatchContent(ClockDeviceState state)
    {
        var time = ValueText("00:00:00:00");
        var parts = ActionCard("\uE916", L("Cronometru", "Stopwatch"),
            L("Pornirea îl ia de la zero și îl arată peste circuit cât timp merge.", "Starting it counts from zero and shows it over the circuit while it runs."),
            L("Pornește", "Start"), "\uE768", time);
        var running = false;
        async Task Refresh(CancellationToken token)
        {
            if (await TryGetJsonAsync("/swstate", token) is not { } stopwatch) return;
            running = stopwatch.Bool("running");
            time.Text = stopwatch.String("text", time.Text);
            SetActionCard(parts, running ? L("Oprește", "Stop") : L("Pornește", "Start"), running ? "\uE71A" : "\uE768");
        }
        parts.Card.Click += (_, _) => _ = RunCommandAsync(parts.Card, running ? "/swstop" : "/swstart", Refresh);
        Add(parts.Card);
        PollWhileOpen(1000, Refresh);
    }

    private void BuildTimerContent(ClockDeviceState state)
    {
        var seconds = Math.Clamp(state.Int("timerDurationSec", 300), 1, 359999);
        var remaining = ValueText(TimerText(seconds));
        var parts = ActionCard("\uE823", "Timer",
            L("Pornește, pune pe pauză sau oprește alarma.", "Start, pause or dismiss the alarm."),
            L("Pornește", "Start"), "\uE768", remaining);
        Add(parts.Card);
        var active = false;
        var settings = new StackPanel { Spacing = 6 };
        var mode = ChoiceBox([(L("Temporizator", "Timer"), "0"), ("Pomodoro", "1")], Str(state.Int("timerMode")));
        if (state.Has("timerMode"))
            settings.Children.Add(SettingsCard("\uE8AB", L("Mod", "Mode"), null, mode));

        var timerGroup = new StackPanel { Spacing = 6 };
        var preset = ChoiceBox([
            (L("5 minute", "5 minutes"), "0"), (L("10 minute", "10 minutes"), "1"),
            (L("25 minute", "25 minutes"), "2"), (L("1 oră", "1 Hour"), "3"),
            (L("Personalizat", "Custom"), "4")], Str(state.Int("timerPreset")));
        timerGroup.Children.Add(SettingsCard("\uE81C", L("Preset", "Preset"), null, preset));
        NumberBox Number(int value, int min, int max) => new()
        {
            Minimum = min, Maximum = max, Value = value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var hours = Number(seconds / 3600, 0, 99);
        var minutes = Number(seconds / 60 % 60, 0, 59);
        var secs = Number(seconds % 60, 0, 59);
        var custom = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 3; i++) custom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var units = new[] { (hours, L("Ore", "Hours")), (minutes, L("Minute", "Minutes")), (secs, L("Secunde", "Seconds")) };
        for (var i = 0; i < units.Length; i++)
        {
            units[i].Item1.Header = units[i].Item2;
            Grid.SetColumn(units[i].Item1, i);
            custom.Children.Add(units[i].Item1);
        }
        var customCard = CardBorder();
        customCard.Padding = new Thickness(14);
        customCard.Child = custom;
        timerGroup.Children.Add(customCard);
        settings.Children.Add(timerGroup);

        var pomoGroup = new StackPanel { Spacing = 10 };
        pomoGroup.Children.Add(Secondary(L("Muncă și pauze alternate, urmate de o pauză lungă. Etapele pornesc automat.",
            "Work and breaks alternate, followed by a long break. Each stage starts automatically.")));
        var fields = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var options = new[]
        {
            ("pomoWork", L("Muncă (minute)", "Work (minutes)"), "\uE821", 1, 120, 25),
            ("pomoShort", L("Pauză (minute)", "Break (minutes)"), "\uE708", 1, 60, 5),
            ("pomoLong", L("Pauză lungă (minute)", "Long break (minutes)"), "\uE708", 1, 60, 15),
            ("pomoRounds", L("Runde", "Rounds"), "\uE8EE", 2, 8, 4),
        };
        var pomo = new Dictionary<string, NumberBox>();
        for (var i = 0; i < options.Length; i++)
        {
            var (key, label, glyph, min, max, fallback) = options[i];
            var box = Number(state.Int(key, fallback), min, max);
            pomo[key] = box;
            var card = CardBorder();
            card.Padding = new Thickness(12);
            card.Child = new StackPanel { Spacing = 8, Children = {
                Inline(new FontIcon { Glyph = glyph, FontSize = 16 }, new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }), box } };
            AutomationProperties.SetName(box, label);
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            fields.Children.Add(card);
        }
        pomoGroup.Children.Add(fields);
        settings.Children.Add(pomoGroup);
        Add(settings);

        var reset = ActionButton(L("Reporniți ciclul", "Restart the cycle"), "\uE72C");
        reset.Visibility = Visibility.Collapsed;
        Add(reset);
        void UpdateGroups()
        {
            var isPomo = state.Has("timerMode") && ChoiceValue(mode) == "1";
            SetExpanded(timerGroup, !isPomo);
            SetExpanded(pomoGroup, isPomo);
            SetExpanded(customCard, ChoiceValue(preset) == "4");
        }
        int Duration() => ChoiceValue(preset) switch
        {
            "0" => 300, "1" => 600, "2" => 1500, "3" => 3600,
            _ => Math.Clamp((int)hours.Value * 3600 + (int)minutes.Value * 60 + (int)secs.Value, 1, 359999),
        };
        void Save()
        {
            if (active || new[] { hours, minutes, secs }.Concat(pomo.Values).Any(box => double.IsNaN(box.Value))) return;
            UpdateGroups();
            remaining.Text = TimerText(ChoiceValue(mode) == "1" ? (int)pomo["pomoWork"].Value * 60 : Duration());
            QueueSave("timer-settings", token => Post(token, "/timersett",
                ("preset", ChoiceValue(preset)), ("durationSec", Str(Duration())), ("mode", ChoiceValue(mode)),
                ("pomoWork", Str((int)pomo["pomoWork"].Value)), ("pomoShort", Str((int)pomo["pomoShort"].Value)),
                ("pomoLong", Str((int)pomo["pomoLong"].Value)), ("pomoRounds", Str((int)pomo["pomoRounds"].Value))), 400);
        }
        preset.SelectionChanged += (_, _) => Save();
        mode.SelectionChanged += (_, _) => Save();
        foreach (var box in new[] { hours, minutes, secs }.Concat(pomo.Values))
            box.ValueChanged += (_, _) => Save();
        UpdateGroups();

        async Task Refresh(CancellationToken token)
        {
            if (await TryGetJsonAsync("/timerstate", token) is not { } timer) return;
            var running = timer.Bool("running");
            var finished = timer.Bool("finished");
            active = running || finished;
            settings.IsHitTestVisible = !active;
            foreach (var control in new Control[] { mode, preset, hours, minutes, secs }.Concat(pomo.Values))
                control.IsEnabled = !active;
            settings.Opacity = active ? 0.6 : 1;
            remaining.Text = timer.Int("mode") == 1 && (running || timer.Bool("inCycle"))
                ? PomodoroPart(timer) + " · " + TimerText(timer.Int("remainingSec"))
                : timer.String("text", remaining.Text);
            SetExpanded(reset, timer.Int("mode") == 1 && timer.Bool("inCycle") && !running);
            SetActionCard(parts, finished ? L("Oprește", "Dismiss") : running ? L("Pauză", "Pause") : L("Pornește", "Start"),
                finished ? "\uE71A" : running ? "\uE769" : "\uE768");
        }
        parts.Card.Click += (_, _) => _ = RunCommandAsync(parts.Card, active ? "/timerpause" : "/timerstart", Refresh);
        reset.Click += (_, _) => _ = RunCommandAsync(reset, "/timersett?reset=1", Refresh);
        PollWhileOpen(1000, Refresh);
    }

    // "Muncă 2/4", "Pauză", "Pauză lungă" - the part a Pomodoro is in.
    private static string PomodoroPart(ClockDeviceState timer) => timer.Int("phase") switch
    {
        0 => L("Muncă ", "Work ") + Str(timer.Int("round", 1)) + "/" + Str(timer.Int("rounds", 4)),
        1 => L("Pauză", "Break"),
        _ => L("Pauză lungă", "Long break"),
    };

    // ------------------------------------------------------------------ YouTube

    private void BuildYoutubeContent(ClockDeviceState state)
    {
        // What the clock ran into, when it says so, rather than "not read yet".
        var reading = state.Int("soc0Err") switch
        {
            1 => L("Eroare: cheie API lipsă", "Error: API key missing"),
            2 => L("Eroare: canal lipsă", "Error: channel missing"),
            3 => L("Eroare: cheie API refuzată", "Error: API key refused"),
            4 => L("Eroare: canal negăsit", "Error: channel not found"),
            5 => L("Eroare: fără răspuns", "Error: no answer"),
            _ => null,
        } ?? (state.Bool("soc0Valid")
            ? L($"Ultima citire: {state.Int("soc0Count"):N0} abonați.", $"Last reading: {state.Int("soc0Count"):N0} subscribers.")
            : L("Numărul de abonați nu a fost citit încă.", "The subscriber count hasn't been read yet."));
        AddSection(L("Canal", "Channel"), reading);
        var savedHandle = state.String("soc0Handle").Trim();
        var handle = new TextBox { Text = savedHandle, MaxLength = 64, PlaceholderText = "@canal" };
        var channel = SaveFieldCard("\uE716", L("Canalul urmărit", "Channel followed"),
            L("Handle-ul (@canal), linkul sau ID-ul canalului.", "The channel's handle (@channel), link or ID."),
            handle,
            () => handle.Text.Trim() != savedHandle,
            () =>
            {
                var value = handle.Text.Trim();
                QueueSave("social0-handle", token => Post(token, "/socialsett", ("i", "0"), ("handle", value)), 0);
                savedHandle = value;
                return Task.CompletedTask;
            });
        handle.TextChanged += (_, _) => channel.Refresh();
        OnEnter(handle, channel.Submit);
        Add(channel.Card);

        AddSection(L("Cheie API", "API key"));
        var savedKey = _youtubeKey;
        var (keyView, key) = SecretField(savedKey ?? "", L("Cheie YouTube Data API", "YouTube Data API key"));
        bool KeyChanged()
        {
            var typed = key.Password.Trim();
            return savedKey is null ? typed.Length > 0 : typed != savedKey;
        }
        var keyField = SaveFieldCard("\uE8D7", L("Cheie YouTube Data API", "YouTube Data API key"),
            savedKey is null && state.Bool("soc0HasKey")
                ? L("O cheie e salvată pe ceas, dar nu a putut fi citită. Scrie-o din nou doar dacă vrei s-o schimbi.", "A key is saved on the clock but couldn't be read. Only enter it again to change it.")
                : L("Cheia cu care ceasul citește numărul de abonați. Golește câmpul și salvează ca s-o ștergi.", "The key the clock reads the subscriber count with. Empty the field and save to remove it."),
            keyView,
            KeyChanged,
            () =>
            {
                var typed = key.Password.Trim();
                // An emptied box clears the key - but only one that was read, so a failed read never wipes it.
                if (typed.Length > 0)
                    QueueSave("social0-key", token => Post(token, "/socialsett", ("i", "0"), ("key", typed)), 0);
                else
                    QueueSave("social0-key", token => Post(token, "/socialsett", ("i", "0"), ("clearkey", "1")), 0);
                savedKey = typed;
                return Task.CompletedTask;
            });
        key.PasswordChanged += (_, _) => keyField.Refresh();
        OnEnter(key, keyField.Submit);
        Add(keyField.Card);

        AddSection(L("Opțiuni", "Options"));
        var showName = Toggle(state.Bool("soc0ShowName"));
        showName.Toggled += (_, _) => QueueSave("social0-name", token => Post(token, "/socialsett",
            ("i", "0"), ("showname", showName.IsOn ? "1" : "0")), 0);
        Add(SettingsCard("\uE77B", L("Arată numele canalului", "Show the channel name"),
            L("Numele apare lângă numărul de abonați.", "The name shows next to the subscriber count."), showName));
    }

    // ------------------------------------------------------------------ alarms

    private sealed class AlarmSlot
    {
        public int Hour { get; set; } = 7;
        public int Minute { get; set; }
        public int Days { get; set; } = 127;
        public bool Enabled { get; set; }
        public bool Present { get; set; }
        public string Tone { get; set; } = "classic";
    }

    private const int AlarmCount = 3;

    private void BuildAlarmContent(ClockDeviceState state)
    {
        var saved = state.Objects("alarms");
        var slots = Enumerable.Range(0, AlarmCount).Select(index =>
        {
            var alarm = index < saved.Count ? saved[index] : null;
            return new AlarmSlot
            {
                Hour = alarm?.Int("h", 7) ?? 7,
                Minute = alarm?.Int("m", 30) ?? 30,
                Days = alarm?.Int("d", 31) ?? 31,
                Enabled = alarm?.Bool("en") ?? false,
                Present = alarm?.Bool("p") ?? false,
                Tone = alarm?.String("tone", "classic") ?? "classic",
            };
        }).ToArray();

        var host = new StackPanel { Spacing = 4 };
        Add(host);

        void Save(int index, bool preview)
        {
            var slot = slots[index];
            var values = new List<(string Key, string Value)>
            {
                ("idx", Str(index)), ("hour", Str(slot.Hour)), ("minute", Str(slot.Minute)), ("days", Str(slot.Days)),
                ("enabled", slot.Enabled ? "1" : "0"), ("present", slot.Present ? "1" : "0"), ("tone", slot.Tone),
            };
            // The clock plays the tone; the save carries the request.
            if (preview) values.Add(("preview", "1"));
            QueueSave("alarm:" + index, token => Post(token, "/alarmsett", values.ToArray()), preview ? 0 : 300);
        }

        void Render(int expandedIndex)
        {
            host.Children.Clear();
            var shown = 0;
            for (var index = 0; index < slots.Length; index++)
            {
                if (!slots[index].Present) continue;
                shown++;
                var slotIndex = index;
                host.Children.Add(AlarmExpander(slots[slotIndex], slotIndex == expandedIndex, preview => Save(slotIndex, preview), () =>
                {
                    slots[slotIndex].Present = false;
                    slots[slotIndex].Enabled = false;
                    Save(slotIndex, false);
                    Render(-1);
                }));
            }
            var add = ActionButton(
                shown < AlarmCount ? L("Adăugați o alarmă", "Add an alarm") : L("Toate cele 3 alarme sunt adăugate", "All 3 alarms are added"),
                "");
            add.IsEnabled = shown < AlarmCount;
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            add.Click += (_, _) =>
            {
                var free = Array.FindIndex(slots, slot => !slot.Present);
                if (free < 0) return;
                // A fresh alarm rather than whatever the slot last held: every day, on, 07:00.
                slots[free] = new AlarmSlot { Hour = 7, Minute = 0, Days = 127, Enabled = true, Present = true };
                Save(free, false);
                Render(free);
            };
            host.Children.Add(add);
        }

        Render(-1);
        _ = OfferAlarmStopAsync(host);
    }

    private Expander AlarmExpander(AlarmSlot slot, bool expanded, Action<bool> save, Action delete)
    {
        var enabled = Toggle(slot.Enabled);
        var time = new TimePicker { ClockIdentifier = "24HourClock", MinuteIncrement = 1, Time = new TimeSpan(slot.Hour, slot.Minute, 0) };
        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var tone = ChoiceBox(AlarmTones(), slot.Tone, 200);
        var listen = ActionButton(L("Ascultă", "Listen"), "");
        var remove = ActionButton(L("Șterge alarma", "Delete alarm"), "");

        var expander = SettingsExpander(PriorityGlyph("alarm"), AlarmTimeText(slot), AlarmDaysText(slot.Days), enabled,
            [
                SubRow(L("Ora", "Time"), L("Când sună alarma.", "When the alarm rings."), time),
                SubBlock(L("Zile", "Days"), null, days),
                SubRow(L("Ton", "Tone"), null, Inline(tone, listen)),
                SubRow("", null, remove),
            ], expanded);
        var texts = ((Grid)expander.Header).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().ToList();
        void Refresh()
        {
            texts[0].Text = AlarmTimeText(slot);
            if (texts.Count > 1) texts[1].Text = AlarmDaysText(slot.Days);
        }

        enabled.Toggled += (_, _) =>
        {
            slot.Enabled = enabled.IsOn;
            save(false);
        };
        time.TimeChanged += (_, args) =>
        {
            slot.Hour = args.NewTime.Hours;
            slot.Minute = args.NewTime.Minutes;
            Refresh();
            save(false);
        };
        for (var day = 0; day < 7; day++)
        {
            var bit = 1 << day;
            var button = new ToggleButton
            {
                Content = DayShortName(day),
                IsChecked = (slot.Days & bit) != 0,
                MinWidth = 44,
                Padding = new Thickness(6, 4, 6, 4),
            };
            ToolTipService.SetToolTip(button, DayName(day));
            button.Click += (_, _) =>
            {
                slot.Days ^= bit;
                Refresh();
                save(false);
            };
            days.Children.Add(button);
        }
        tone.SelectionChanged += (_, _) =>
        {
            slot.Tone = ChoiceValue(tone);
            save(false);
        };
        listen.Click += (_, _) => save(true);
        remove.Click += (_, _) => delete();
        return expander;
    }

    // Only while one is actually ringing: an alarm going off behind the dialog
    // should be stoppable from it.
    private async Task OfferAlarmStopAsync(StackPanel host)
    {
        if (await TryGetJsonAsync("/alarmstate", _dialogToken) is not { } alarm || !alarm.Bool("ringing")) return;
        var card = ActionCard(PriorityGlyph("alarm"), L("Oprește alarma", "Stop the alarm"),
            L("O alarmă sună acum; o oprești de aici, fără să atingi ceasul.", "An alarm is ringing; stop it from here, without touching the clock."),
            "", null).Card;
        card.Click += async (_, _) =>
        {
            await RunCommandAsync(card, "/alarmstop", _ => Task.CompletedTask);
            host.Children.Remove(card);
        };
        host.Children.Insert(0, card);
    }

    private static string AlarmTimeText(AlarmSlot slot) => $"{slot.Hour:00}:{slot.Minute:00}";

    // Monday first, the order the clock numbers the day bits in.
    private static string AlarmDaysText(int days) => days switch
    {
        127 => L("Zilnic", "Every day"),
        31 => L("De luni până vineri", "Monday to Friday"),
        96 => L("În weekend", "Weekends"),
        0 => L("Nicio zi aleasă", "No days chosen"),
        _ => string.Join(", ", Enumerable.Range(0, 7).Where(day => (days & (1 << day)) != 0).Select(DayShortName)),
    };

    private static string DayShortName(int day) => AppStrings.IsEnglish
        ? new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" }[day]
        : new[] { "Lu", "Ma", "Mi", "Jo", "Vi", "Sâ", "Du" }[day];

    private static string DayName(int day) => AppStrings.IsEnglish
        ? new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }[day]
        : new[] { "Luni", "Marți", "Miercuri", "Joi", "Vineri", "Sâmbătă", "Duminică" }[day];
}
