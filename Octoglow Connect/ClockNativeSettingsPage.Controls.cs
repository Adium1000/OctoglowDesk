using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace OctoglowSender;

// Windows 11 Settings-style building blocks for the clock pages - a section
// header, a card with an icon, a title, a description and a control on the
// right, an expander built the same way with rows inside it - and the queue
// that applies a change to the clock a moment after it is made.
public sealed partial class ClockNativeSettingsPage
{
    private const string XamlNs = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
    private const string DimTag = "dim";
    // Marks a section heading, so a tile's settings can tell whether they begin with one.
    private const string SectionTag = "section";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Dictionary<string, CancellationTokenSource> _pendingSaves = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private int _savesRunning;
    private int _saveStatusVersion;
    private int _settingsRevision;
    private bool _suppressSaves;

    private enum SaveState
    {
        Saving,
        Saved,
        Failed,
    }

    // Changes apply the way Windows Settings applies them: shortly after the
    // control settles, with no Save button. A second change to the same setting
    // inside that moment replaces the first, so a dragged slider sends one
    // request rather than one per step.
    private void QueueSave(string key, Func<CancellationToken, Task> operation, int delayMs = 350)
    {
        if (_suppressSaves) return;
        ++_settingsRevision;
        _linesStateUpdatedAt = DateTime.MinValue;
        PauseTileLineReads();
        CancelSave(key);
        var cancellation = new CancellationTokenSource();
        _pendingSaves[key] = cancellation;
        _ = RunQueuedSaveAsync(key, cancellation, operation, delayMs);
    }

    private void CancelSave(string key)
    {
        if (_pendingSaves.Remove(key, out var previous)) previous.Cancel();
    }

    private async Task RunQueuedSaveAsync(
        string key,
        CancellationTokenSource cancellation,
        Func<CancellationToken, Task> operation,
        int delayMs)
    {
        var enteredGate = false;
        var running = false;
        try
        {
            await Task.Delay(delayMs, cancellation.Token);
            // Keep the setting pending until the previous request has finished.
            // Otherwise a later edit can reach the clock first and be overwritten.
            await _saveGate.WaitAsync(cancellation.Token);
            enteredGate = true;
            if (_pendingSaves.TryGetValue(key, out var current) && current == cancellation)
                _pendingSaves.Remove(key);

            _savesRunning++;
            running = true;
            SetSaveState(SaveState.Saving);
            await operation(CancellationToken.None);
            _savesRunning--;
            running = false;
            if (_savesRunning == 0 && _pendingSaves.Count == 0) SetSaveState(SaveState.Saved);
            _tilePreviewKick?.Invoke();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (running) _savesRunning--;
            SetSaveState(SaveState.Failed);
            ShowError(FriendlyError(error));
        }
        finally
        {
            if (_pendingSaves.TryGetValue(key, out var current) && current == cancellation)
                _pendingSaves.Remove(key);
            if (enteredGate) _saveGate.Release();
            cancellation.Dispose();
        }
    }

    // Sets controls from code without their change handlers sending anything.
    private void WithoutSaving(Action action)
    {
        var previous = _suppressSaves;
        _suppressSaves = true;
        try
        {
            action();
        }
        finally
        {
            _suppressSaves = previous;
        }
    }

    // Saving shows in the page header, and in the headers of a panel and a
    // tile's settings dialog as well while they are open over the page.
    private sealed record SaveIndicator(StackPanel Panel, ProgressRing Progress, FrameworkElement Saved, FontIcon Failed, TextBlock Text);

    private SaveIndicator? _dialogSaveIndicator;
    private readonly Dictionary<StackPanel, Storyboard> _saveFades = new();

    private void SetSaveState(SaveState state)
    {
        var version = ++_saveStatusVersion;
        var text = state switch
        {
            SaveState.Saving => L("Se salvează…", "Saving…"),
            SaveState.Saved => L("Salvat!", "Saved!"),
            _ => L("Nesalvat", "Not saved"),
        };
        foreach (var indicator in SaveIndicators())
        {
            if (_saveFades.Remove(indicator.Panel, out var fading)) fading.Stop();
            indicator.Panel.Visibility = Visibility.Visible;
            indicator.Panel.Opacity = 1;
            indicator.Progress.IsActive = state == SaveState.Saving;
            indicator.Progress.Visibility = state == SaveState.Saving ? Visibility.Visible : Visibility.Collapsed;
            indicator.Saved.Visibility = state == SaveState.Saved ? Visibility.Visible : Visibility.Collapsed;
            indicator.Failed.Visibility = state == SaveState.Failed ? Visibility.Visible : Visibility.Collapsed;
            indicator.Text.Text = text;
            indicator.Text.Visibility = Visibility.Visible;
        }
        if (state == SaveState.Saved) _ = HideSaveStateAsync(version);
    }

