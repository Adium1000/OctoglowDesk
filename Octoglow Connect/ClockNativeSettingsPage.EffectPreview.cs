using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// A play button beside every transition and scroll type picker, as the web
// interface has it, and behind it the effect on the panel: two tiles changing
// places with the transition, or a line of text moving the way the scroll type
// moves it, at the pace the row is set to. Looking changes nothing on the
// clock; "Folosiți" hands the picker the effect being looked at.
public sealed partial class ClockNativeSettingsPage
{
    private const string EffectGlyph = "";
    // A play mark as LED columns, lit like the "Aa" beside the font pickers.
    private static readonly byte[] PlayMark = [0x7F, 0x3E, 0x1C, 0x08];

    // What the row behind a picker is set to. Pace: percent of a transition's
    // normal pace. StepMs, Font and Icon: a scrolling tile's own.
    private sealed record EffectLook(int Pace = 100, int StepMs = 40, int Font = 0, int Icon = 4);

    // The picker with its play button in front, for a card's control column.
    // `title` names what the effect is for, over the preview.
    private FrameworkElement EffectPicker(ComboBox picker, string title, bool transition, EffectLook look)
    {
        var dots = Themed<Microsoft.UI.Xaml.Shapes.Path>($"<Path {XamlNs} Fill='{{ThemeResource AccentTextFillColorPrimaryBrush}}'/>");
        dots.Data = LedGeometry(PlayMark, 3, 1.2);
        var swatch = new Button
        {
            Padding = new Thickness(0),
            Content = new Grid { Width = PlayMark.Length * 3, Height = 24, Children = { dots } },
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = L("Previzualizare", "Preview");
        ToolTipService.SetToolTip(swatch, name);
        AutomationProperties.SetName(swatch, name);
        swatch.Click += (_, _) => _ = ShowEffectPreviewAsync(title, picker, transition, look);
        return PreviewPicker(picker, swatch);
    }

    private static byte[] IconPattern(int selection) =>
        selection >= 1 && selection <= IconPatterns.Length ? IconPatterns[selection - 1] : IconPatterns[3];

    private async Task ShowEffectPreviewAsync(string title, ComboBox picker, bool transition, EffectLook look)
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null) return;
        var saved = FontOf(picker);
        var effect = saved;
        var width = Math.Clamp(root.Size.Width - 120, 280, 460);

        var effects = ChoiceBox(transition ? TransitionChoices() : ScrollChoices(), Str(saved), 0);
        effects.Header = transition ? L("Tranziție", "Transition") : L("Tip scroll", "Scroll Type");
        effects.HorizontalAlignment = HorizontalAlignment.Stretch;
        var (panel, draw) = LedPanel();
        panel.Margin = new Thickness(0, 6, 0, 0);
        var content = new StackPanel { Width = width, Spacing = 8, Children = { effects, panel } };

        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(GlyphIcon(EffectGlyph, 24));
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = header,
            Content = content,
            CloseButtonText = L("Închideți", "Close"),
            PrimaryButtonStyle = Resource<Style>("AccentButtonStyle"),
            DefaultButton = ContentDialogButton.None,
            RequestedTheme = ThemeSource.ActualTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = width + 64;

        // One round of the effect, played over and over by the clock on the wall
        // rather than one frame a tick: the fastest frames are 4 ms apart.
        var frames = new List<ClockEffects.Frame>();
        var ends = new List<double>();
        var total = 1.0;
        var shown = -1;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        void Load()
        {
            frames.Clear();
            if (transition)
            {
                // Two tiles taking turns: each rests a moment, then the transition
                // takes it to the other.
                var first = ClockFont.Clock(look.Font, 12, 34, true);
                var second = ClockEffects.Tile(ClockEffects.IconColumns(IconPattern(1)), ClockFont.Text(look.Font, "25.12"));
                frames.Add(new ClockEffects.Frame(first, 1100));
                frames.AddRange(ClockEffects.Transition(effect, first, second, look.Pace));
                frames.Add(new ClockEffects.Frame(second, 1100));
                frames.AddRange(ClockEffects.Transition(effect, second, first, look.Pace));
            }
            else
            {
                var sample = ClockFont.Sanitize(L("Octoglow Clock - text de exemplu", "Octoglow Clock - sample text"));
                frames.AddRange(ClockEffects.Scroll(effect, ClockEffects.IconColumns(IconPattern(look.Icon)),
                    ClockFont.Text(look.Font, sample), look.StepMs));
            }
            ends.Clear();
            total = 0;
            foreach (var frame in frames)
            {
                total += frame.Milliseconds;
                ends.Add(total);
            }
            shown = -1;
            watch.Restart();
        }

        void Show()
        {
            if (frames.Count == 0) return;
            var at = watch.Elapsed.TotalMilliseconds % total;
            var index = shown >= 0 && shown < frames.Count && ends[shown] > at && (shown == 0 || ends[shown - 1] <= at) ? shown : 0;
            while (index < frames.Count - 1 && ends[index] <= at) index++;
            if (index == shown) return;
            shown = index;
            draw(frames[index].Columns);
            if (panel.Child is FrameworkElement leds) leds.Opacity = frames[index].Level;
        }

        void Pick()
        {
            effect = FontOf(effects);
            // Only a different effect has anything to use.
            dialog.PrimaryButtonText = effect == saved ? "" : L("Folosiți", "Use");
            Load();
            Show();
        }
        effects.SelectionChanged += (_, _) => Pick();
        Pick();

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) => Show();
        timer.Start();
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && effect != saved)
                SelectChoice(picker, Str(effect));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog was already open; only one can be at a time.
        }
        finally
        {
            timer.Stop();
        }
    }
}
