using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;

namespace OctoglowSender;

public sealed partial class ClockNativeSettingsPage
{
    // The firmware keeps exactly this many tiles in the circuit and refuses a
    // list with one missing (Octoglow.ino, handleSettings).
    private const int CircuitTileCount = 15;
    private const string NowPlayingTileKey = "3";
    private const string NowPlayingPriorityKey = "nowplaying";
    // A drag carries a format of our own: plain text would be taken by any text
    // box the row happened to be dropped on.
    private const string DragFormat = "OctoglowTileKey";

    private sealed class CircuitTile
    {
        public int Id { get; init; }
        public bool Enabled { get; set; }
        public int Duration { get; set; }
    }

    // Rows that can be dragged into a new order. Order holds every key - taken
    // out of the list or not, Now Playing in one list or the other - because the
    // clock only takes a complete circuit list and a complete ranking.
    private sealed class ReorderList
    {
        public StackPanel Host { get; } = new() { Spacing = 4 };
        public List<string> Order { get; } = new();
        public Dictionary<string, Border> Rows { get; } = new();
        public Dictionary<string, (Rectangle Before, Rectangle After)> Markers { get; } = new();
        // Now Playing's row in the list it is not in.
        public HashSet<string> Hidden { get; } = new();
        public Action? Changed { get; set; }
        public Button? AddButton { get; set; }
        public required bool IsCircuit { get; init; }
        public required string NowPlayingKey { get; init; }
    }

    private ReorderList? _circuitList;
    private ReorderList? _priorityList;
    private Dictionary<string, CircuitTile>? _circuitTiles;
    // "c:" + circuit id or "p:" + priority key.
    private readonly Dictionary<string, ToggleSwitch> _rowToggles = new();
    private bool _nowPlayingIsPriority;
    // Which tiles are out of the Tile Manager, shared with the web interface's
    // add and remove buttons: one bit per circuit tile id and per priority tile.
    private int _tileHiddenMask;
    private int _prioHiddenMask;
    private ReorderList? _dragList;
    private string? _dragKey;
    private DispatcherQueueTimer? _dragScrollTimer;
    private double _dragPointerY = double.NaN;
    private ScrollViewer? _dragScroller;

    private Action BuildCompleteTileManager(ClockDeviceState state)
    {
        _tileHiddenMask = state.Int("tileHidden");
        _prioHiddenMask = state.Int("prioHidden");
        _nowPlayingIsPriority = state.Bool("nowPlayingIsPriority");
        _rowToggles.Clear();
        _tileRunButtons.Clear();
        _tileLines.Clear();
        _tileErrors.Clear();
        _tileWarns.Clear();
        _linesState = state;
        _linesStateUpdatedAt = DateTime.UtcNow;
        _linesPlaying = null;
        _linesEts2 = null;
        _linesBeamng = null;
        _linesAssetto = null;
        _linesNfsu2 = null;
        _linesNfsMw = null;
        _linesNfsCarbon = null;
        _linesBrowserDownloads = null;
        _linesVoicemeeter = null;
        _linesStopwatch = null;
        _linesAlarm = null;
        _linesTimer = null;
        _linesObs = null;
        _linesQbit = null;
        _linesBambu = null;
        _linesAb = null;
        // The web interface's Tile Manager opens on its display settings.
        BuildAppearanceSection(state);
        BuildCircuitSection(state);
        BuildPrioritySection(state);
        AddLookingElseLinks(
            (ClockSettingsSection.Buzzer, L("Tonuri buzzer", "Buzzer Tones")),
            (ClockSettingsSection.Touch, L("Acțiuni senzor tactil", "Touch Sensor Actions")));
        return StartTileLines();
    }

    // ------------------------------------------------------------------ lists