    private async Task HideSaveStateAsync(int version)
    {
        await Task.Delay(2500);
        if (version != _saveStatusVersion) return;
        foreach (var indicator in SaveIndicators())
        {
            var panel = indicator.Panel;
            var fade = new DoubleAnimation
            {
                From = 1, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(450)),
            };
            Storyboard.SetTarget(fade, panel);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var board = new Storyboard();
            board.Children.Add(fade);
            _saveFades[panel] = board;
            board.Completed += (_, _) =>
            {
                if (version != _saveStatusVersion || !_saveFades.Remove(panel, out var current) || current != board) return;
                board.Stop();
                panel.Visibility = Visibility.Collapsed;
                panel.Opacity = 1;
            };
            board.Begin();
        }
    }

    private IEnumerable<SaveIndicator> SaveIndicators()
    {
        yield return new SaveIndicator(SaveStatusPanel, SaveProgress, SaveOkBadge, SaveFailIcon, SaveText);
        if (_panel?.SaveIndicator is { } panel) yield return panel;
        if (_dialogSaveIndicator is not null) yield return _dialogSaveIndicator;
    }

    private static SaveIndicator NewSaveIndicator()
    {
        var panel = Themed<StackPanel>(
            $"<StackPanel {XamlNs} Orientation='Horizontal' Spacing='8' VerticalAlignment='Center' Visibility='Collapsed'>" +
            "<ProgressRing Width='16' Height='16' IsActive='False'/>" +
            "<FontIcon Glyph='&#xE74E;' FontSize='14' Visibility='Collapsed' " +
            "Foreground='{ThemeResource AccentFillColorDefaultBrush}'/>" +
            "<FontIcon Glyph='&#xE7BA;' FontSize='14' Visibility='Collapsed' Foreground='{ThemeResource SystemFillColorCriticalBrush}'/>" +
            "<TextBlock Opacity='0.8' VerticalAlignment='Center'/>" +
            "</StackPanel>");
        var text = (TextBlock)panel.Children[3];
        text.Style = Resource<Style>("CaptionTextBlockStyle");
        return new SaveIndicator(panel, (ProgressRing)panel.Children[0], (FontIcon)panel.Children[1], (FontIcon)panel.Children[2], text);
    }

    private Task Post(CancellationToken token, string path, params (string Key, string Value)[] values) =>
        ClockDeviceApi.PostAsync(_device, path, Form(values), token);

    // ------------------------------------------------------------------ layout

    // Where AddSection and Add put what they build: the page or a panel, or a
    // tile's settings dialog while it is being filled.
    private Panel? _buildTarget;

    private Panel BuildTarget => _buildTarget ?? Surface.Content;

    private void AddSection(string title, string? hint = null)
    {
        BuildTarget.Children.Add(new TextBlock
        {
            Text = title,
            Style = Resource<Style>("BodyStrongTextBlockStyle"),
            Margin = new Thickness(2, BuildTarget.Children.Count == 0 ? 4 : 28, 0, hint is null ? 6 : 2),
            Tag = SectionTag,
        });
        if (hint is not null)
        {
            var text = Secondary(hint);
            text.Margin = new Thickness(2, 0, 0, 8);
            BuildTarget.Children.Add(text);
        }
    }

    private void Add(UIElement element) => BuildTarget.Children.Add(element);

    private static T Themed<T>(string xaml) where T : class => (T)XamlReader.Load(xaml);

    // Card brushes go through {ThemeResource}, so a card follows the app's own
    // light or dark theme even when it differs from the one Windows is using.
    private static Border CardBorder() => Themed<Border>(
        $"<Border {XamlNs} Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' " +
        "BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1' CornerRadius='6'/>");

    private static Rectangle Divider() => Themed<Rectangle>(
        $"<Rectangle {XamlNs} Height='1' HorizontalAlignment='Stretch' " +
        "Fill='{ThemeResource DividerStrokeColorDefaultBrush}'/>");

    private static FontIcon GlyphIcon(string glyph, double size = 20) => new()
    {
        Glyph = glyph,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Secondary(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("CaptionTextBlockStyle"),
        Opacity = 0.72,
        TextWrapping = TextWrapping.Wrap,
    };

    private static StackPanel TitleBlock(string title, string? description)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        if (!string.IsNullOrEmpty(title))
            text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description))
            text.Children.Add(Secondary(description));
        return text;
    }

    private static Grid HeaderGrid(string glyph, string title, string? description, FrameworkElement? control)
    {
        var grid = new Grid { ColumnSpacing = 16, MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(GlyphIcon(glyph));
        var text = TitleBlock(title, description);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
            grid.Children.Add(control);
            AdaptTrailingControl(grid, text, control, 1, 2);
        }
        return grid;
    }

    private static Border SettingsCard(string glyph, string title, string? description = null, FrameworkElement? control = null)
    {
        var card = CardBorder();
        card.Padding = new Thickness(16, 12, 16, 12);
        card.MinHeight = 68;
        card.Child = HeaderGrid(glyph, title, description, control);
        return card;
    }

    internal static Expander SettingsExpander(
        string glyph,
        string title,
        string? description,
        FrameworkElement? headerControl,
        IEnumerable<UIElement> rows,
        bool expanded = false)
    {
        var body = new StackPanel();
        foreach (var row in rows)
        {
            if (body.Children.Count > 0) body.Children.Add(Divider());
            body.Children.Add(row);
        }
        var header = HeaderGrid(glyph, title, description, headerControl);
        header.Padding = new Thickness(0, 10, 0, 10);
        return new Expander
        {
            Header = header,
            Content = body,
            IsExpanded = expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
        };
    }

    // A switch whose settings only mean anything while it is on: they show
    // under it as part of the same card once it is turned on, and go away when
    // it is turned off, rather than waiting behind a chevron that leads
    // nowhere.
    // `showWhenOn` false turns it the other way round: the rows belong to the
    // switch being off, as setting the time by hand belongs to network time not
    // being used.
    private static StackPanel ToggleGroup(
        string glyph,
        string title,
        string? description,
        ToggleSwitch toggle,
        IEnumerable<UIElement> rows,
        bool showWhenOn = true)
    {
        var header = CardBorder();
        header.Padding = new Thickness(16, 12, 16, 12);
        header.MinHeight = 68;
        header.Child = HeaderGrid(glyph, title, description, toggle);

        var body = new StackPanel();
        foreach (var row in rows)
        {
            if (body.Children.Count > 0) body.Children.Add(Divider());
            body.Children.Add(row);
        }
        var bodyCard = CardBorder();
        bodyCard.Child = body;
        // The two are one card while both show, so they share the edge between them.
        bodyCard.BorderThickness = new Thickness(1, 0, 1, 1);
        bodyCard.CornerRadius = new CornerRadius(0, 0, 6, 6);
        // Its height is animated, so what does not fit yet has to be cut off
        // rather than drawn over the card below.
        var clip = new RectangleGeometry();
        bodyCard.Clip = clip;
        bodyCard.SizeChanged += (_, args) => clip.Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height);

        void Update(bool animate)
        {
            var open = toggle.IsOn == showWhenOn;
            header.CornerRadius = open ? new CornerRadius(6, 6, 0, 0) : new CornerRadius(6);
            SetExpanded(bodyCard, open, animate);
        }
        toggle.Toggled += (_, _) => Update(animate: true);
        Update(animate: false);
        return new StackPanel { Children = { header, bodyCard } };
    }

    // Settings that appear and go away follow the switch that governs them
    // rather than jumping into place.
    private static readonly TimeSpan GroupAnimation = TimeSpan.FromMilliseconds(180);

    // A row inside an expander, lined up with the title of the header above it.
    internal static Grid SubRow(string title, string? description, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 16, MinHeight = 52, Padding = new Thickness(52, 10, 16, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = TitleBlock(title, description);
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        AdaptTrailingControl(grid, text, control, 0, 1);
        return grid;
    }

    // A wide editor goes below its label in a narrow dialog, like the web
    // layout. Small toggles stay inline; controls keep their native hit targets.
    private static void AdaptTrailingControl(Grid grid, StackPanel text, FrameworkElement control, int textColumn, int controlColumn)
    {
        if (text.Children.Count == 0) return;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var originalAlignment = control.HorizontalAlignment;
        double preferredControlWidth = 0;
        bool? stacked = null;
        void Adapt()
        {
            if (grid.ActualWidth <= 0) return;
            if (preferredControlWidth <= 0)
                preferredControlWidth = double.IsNaN(control.Width) ? Math.Max(control.DesiredSize.Width, control.MinWidth) : control.Width;
            if (preferredControlWidth <= 0) return;
            var available = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;
            var narrow = available < preferredControlWidth + 160 + (textColumn == 1 ? 52 : 16);
            if (stacked == narrow) return;
            stacked = narrow;
            grid.RowDefinitions[0].Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            Grid.SetColumnSpan(text, narrow ? grid.ColumnDefinitions.Count - textColumn : 1);
            Grid.SetRow(control, narrow ? 1 : 0);
            Grid.SetColumn(control, narrow ? textColumn : controlColumn);
            Grid.SetColumnSpan(control, narrow ? grid.ColumnDefinitions.Count - textColumn : 1);
            control.HorizontalAlignment = narrow ? HorizontalAlignment.Right : originalAlignment;
            grid.RowSpacing = narrow ? 8 : 0;
        }
        grid.Loaded += (_, _) => Adapt();
        grid.SizeChanged += (_, _) => Adapt();
    }

    // A row inside an expander whose content sits under its text instead of beside it.
    private static StackPanel SubBlock(string? title, string? description, params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(52, 12, 16, 14) };
        if (title is not null || description is not null) panel.Children.Add(TitleBlock(title ?? "", description));
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static StackPanel Inline(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    // Makes the preview action part of the picker instead of a separate square
    // beside it. The rest of the ComboBox remains the normal, native hit target.
    private static Grid PreviewPicker(ComboBox picker, Button preview)
    {
        picker.HorizontalAlignment = HorizontalAlignment.Stretch;
        picker.Padding = new Thickness(48, picker.Padding.Top, picker.Padding.Right, picker.Padding.Bottom);
        preview.Width = 40;
        preview.Height = 28;
        preview.Margin = new Thickness(3, 0, 0, 0);
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        preview.VerticalAlignment = VerticalAlignment.Center;
        preview.Padding = new Thickness(0);
        preview.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        preview.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        preview.BorderThickness = new Thickness(0);
        preview.CornerRadius = new CornerRadius(4);

        var host = new Grid { MinWidth = picker.MinWidth, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(picker);
        host.Children.Add(preview);
        return host;
    }

    // ------------------------------------------------------------------ controls

    // Just the switch, flush with the right edge like every other control: no
    // On/Off label, and not the 12px the template keeps after the knob for one.
    private static ToggleSwitch Toggle(bool on) => new()
    {
        IsOn = on,
        OnContent = "",
        OffContent = "",
        MinWidth = 0,
        Margin = new Thickness(0, 0, -12, 0),
    };

    private static ComboBox ChoiceBox(IEnumerable<(string Label, string Value)> choices, string selected, double minWidth = 200)
    {
        var box = new ComboBox { MinWidth = minWidth };
        foreach (var choice in choices)
        {
            var item = new ComboBoxItem { Content = choice.Label, Tag = choice.Value };
            box.Items.Add(item);
            if (string.Equals(choice.Value, selected, StringComparison.OrdinalIgnoreCase)) box.SelectedItem = item;
        }
        if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    private static string ChoiceValue(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0";

    private static void SelectChoice(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase)) continue;
            box.SelectedItem = item;
            return;
        }
    }

    private static Slider NewSlider(double minimum, double maximum, double value, double width = 220) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        StepFrequency = 1,
        SmallChange = 1,
        Value = value,
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel SliderWithValue(Slider slider, Func<double, string> format)
    {
        var value = new TextBlock
        {
            Text = format(slider.Value),
            MinWidth = 40,
            Opacity = 0.8,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, args) => value.Text = format(args.NewValue);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { slider, value } };
    }

    private static Button ActionButton(string text, string? glyph = null, bool accent = false)
    {
        var button = new Button { MinWidth = 96, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is null)
        {
            button.Content = text;
        }
        else
        {
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
            };
        }
        if (accent) button.Style = Resource<Style>("AccentButtonStyle");
        // A glyph beside the label leaves the button's own name to chance.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        return button;
    }

    private static TextBlock ValueText(string text) => new()
    {
        Text = text,
        IsTextSelectionEnabled = true,
        Opacity = 0.8,
        MaxWidth = 380,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // A Save or Apply button that only shows once a value differs from what the
    // clock has - not merely because the settings were opened. The action it hands
    // back checks again; call it when a value changes and after saving.
    private static Action ShowWhenChanged(Func<bool> changed, params UIElement[] elements)
    {
        void Update()
        {
            var visibility = changed() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var element in elements) element.Visibility = visibility;
        }
        Update();
        return Update;
    }

    private static void OnEnter(UIElement box, Action action) => box.KeyDown += (_, args) =>
    {
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        action();
    };

    private static string Str(int value) => value.ToString(Inv);

    private static string Percent(double value) => $"{Math.Round(value)}%";
}
