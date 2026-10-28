using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace OctoglowSender;

// "Aa" in the chosen font beside every font picker, as the web interface has
// it, and behind it a preview: the time and a line of text on the panel in
// any of the fonts, starting from the picker's. Looking changes nothing on the
// clock; "Folosiți" hands the picker the font being looked at.
public sealed partial class ClockNativeSettingsPage
{
    private const string FontGlyph = "";
    private const string FontSample = "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz 0123456789";

    private static string FontDescription(int mode) => mode switch
    {
        1 => L("Îngust: 3 coloane și doar majuscule, ca să încapă mai mult text.", "Narrow: 3 columns and capitals only, so more text fits."),
        2 => L("4 coloane, cu forme rotunjite și litere mici adevărate.", "4 columns, rounded, with real lowercase."),
        3 => L("Ca un afișaj cu șapte segmente, cu goluri între segmente.", "Like a seven-segment display, with gaps between the segments."),
        4 => L("Tot șapte segmente, cu colțurile aprinse, ca liniile să se unească.", "Seven segments too, with the corners lit so the strokes join up."),
        5 => L("Gros: verticale de 2 coloane, doar majuscule.", "Heavy: two-column uprights, capitals only."),
        6 => L("Mako înclinat spre dreapta.", "Mako leaning to the right."),
        7 => L("Majusculele Mako sar în sus și în jos, pe toate cele 8 rânduri.", "Mako's capitals hopping up and down, over all 8 rows."),
        10 => L("Litere Mako cu trase verticale duble și aspect mai dens.", "Mako letters with doubled vertical strokes and a denser look."),
        _ => L("Fontul clasic al ceasului: litere de 5 coloane, cu litere mici.", "The clock's classic font: 5-column letters, with lowercase."),
    };

    private static int FontOf(ComboBox picker) => int.TryParse(ChoiceValue(picker), System.Globalization.NumberStyles.Integer, Inv, out var mode) ? mode : 0;

    // The picker with its "Aa" in front, for a card's control column. `title`
    // names what the font is for, over the preview.
    private FrameworkElement FontPicker(ComboBox picker, string title)
    {
        var dots = Themed<Microsoft.UI.Xaml.Shapes.Path>($"<Path {XamlNs} Fill='{{ThemeResource AccentTextFillColorPrimaryBrush}}'/>");
        var frame = new Grid { Height = 24, Children = { dots } };
        var swatch = new Button
        {
            Padding = new Thickness(0),
            Content = frame,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = L("Previzualizare", "Preview");
        ToolTipService.SetToolTip(swatch, name);
        AutomationProperties.SetName(swatch, name);

        void Show()
        {
            var columns = ClockFont.Text(FontOf(picker), "Aa");
            frame.Width = columns.Length * 3;
            dots.Data = LedGeometry(columns, 3, 1.2);
        }
        picker.SelectionChanged += (_, _) => Show();
        Show();
        swatch.Click += (_, _) => _ = ShowFontPreviewAsync(title, picker);
        return PreviewPicker(picker, swatch);
    }

    // Lit LEDs only, `pitch` apart, as one shape.
    private static GeometryGroup LedGeometry(byte[] columns, double pitch, double radius)
    {
        var group = new GeometryGroup();
        for (var x = 0; x < columns.Length; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                if ((columns[x] >> y & 1) == 0) continue;
                group.Children.Add(new EllipseGeometry
                {
                    Center = new Point(x * pitch + pitch / 2, y * pitch + pitch / 2),
                    RadiusX = radius,
                    RadiusY = radius,
                });
            }
        }
        return group;
    }

    private async Task ShowFontPreviewAsync(string title, ComboBox picker)
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null) return;
        var saved = FontOf(picker);
        var mode = saved;
        var width = Math.Clamp(root.Size.Width - 120, 280, 460);

        var fonts = ChoiceBox(FontChoices(), Str(saved), 0);
        fonts.Header = "Font";
        fonts.HorizontalAlignment = HorizontalAlignment.Stretch;
        var description = Secondary(FontDescription(saved));
        var (clockPanel, drawClock) = LedPanel();
        var (textPanel, drawText) = LedPanel();
        clockPanel.Margin = new Thickness(0, 6, 0, 0);
        textPanel.Margin = new Thickness(0);
        AutomationProperties.SetName(clockPanel, L("Ceasul în acest font", "The clock in this font"));
        AutomationProperties.SetName(textPanel, L("Text în acest font", "Text in this font"));
        var sample = new TextBox
        {
            Header = L("Text de probă", "Sample text"),
            PlaceholderText = L("Scrieți un text", "Type some text"),
            MaxLength = 80,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var content = new StackPanel
        {
            Width = width,
            Spacing = 8,
            Children = { fonts, description, clockPanel, textPanel, sample },
        };

        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(GlyphIcon(FontGlyph, 24));
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = header,
            Content = content,
            CloseButtonText = L("Închideți", "Close"),
            // Accented like a default button, without Enter in the text box using it.
            PrimaryButtonStyle = Resource<Style>("AccentButtonStyle"),
            DefaultButton = ContentDialogButton.None,
            RequestedTheme = ThemeSource.ActualTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = width + 64;

        // Text that fits stands still in the middle; longer text runs across
        // and comes round again, as on the clock.
        var text = Array.Empty<byte>();
        var position = 32;
        void Scroll()
        {
            var frame = new byte[32];
            var left = (32 - text.Length) / 2;
            for (var x = 0; x < 32; x++)
            {
                var j = text.Length <= 32 ? x - left : position + x - 32;
                if (j >= 0 && j < text.Length) frame[x] = text[j];
            }
            drawText(frame);
        }
        void Redraw()
        {
            drawClock(ClockFont.Clock(mode, 12, 34, true));
            var typed = ClockFont.Sanitize(sample.Text).Trim();
            text = ClockFont.Text(mode, typed.Length > 0 ? typed : FontSample);
            position = 32;
            Scroll();
        }
        void Pick()
        {
            mode = FontOf(fonts);
            description.Text = FontDescription(mode);
            // Only a different font has anything to use.
            dialog.PrimaryButtonText = mode == saved ? "" : L("Folosiți", "Use");
            Redraw();
        }
        fonts.SelectionChanged += (_, _) => Pick();
        sample.TextChanged += (_, _) => Redraw();
        Pick();

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(40);
        timer.Tick += (_, _) =>
        {
            if (text.Length <= 32) return;
            position = position >= text.Length + 32 ? 0 : position + 1;
            Scroll();
        };
        timer.Start();
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && mode != saved)
                SelectChoice(picker, Str(mode));
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
