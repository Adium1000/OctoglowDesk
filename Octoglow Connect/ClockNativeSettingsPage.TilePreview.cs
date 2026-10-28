using System.Net;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace OctoglowSender;

// The Preview at the end of a tile's settings, as the web interface has it: the
// tile on the panel as the clock draws it, with the tile's own code (GET
// /tilepreview) - the panel itself while the tile is on it, otherwise drawn off
// the panel from what the tile has, or from a made-up value while it has
// nothing to show. Asked again every so often while the settings are open, and
// just after one of them is saved, so a change shows here as on the clock. Text
// wider than its window scrolls here as there, from the text the clock sends. A
// clock without /tilepreview leaves the part out.
public sealed partial class ClockNativeSettingsPage
{
    // Asks the open dialog's Preview again, once something in it was saved.
    private Action? _tilePreviewKick;

    private void AddTilePreview(string key)
    {
        var first = BuildTarget.Children.Count;
        AddSection(L("Previzualizare", "Preview"));
        var heading = BuildTarget.Children[first];
        var (panel, draw) = LedPanel();
        var note = Secondary("");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.Margin = new Thickness(0, 6, 0, 0);
        // Keep the caption's line reserved so its first response does not resize the popup.
        note.MinHeight = 18;
        note.Opacity = 0;
        Add(panel);
        Add(note);

        var token = _dialogToken;
        var fetchTimer = DispatcherQueue.CreateTimer();
        fetchTimer.IsRepeating = false;
        var playTimer = DispatcherQueue.CreateTimer();
        playTimer.IsRepeating = false;
        Action? step = null;
        playTimer.Tick += (_, _) => step?.Invoke();
        string? shown = null;
        TilePreviewAnswer? currentAnswer = null;
        var busy = false;
        var refreshRequested = false;
        var supported = true;

        void Wait(DispatcherQueueTimer timer, int ms)
        {
            timer.Stop();
            timer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, ms));
            timer.Start();
        }

        void Play(TilePreviewAnswer answer)
        {
            playTimer.Stop();
            step = null;
            draw(answer.Still);
            note.Text = answer.Live ? L("Ce arată ceasul acum", "What the clock shows now")
                : answer.Sample ? L("Cu o valoare de exemplu", "With a sample value")
                : "";
            note.Opacity = note.Text.Length > 0 ? 0.72 : 0;

            if (answer.Frames is { Count: > 1 } frames)
            {
                var index = 0;
                step = () =>
                {
                    draw(frames[index]);
                    index = (index + 1) % frames.Count;
                    Wait(playTimer, answer.FrameMs);
                };
                step();
                return;
            }
            if (answer.Text is not { } text || text.Length <= answer.TextWidth) return;

            // The tile's DrawAtPos(): the window from TextX shows the text from pos.
            int count = text.Length, width = answer.TextWidth, end = count - width, pos = 0, direction = 1;
            var scrollFrame = (byte[])answer.Still.Clone();
            byte[] At(int at)
            {
                for (var k = 0; k < width && answer.TextX + k < 32; k++)
                    scrollFrame[answer.TextX + k] = at + k >= 0 && at + k < count ? text[at + k] : (byte)0;
                return scrollFrame;
            }
            step = () =>
            {
                var wait = answer.StepMs;
                if (answer.Wraps)
                {
                    if (++pos >= count) pos = -width;
                    if (pos == 0) wait = answer.PauseMs;
                }
                else
                {
                    pos += direction;
                    if (pos >= end || pos <= 0)
                    {
                        pos = Math.Clamp(pos, 0, end);
                        direction = -direction;
                        wait = answer.PauseMs;
                    }
                }
                draw(At(pos));
                Wait(playTimer, wait);
            };
            Wait(playTimer, answer.PauseMs);
        }

        async void Fetch()
        {
            if (!supported || token.IsCancellationRequested || !panel.IsLoaded) return;
            if (busy) { refreshRequested = true; return; }
            busy = true;
            refreshRequested = false;
            var next = 1500;
            try
            {
                var json = await ClockDeviceApi.GetTextAsync(_device, "/tilepreview?t=" + Uri.EscapeDataString(key), token);
                if (token.IsCancellationRequested || !panel.IsLoaded) return;
                heading.Visibility = panel.Visibility = Visibility.Visible;
                if (json != shown)
                {
                    currentAnswer = ParseTilePreview(json);
                    shown = json;
                    Play(currentAnswer);
                }
                // Unchanged responses need no JSON parsing or playback restart.
                next = currentAnswer?.Live == true ? 350 : currentAnswer?.Frames is not null ? 8000 : 1500;
            }
            catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                // A clock whose firmware has no Preview: nothing to show, nothing more to ask.
                heading.Visibility = panel.Visibility = note.Visibility = Visibility.Collapsed;
                supported = false;
                playTimer.Stop();
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                next = 2500;
            }
            finally
            {
                busy = false;
            }
            if (!token.IsCancellationRequested && panel.IsLoaded) Wait(fetchTimer, refreshRequested ? 150 : next);
        }

        fetchTimer.Tick += (_, _) => Fetch();
        _tilePreviewKick = () =>
        {
            if (!supported || token.IsCancellationRequested) return;
            refreshRequested = true;
            Wait(fetchTimer, 150);
        };
        _dialogCleanups.Add(() =>
        {
            fetchTimer.Stop();
            playTimer.Stop();
            _tilePreviewKick = null;
        });
        panel.Loaded += (_, _) =>
        {
            if (token.IsCancellationRequested || !supported) return;
            if (currentAnswer is not null) Play(currentAnswer);
            Fetch();
        };
        panel.Unloaded += (_, _) => { fetchTimer.Stop(); playTimer.Stop(); };
    }

    // A 32x8 panel drawn the way the clock page draws the real one, and what
    // puts a frame on it: 32 columns, bit y of each lit in row y.
    private static (Border Panel, Action<byte[]> Draw) LedPanel()
    {
        const double pitch = 12, lit = 9, unlit = 5;
        var canvas = new Canvas { Width = 32 * pitch, Height = 8 * pitch };
        // The accent the clock page lights its LEDs in.
        var on = new SolidColorBrush(Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var accent) &&
                                     accent is Windows.UI.Color color ? color : Colors.DeepSkyBlue);
        var off = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x73, 0x76, 0x7C));
        var leds = new Ellipse[8, 32];
        var scales = new ScaleTransform[8, 32];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var scale = new ScaleTransform { ScaleX = unlit / lit, ScaleY = unlit / lit };
                var led = new Ellipse { Width = lit, Height = lit, Fill = off, Opacity = 0.42,
                    RenderTransform = scale, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5) };
                Canvas.SetLeft(led, x * pitch + (pitch - lit) / 2);
                Canvas.SetTop(led, y * pitch + (pitch - lit) / 2);
                canvas.Children.Add(led);
                leds[y, x] = led;
                scales[y, x] = scale;
            }
        }
        var panel = Themed<Border>(
            $"<Border {XamlNs} Background='{{ThemeResource LayerFillColorDefaultBrush}}' " +
            "BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1' CornerRadius='10'/>");
        panel.Padding = new Thickness(14, 12, 14, 12);
        panel.Margin = new Thickness(0, 8, 0, 0);
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
        AutomationProperties.SetName(panel, L("Previzualizare", "Preview"));

        var shown = new byte[32];
        void Draw(byte[] columns)
        {
            for (var x = 0; x < 32; x++)
            {
                var value = x < columns.Length ? columns[x] : (byte)0;
                if (value == shown[x]) continue;
                for (var y = 0; y < 8; y++)
                {
                    var isLit = (value >> y & 1) != 0;
                    if (isLit == ((shown[x] >> y & 1) != 0)) continue;
                    var led = leds[y, x];
                    var scale = scales[y, x];
                    scale.ScaleX = scale.ScaleY = isLit ? 1 : unlit / lit;
                    led.Fill = isLit ? on : off;
                    led.Opacity = isLit ? 1 : 0.42;
                }
                shown[x] = value;
            }
        }
        return (panel, Draw);
    }
}
