using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

public sealed partial class ClockNativeSettingsPage
{
    private readonly Dictionary<string, Button> _tileRunButtons = new();

    private Button CreateTileRunButton(string key)
    {
        var button = TransparentIconButton("\uE768", 16);
        _tileRunButtons[key] = button;
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            var token = _linesCancellation?.Token ?? CancellationToken.None;
            try
            {
                var path = key == "stopwatch" ? "/swstate" : key == "timer" ? "/timerstate" : "/alarmstate";
                var state = await ClockDeviceApi.GetJsonAsync(_device, path, token);
                var command = key switch
                {
                    "stopwatch" => state.Bool("running") ? "/swstop" : "/swstart",
                    "timer" => state.Bool("running") || state.Bool("finished") ? "/timerpause" : "/timerstart",
                    _ => "/alarmstop",
                };
                await Post(token, command);
                var updated = await ClockDeviceApi.GetJsonAsync(_device, path, token);
                if (token.IsCancellationRequested) return;
                if (key == "stopwatch") _linesStopwatch = updated;
                else if (key == "timer") _linesTimer = updated;
                else _linesAlarm = updated;
                RefreshTileLines();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { ShowError(FriendlyError(error)); }
            finally { button.IsEnabled = true; }
        };
        RefreshTileRunButtons();
        return button;
    }

    private void RefreshTileRunButtons()
    {
        foreach (var (key, button) in _tileRunButtons)
        {
            var state = key == "stopwatch" ? _linesStopwatch : key == "timer" ? _linesTimer : _linesAlarm;
            var running = state?.Bool("running") == true;
            var stop = key == "alarm" || key == "timer" && state?.Bool("finished") == true;
            var glyph = stop ? "\uE71A" : running ? (key == "timer" ? "\uE769" : "\uE71A") : "\uE768";
            var label = stop || running && key == "stopwatch" ? L("Oprește", "Stop")
                : running ? L("Pauză", "Pause") : L("Pornește", "Start");
            button.Visibility = key == "alarm" && state?.Bool("ringing") != true ? Visibility.Collapsed : Visibility.Visible;
            if (button.Content is FontIcon icon) icon.Glyph = glyph;
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label + " " + PriorityName(key));
        }
    }
}