    private void BuildCircuitSection(ClockDeviceState state)
    {
        var tiles = state.Tiles().Select(tile => new CircuitTile
        {
            Id = tile.Id,
            Enabled = tile.Enabled,
            Duration = tile.Duration,
        }).ToList();
        // Older firmware has fewer: no countdown (cdEvents), no MSI Afterburner (abMode),
        // and before that no battery tile.
        var expectedCount = state.Has("cdEvents") ? CircuitTileCount
            : state.Has("abMode") ? CircuitTileCount - 1
            : state.Has("iconSelBattery") ? CircuitTileCount - 2 : CircuitTileCount - 3;
        var valid = tiles.Count == expectedCount
                    && tiles.Select(tile => tile.Id).Distinct().Count() == expectedCount
                    && tiles.All(tile => TileGlyph(tile.Id) is not null);

        AddSection(L("Tile-uri circuit", "Circuit Tiles"),
            L("Trageți un rând pentru a schimba ordinea. Rotița deschide setările tile-ului, coșul îl scoate din listă.",
              "Drag a row to change the order. The gear opens the tile's settings, the bin takes it out of the list."));

        _circuitList = null;
        _circuitTiles = null;
        if (!valid)
        {
            // Better nothing than a guessed list: saving one would overwrite the clock's real order.
            Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = L("Ordinea nu poate fi editată", "The order can't be edited"),
                Message = L("Ceasul a trimis o listă de tile-uri pe care aplicația nu o recunoaște. Actualizează firmware-ul ceasului și reîncarcă pagina.",
                            "The clock sent a tile list this app doesn't recognise. Update the clock's firmware and refresh the page."),
            });
            return;
        }

        var circuit = new ReorderList { IsCircuit = true, NowPlayingKey = NowPlayingTileKey };
        var byKey = tiles.ToDictionary(tile => Str(tile.Id));
        _circuitList = circuit;
        _circuitTiles = byKey;
        foreach (var tile in tiles)
        {
            var key = Str(tile.Id);
            circuit.Order.Add(key);
            var toggle = Toggle(tile.Enabled);
            toggle.Toggled += (_, _) => SetCircuitEnabled(key, toggle.IsOn);
            _rowToggles["c:" + key] = toggle;
            SetTileLine("c:" + key, AddReorderRow(circuit, key, TileGlyph(tile.Id)!, TileName(tile.Id), TileLine("c:" + key), toggle, key));
            SetRowDimmed(circuit.Rows[key], !tile.Enabled);
        }
        if (_nowPlayingIsPriority) circuit.Hidden.Add(NowPlayingTileKey);
        circuit.Changed = () => QueueSave("circuit", token => SaveCircuitAsync(circuit, byKey, token), 250);
        EnableListDrop(circuit);
        SyncRows(circuit);
        Add(circuit.Host);
        Add(AddTileButton(circuit));
    }

    private void BuildPrioritySection(ClockDeviceState state)
    {
        AddSection(L("Tile-uri prioritare", "Priority Tiles"),
            L("Apar peste circuit cât timp au ceva de arătat; când sunt mai multe active, câștigă cel mai de sus. Redare curentă stă fie aici, fie în circuit: trageți-o dintr-o listă în cealaltă.",
              "They show over the circuit while they have something to show; when several are active, the highest one wins. Now Playing sits either here or in the circuit: drag it from one list to the other."));

        var priority = new ReorderList { IsCircuit = false, NowPlayingKey = NowPlayingPriorityKey };
        _priorityList = priority;
        foreach (var key in PriorityKeys(state))
        {
            priority.Order.Add(key);
            var toggle = key switch
            {
                "notif" => FeatureToggle(state, "notifEnabled", "/notiftoggle", key),
                "browserdownloads" => FeatureToggle(state, "browserDownloadsEnabled", "/browserdownloadtoggle", key),
                "qbittorrent" => FeatureToggle(state, "qbitEnabled", "/qbittoggle", key),
                "bambu" => FeatureToggle(state, "bambuEnabled", "/bambutoggle", key),
                "ets2" => FeatureToggle(state, "ets2Enabled", "/ets2toggle", key),
                "beamng" => FeatureToggle(state, "beamngEnabled", "/beamngtoggle", key),
                "assetto" => FeatureToggle(state, "assettoEnabled", "/assettotoggle", key),
                "nfsu2" => FeatureToggle(state, "nfsu2Enabled", "/nfsu2toggle", key),
                "nfsmw" => FeatureToggle(state, "nfsmwEnabled", "/nfsmwtoggle", key),
                "nfsc" => FeatureToggle(state, "nfscEnabled", "/nfsctoggle", key),
                "gamefps" => FeatureToggle(state, "gameFpsEnabled", "/gamefpstoggle", key),
                "volume" => FeatureToggle(state, "volumeEnabled", "/volumetoggle", key),
                "voicemeeter" => FeatureToggle(state, "voicemeeterEnabled", "/voicemeetertoggle", key),
                "obs" => FeatureToggle(state, "obsEnabled", "/obstoggle", key),
                "webaccess" => FeatureToggle(state, "webEnabled", "/webtoggle", key),
                // Firmware from before the alert had a switch keeps it on while it is listed.
                "lowbattery" when state.Has("lowBatteryEnabled") => FeatureToggle(state, "lowBatteryEnabled", "/lowbatterytoggle", key),
                NowPlayingPriorityKey => NowPlayingPriorityToggle(),
                _ => null,
            };
            if (toggle is not null) _rowToggles["p:" + key] = toggle;
            // Notifications have nothing to set beyond their switch here. The
            // games' tiles have their look - unit, gauge - the progress tiles their
            // bar, and the web interface's alerts a switch each, on a clock whose
            // firmware has them.
            var settingsKey = key is "notif" or "gamefps" or "lowbattery" ? null
                : key is "webaccess" && !state.Has("webAlAccess") ? null
                : key is "ets2" or "beamng" or "assetto" or "nfsu2" or "nfsmw" or "nfsc" && !state.Has(key + "Mph") ? null
                : key is "browserdownloads" or "qbittorrent" or "bambu" && !state.Has(BarStyleKey(key)) ? null
                : key;
            SetTileLine("p:" + key, AddReorderRow(priority, key, PriorityGlyph(key), PriorityName(key), TileLine("p:" + key), toggle, settingsKey));
            if (toggle is not null) SetRowDimmed(priority.Rows[key], !toggle.IsOn);
        }
        if (!_nowPlayingIsPriority) priority.Hidden.Add(NowPlayingPriorityKey);
        priority.Changed = () => QueueSave("priority", token => Post(token, "/priorityorder",
            ("order", string.Join(',', priority.Order)),
            ("npPriority", _nowPlayingIsPriority ? "1" : "0")), 250);
        EnableListDrop(priority);
        SyncRows(priority);
        Add(priority.Host);
        Add(AddTileButton(priority));
    }

    private ToggleSwitch FeatureToggle(ClockDeviceState state, string stateKey, string path, string key)
    {
        var toggle = Toggle(state.Bool(stateKey, true));
        toggle.Toggled += (_, _) =>
        {
            if (_priorityList?.Rows.TryGetValue(key, out var row) == true) SetRowDimmed(row, !toggle.IsOn);
            QueueSave(path, token => Post(token, path, ("enabled", toggle.IsOn ? "1" : "0")), 0);
        };
        return toggle;
    }

    // Now Playing's switch among the priority tiles is the circuit tile's own:
    // the clock only shows it over the others while that tile is switched on.
    private ToggleSwitch? NowPlayingPriorityToggle()
    {
        if (_circuitTiles is null || !_circuitTiles.TryGetValue(NowPlayingTileKey, out var tile)) return null;
        var toggle = Toggle(tile.Enabled);
        toggle.Toggled += (_, _) => SetCircuitEnabled(NowPlayingTileKey, toggle.IsOn);
        return toggle;
    }

    private void SetCircuitEnabled(string key, bool on)
    {
        if (_circuitTiles is null || !_circuitTiles.TryGetValue(key, out var tile) || tile.Enabled == on) return;
        tile.Enabled = on;
        // A switch set from here calls back in, and stops at the check above.
        SyncToggle("c:" + key, on);
        if (_circuitList?.Rows.TryGetValue(key, out var row) == true) SetRowDimmed(row, !on);
        if (key == NowPlayingTileKey)
        {
            SyncToggle("p:" + NowPlayingPriorityKey, on);
            if (_priorityList?.Rows.TryGetValue(NowPlayingPriorityKey, out var priorityRow) == true) SetRowDimmed(priorityRow, !on);
        }
        _circuitList?.Changed?.Invoke();
    }

    private void SetCircuitDuration(string key, int seconds)
    {
        if (_circuitTiles is null || !_circuitTiles.TryGetValue(key, out var tile) || tile.Duration == seconds) return;
        tile.Duration = seconds;
        _circuitList?.Changed?.Invoke();
    }

    private void SyncToggle(string id, bool on)
    {
        if (_rowToggles.TryGetValue(id, out var toggle) && toggle.IsOn != on) toggle.IsOn = on;
    }

    private Task SaveCircuitAsync(ReorderList circuit, Dictionary<string, CircuitTile> tiles, CancellationToken token)
    {
        // The whole list every time: the clock only takes all twelve at once.
        var values = new List<(string Key, string Value)>();
        for (var index = 0; index < circuit.Order.Count; index++)
        {
            var tile = tiles[circuit.Order[index]];
            values.Add(($"id{index}", Str(tile.Id)));
            values.Add(($"en{index}", tile.Enabled ? "1" : "0"));
            values.Add(($"dur{index}", Str(Math.Clamp(tile.Duration, 1, 3600))));
        }
        return Post(token, "/settings", values.ToArray());
    }

    // grip | icon | name | gear | bin | switch. The last three columns keep their
    // width on rows without them, so gears, bins and switches line up.
    private TextBlock? AddReorderRow(
        ReorderList list,
        string key,
        string glyph,
        string title,
        string? description,
        ToggleSwitch? toggle,
        string? settingsKey)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 40 };
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

        var grip = new FontIcon { Glyph = "", FontSize = 14, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(grip, L("Trage ca să muți", "Drag to move"));
        grid.Children.Add(grip);

        // A tile fed by an online service carries an exclamation mark on the
        // corner of its icon while that service gives it nothing, and its line
        // reads what is wrong (RefreshTileLines), as on the web interface.
        var id = (list.IsCircuit ? "c:" : "p:") + key;
        var online = ApiTileIds.Contains(id);
        FrameworkElement icon = GlyphIcon(glyph);
        if (online)
        {
            var mark = Themed<Border>(
                $"<Border {XamlNs} Background='{{ThemeResource SystemFillColorCriticalBrush}}' Width='14' Height='14' CornerRadius='7' " +
                "HorizontalAlignment='Right' VerticalAlignment='Bottom' Margin='0,0,-6,-5' Visibility='Collapsed'/>");
            mark.Child = new TextBlock
            {
                Text = "!",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
            };
            _tileWarns[id] = mark;
            icon = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { icon, mark } };
        }
        icon.Tag = DimTag;
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Tag = DimTag };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        TextBlock? subtitle = null;
        if (!string.IsNullOrWhiteSpace(description))
        {
            subtitle = Secondary(description);
            text.Children.Add(subtitle);
        }
        if (online && subtitle is not null)
        {
            var failure = Themed<TextBlock>(
                $"<TextBlock {XamlNs} Foreground='{{ThemeResource SystemFillColorCriticalBrush}}' TextTrimming='CharacterEllipsis' Visibility='Collapsed'/>");
            failure.Style = Resource<Style>("CaptionTextBlockStyle");
            text.Children.Add(failure);
            _tileErrors[id] = failure;
        }
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        // Whatever a row has sits against its right edge, in the same order:
        // a row with no switch or no settings leaves no gap where they would be.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (!list.IsCircuit && key is "stopwatch" or "timer" or "alarm")
            actions.Children.Add(CreateTileRunButton(key));
        if (settingsKey is not null) actions.Children.Add(SettingsButton(settingsKey, title));

        var remove = TransparentIconButton("\uE74D", 16);
        ToolTipService.SetToolTip(remove, L("Scoate din listă", "Remove from the list"));
        AutomationProperties.SetName(remove, L("Scoate din listă: ", "Remove from the list: ") + title);
        remove.Click += (_, _) => _ = ConfirmRemoveTileAsync(list, key, title);
        actions.Children.Add(remove);

        if (toggle is not null)
        {
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(8, 0, -12, 0);
            ToolTipService.SetToolTip(toggle, L("Pornit sau oprit", "On or off"));
            AutomationProperties.SetName(toggle, title);
            actions.Children.Add(toggle);
        }
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        var before = DropMarker(VerticalAlignment.Top);
        var after = DropMarker(VerticalAlignment.Bottom);
        grid.Children.Add(before);
        grid.Children.Add(after);
        list.Markers[key] = (before, after);

        var row = CardBorder();
        row.Padding = new Thickness(10, 8, 16, 8);
        row.Child = grid;
        row.CanDrag = true;
        row.AllowDrop = true;
        AutomationProperties.SetName(row, title);

        row.DragStarting += (_, args) =>
        {
            _dragList = list;
            _dragKey = key;
            args.AllowedOperations = DataPackageOperation.Move;
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetData(DragFormat, key);
            row.Opacity = 0.5;
            StartDragScroll();
        };
        row.DropCompleted += (_, _) =>
        {
            row.Opacity = 1;
            EndDrag();
        };
        row.DragOver += (_, args) =>
        {
            args.Handled = true;
            if (!CanDrop(list, key))
            {
                args.AcceptedOperation = DataPackageOperation.None;
                HideAllMarkers();
                return;
            }
            args.AcceptedOperation = DataPackageOperation.Move;
            SetDragCaption(args);
            var below = args.GetPosition(row).Y > row.ActualHeight / 2;
            HideAllMarkers();
            (below ? after : before).Visibility = Visibility.Visible;
        };
        row.DragLeave += (_, _) =>
        {
            before.Visibility = Visibility.Collapsed;
            after.Visibility = Visibility.Collapsed;
        };
        row.Drop += (_, args) =>
        {
            args.Handled = true;
            HideAllMarkers();
            DropOn(list, key, args.GetPosition(row).Y > row.ActualHeight / 2);
        };

        list.Rows[key] = row;
        return subtitle;
    }

    private Button SettingsButton(string settingsKey, string title)
    {
        var button = TransparentIconButton("", 16);
        ToolTipService.SetToolTip(button, L("Setările tile-ului", "Tile settings"));
        AutomationProperties.SetName(button, L("Setări pentru ", "Settings for ") + title);
        button.Click += (_, _) => _ = ShowTileSettingsAsync(settingsKey, button);
        return button;
    }

    private static Button TransparentIconButton(string glyph, double size) => new()
    {
        Content = new FontIcon { Glyph = glyph, FontSize = size },
        Padding = new Thickness(8),
        Background = new SolidColorBrush(Colors.Transparent),
        BorderBrush = new SolidColorBrush(Colors.Transparent),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ------------------------------------------------------------------ adding and removing

    private bool IsRemoved(ReorderList list, string key)
    {
        if (list.IsCircuit) return int.TryParse(key, NumberStyles.Integer, Inv, out var id) && (_tileHiddenMask & (1 << id)) != 0;
        if (key == NowPlayingPriorityKey) return (_tileHiddenMask & (1 << 3)) != 0;
        return (_prioHiddenMask & (1 << PrioBit(key))) != 0;
    }

    private List<string> RemovedKeys(ReorderList list) => list.Order.Where(key => IsRemoved(list, key)).ToList();

    private Button AddTileButton(ReorderList list)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 9, 12, 9),
        };
        button.Click += (_, _) => _ = ShowTilePickerAsync(list);
        list.AddButton = button;
        UpdateAddButton(list);
        return button;
    }

    private void UpdateAddButtons()
    {
        if (_circuitList is not null) UpdateAddButton(_circuitList);
        if (_priorityList is not null) UpdateAddButton(_priorityList);
    }

    private void UpdateAddButton(ReorderList list)
    {
        if (list.AddButton is not { } button) return;
        var any = RemovedKeys(list).Count > 0;
        button.IsEnabled = any;
        SetActionButton(button, any ? L("Adăugați un tile", "Add a Tile") : L("Toate tile-urile sunt adăugate", "All tiles have been added"), "");
    }

    // Adding doesn't close the picker - you usually want more than one - but it
    // closes once there is nothing left in it.
    private async Task ShowTilePickerAsync(ReorderList list)
    {
        if (XamlRoot is null) return;
        var keys = RemovedKeys(list);
        if (keys.Count == 0) return;

        var cards = new StackPanel { Spacing = 4, Padding = new Thickness(0, 0, 14, 0) };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = DialogHeading.Create("\uE710", list.IsCircuit ? L("Adăugați în tile-uri circuit", "Add to Circuit Tiles") : L("Adăugați în tile-uri prioritare", "Add to Priority Tiles")),
            Content = new ScrollViewer { Content = cards, MaxHeight = Math.Max(200, XamlRoot.Size.Height - 260) },
            CloseButtonText = L("Gata", "Done"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        foreach (var key in keys)
        {
            var id = list.IsCircuit ? int.Parse(key, Inv) : -1;
            var name = list.IsCircuit ? TileName(id) : PriorityName(key);
            var description = list.IsCircuit ? TileDescription(id) : PriorityDescription(key);
            var glyph = list.IsCircuit ? TileGlyph(id) ?? "" : PriorityGlyph(key);
            Button? card = null;
            card = NavCard(glyph, name, description, () =>
            {
                AddTile(list, key);
                cards.Children.Remove(card);
                if (cards.Children.Count == 0) dialog.Hide();
            });
            // A plus rather than a chevron: this adds, it doesn't open anything.
            card.Content = HeaderGrid(glyph, name, description, new FontIcon { Glyph = "", FontSize = 14, Opacity = 0.72 });
            cards.Children.Add(card);
        }
        try
        {
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog was already open; only one can be at a time.
        }
    }

    private void AddTile(ReorderList list, string key)
    {
        if (list.IsCircuit)
        {
            _tileHiddenMask &= ~(1 << int.Parse(key, Inv));
            // Added tiles land at the bottom of the list, where they were asked for.
            PlaceKey(list, key, null, true);
            if (key == NowPlayingTileKey && _nowPlayingIsPriority && _priorityList is not null)
            {
                _nowPlayingIsPriority = false;
                list.Hidden.Remove(NowPlayingTileKey);
                _priorityList.Hidden.Add(NowPlayingPriorityKey);
                SyncRows(_priorityList);
                _priorityList.Changed?.Invoke();
            }
            SyncRows(list);
            SetCircuitEnabled(key, true);
            list.Changed?.Invoke();
        }
        else if (key == NowPlayingPriorityKey)
        {
            _tileHiddenMask &= ~(1 << 3);
            if (!_nowPlayingIsPriority)
            {
                _nowPlayingIsPriority = true;
                list.Hidden.Remove(NowPlayingPriorityKey);
                if (_circuitList is not null)
                {
                    _circuitList.Hidden.Add(NowPlayingTileKey);
                    SyncRows(_circuitList);
                }
            }
            PlaceKey(list, key, null, true);
            SyncRows(list);
            SetCircuitEnabled(NowPlayingTileKey, true);
            list.Changed?.Invoke();
        }
        else
        {
            _prioHiddenMask &= ~(1 << PrioBit(key));
            PlaceKey(list, key, null, true);
            SyncRows(list);
            list.Changed?.Invoke();
            // Notifications, ETS2 and web access come back switched on, as in the web interface.
            SyncToggle("p:" + key, true);
        }
        SaveHiddenMasks();
        UpdateAddButtons();
        RevealRow(list, key);
    }

    private async Task ConfirmRemoveTileAsync(ReorderList list, string key, string title)
    {
        if (list.IsCircuit && !CanRemoveCircuitTile(key))
        {
            ShowError(L("În circuit trebuie să rămână cel puțin un tile pornit.", "At least one switched-on tile has to stay in the circuit."));
            return;
        }
        // Taking a tile out switches it off as well, so it is worth one question.
        var confirmed = await ConfirmAsync(
            L($"Scoți „{title}” din listă?", $"Remove “{title}” from the list?"),
            L("Tile-ul se oprește și nu mai apare pe ceas. Îl poți pune înapoi oricând din „Adaugă un tile”.",
              "The tile switches off and no longer shows on the clock. You can put it back any time with “Add a tile”."),
            L("Scoate", "Remove"));
        if (confirmed) RemoveTile(list, key);
    }

    private bool CanRemoveCircuitTile(string key)
    {
        if (_circuitList is null || _circuitTiles is null) return false;
        var others = VisibleKeys(_circuitList).Where(other => other != key).ToList();
        // An empty circuit would leave the clock with nothing to show; it refuses one too.
        if (others.Count == 0) return false;
        return !_circuitTiles[key].Enabled || others.Any(other => _circuitTiles[other].Enabled);
    }

    private void RemoveTile(ReorderList list, string key)
    {
        if (list.IsCircuit)
        {
            _tileHiddenMask |= 1 << int.Parse(key, Inv);
            SyncRows(list);
            SetCircuitEnabled(key, false);
        }
        else if (key == NowPlayingPriorityKey)
        {
            _tileHiddenMask |= 1 << 3;
            // Out of both lists now, so it is back to being a circuit tile for when it returns.
            _nowPlayingIsPriority = false;
            list.Hidden.Add(NowPlayingPriorityKey);
            _circuitList?.Hidden.Remove(NowPlayingTileKey);
            SyncRows(list);
            if (_circuitList is not null) SyncRows(_circuitList);
            SetCircuitEnabled(NowPlayingTileKey, false);
            list.Changed?.Invoke();
        }
        else
        {
            _prioHiddenMask |= 1 << PrioBit(key);
            SyncRows(list);
            // The row carried the feature's only switch, so the feature goes with it.
            SyncToggle("p:" + key, false);
        }
        SaveHiddenMasks();
        UpdateAddButtons();
    }

    private void SaveHiddenMasks() => QueueSave("tilehidden", token => Post(token, "/tilehidden",
        ("circuit", Str(_tileHiddenMask)), ("prio", Str(_prioHiddenMask))), 0);

    private void RevealRow(ReorderList list, string key)
    {
        if (!list.Rows.TryGetValue(key, out var row)) return;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            row.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.5 }));
    }

    // ------------------------------------------------------------------ dragging

    // The gaps between rows and the space under the last one take a drop as
    // well, landing it at the end: a row can go below the last one, and Now
    // Playing can cross even into a list where every other row is elsewhere.
    private void EnableListDrop(ReorderList list)
    {
        list.Host.Background = new SolidColorBrush(Colors.Transparent);
        list.Host.Padding = new Thickness(0, 0, 0, 8);
        list.Host.MinHeight = 28;
        list.Host.AllowDrop = true;
        list.Host.DragOver += (_, args) =>
        {
            if (!CanDrop(list, null))
            {
                args.AcceptedOperation = DataPackageOperation.None;
                return;
            }
            args.AcceptedOperation = DataPackageOperation.Move;
            SetDragCaption(args);
            HideAllMarkers();
            var last = VisibleKeys(list).LastOrDefault();
            if (last is not null && list.Markers.TryGetValue(last, out var markers)) markers.After.Visibility = Visibility.Visible;
        };
        list.Host.DragLeave += (_, _) => HideAllMarkers();
        list.Host.Drop += (_, _) =>
        {
            HideAllMarkers();
            DropOn(list, null, true);
        };
    }

    private bool CanDrop(ReorderList target, string? targetKey)
    {
        if (_dragList is null || _dragKey is null) return false;
        if (ReferenceEquals(_dragList, target)) return targetKey != _dragKey;
        var other = ReferenceEquals(_dragList, _circuitList) ? _priorityList : _circuitList;
        return _dragKey == _dragList.NowPlayingKey && ReferenceEquals(target, other);
    }

    private void DropOn(ReorderList target, string? targetKey, bool after)
    {
        var source = _dragList;
        var key = _dragKey;
        if (source is null || key is null || !CanDrop(target, targetKey)) return;
        EndDrag();
        if (ReferenceEquals(source, target)) MoveKey(target, key, targetKey, after);
        else MoveNowPlaying(ReferenceEquals(target, _priorityList), targetKey, after);
    }

    private void EndDrag()
    {
        _dragList = null;
        _dragKey = null;
        _dragScrollTimer?.Stop();
        HideAllMarkers();
    }

    // The lists are taller than the room they have, and a scroller doesn't
    // scroll by itself while something is dragged over it - so a row could not
    // reach a place out of view, least of all the other list. Held near the
    // top or bottom edge, it now scrolls the lists that way.
    private void StartDragScroll()
    {
        var surface = Surface;
        var scroller = surface.Scroller;
        if (!surface.DragScrollHooked)
        {
            surface.DragScrollHooked = true;
            scroller.AllowDrop = true;
            // Handled events too: each row handles the drag over itself.
            scroller.AddHandler(UIElement.DragOverEvent,
                new DragEventHandler((_, args) => _dragPointerY = args.GetPosition(scroller).Y), true);
        }
        if (_dragScrollTimer is null)
        {
            _dragScrollTimer = DispatcherQueue.CreateTimer();
            _dragScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
            _dragScrollTimer.Tick += (_, _) => DragScrollTick();
        }
        _dragScroller = scroller;
        _dragPointerY = double.NaN;
        _dragScrollTimer.Start();
    }

    private void DragScrollTick()
    {
        if (_dragList is null || _dragScroller is not { } scroller)
        {
            _dragScrollTimer?.Stop();
            return;
        }
        if (double.IsNaN(_dragPointerY)) return;
        const double edge = 64;
        var bottom = scroller.ViewportHeight - edge;
        // A pointer that left the page over an edge sends no more positions;
        // the last one it sent keeps the page scrolling that way.
        var distance = _dragPointerY < edge ? _dragPointerY - edge
            : _dragPointerY > bottom ? _dragPointerY - bottom
            : 0;
        if (distance == 0) return;
        var step = Math.Sign(distance) * Math.Min(30, 4 + Math.Abs(distance) / 3);
        var offset = Math.Clamp(scroller.VerticalOffset + step, 0, scroller.ScrollableHeight);
        if (Math.Abs(offset - scroller.VerticalOffset) >= 0.5) scroller.ChangeView(null, offset, null, true);
    }

    // Now Playing is the one tile that lives in either list, and moving it
    // across makes it a priority tile or a circuit tile again.
    private void MoveNowPlaying(bool toPriority, string? targetKey, bool after)
    {
        if (_circuitList is null || _priorityList is null) return;
        var to = toPriority ? _priorityList : _circuitList;
        var from = toPriority ? _circuitList : _priorityList;
        from.Hidden.Add(from.NowPlayingKey);
        to.Hidden.Remove(to.NowPlayingKey);
        PlaceKey(to, to.NowPlayingKey, targetKey, after);
        SyncRows(from);
        SyncRows(to);
        _nowPlayingIsPriority = toPriority;
        // Its place in the circuit only matters once it is back there.
        if (!toPriority) _circuitList.Changed?.Invoke();
        _priorityList.Changed?.Invoke();
    }

    private static void SetDragCaption(DragEventArgs args)
    {
        if (args.DragUIOverride is not { } overlay) return;
        overlay.Caption = L("Mută aici", "Move here");
        overlay.IsCaptionVisible = true;
        overlay.IsGlyphVisible = false;
    }

    // The line that shows where a dragged row will land, drawn on the edge of the row it hovers.
    private static Rectangle DropMarker(VerticalAlignment edge)
    {
        var marker = Themed<Rectangle>(
            $"<Rectangle {XamlNs} Height='3' RadiusX='1.5' RadiusY='1.5' IsHitTestVisible='False' Visibility='Collapsed' " +
            "Fill='{ThemeResource AccentFillColorDefaultBrush}'/>");
        marker.VerticalAlignment = edge;
        marker.Margin = edge == VerticalAlignment.Top ? new Thickness(0, -7, 0, 0) : new Thickness(0, 0, 0, -7);
        Grid.SetColumnSpan(marker, 6);
        return marker;
    }

    private void HideAllMarkers()
    {
        if (_circuitList is not null) HideMarkers(_circuitList);
        if (_priorityList is not null) HideMarkers(_priorityList);
    }

    private static void HideMarkers(ReorderList list)
    {
        foreach (var (before, after) in list.Markers.Values)
        {
            before.Visibility = Visibility.Collapsed;
            after.Visibility = Visibility.Collapsed;
        }
    }

    private List<string> VisibleKeys(ReorderList list) =>
        list.Order.Where(key => !list.Hidden.Contains(key) && !IsRemoved(list, key)).ToList();

    private void MoveKey(ReorderList list, string key, string? target, bool after)
    {
        if (key == target || !list.Order.Contains(key)) return;
        PlaceKey(list, key, target, after);
        SyncRows(list);
        list.Changed?.Invoke();
    }

    // Puts key next to target, or at the end when there is no target.
    private static void PlaceKey(ReorderList list, string key, string? target, bool after)
    {
        list.Order.Remove(key);
        var index = target is null ? -1 : list.Order.IndexOf(target);
        if (index < 0) list.Order.Add(key);
        else list.Order.Insert(after ? index + 1 : index, key);
    }

    private void SyncRows(ReorderList list)
    {
        list.Host.Children.Clear();
        foreach (var key in VisibleKeys(list))
        {
            if (!list.Rows.TryGetValue(key, out var row)) continue;
            // A row dragged out of sight never gets its drop-completed back.
            row.Opacity = 1;
            list.Host.Children.Add(row);
        }
    }

    // A tile that is switched off keeps its place, with its icon and name faded.
    private static void SetRowDimmed(Border row, bool dimmed)
    {
        if (row.Child is not Grid grid) return;
        foreach (var child in grid.Children.OfType<FrameworkElement>())
            if (Equals(child.Tag, DimTag)) child.Opacity = dimmed ? 0.45 : 1;
    }
}
