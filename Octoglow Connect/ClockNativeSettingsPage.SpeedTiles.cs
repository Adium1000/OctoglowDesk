using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace OctoglowSender;

// The speed tiles' own settings - Euro Truck Simulator 2's, BeamNG.drive's,
// Assetto Corsa's and the three NFS games', each apart - as the web interface has them behind the tile's
// gear: the unit, the plain number or one of the gauges, and where a gauge's
// scale ends. All of it is kept on the clock, which keeps the speed in km/h and
// only converts to draw it, so a change shows at once - in the Preview under
// them too. What the app itself does with the games is on their Integrations
// pages.
public sealed partial class ClockNativeSettingsPage
{
    private void BuildSpeedTileContent(ClockDeviceState state, string tile)
    {
        var mphKey = tile + "Mph";
        var gaugeKey = tile + "Gauge";
        var fullKey = tile + "GaugeFull";
        var mph = state.Bool(mphKey);

        var unit = ChoiceBox([("KM/H", "0"), ("MPH", "1")], mph ? "1" : "0", 140);
        Add(SettingsCard("", L("Unitate de viteză", "Speed unit"),
            L("Cum afișează ceasul viteza.", "How the clock shows the speed."), unit));

        // A clock whose firmware predates the gauges has only the unit.
        if (!state.Has(gaugeKey))
        {
            unit.SelectionChanged += (_, _) => QueueSave("speedunit:" + mphKey,
                token => Post(token, "/speedunit", (mphKey, ChoiceValue(unit))), 0);
            return;
        }

        var style = ChoiceBox(
            [
                (L("Implicit", "Default"), "0"),
                ("Arc", "1"),
                (L("Cadran", "Dial"), "2"),
                (L("Bară", "Bar"), "3"),
            ],
            Str(state.Int(gaugeKey)), 180);
        Add(SettingsCard("", L("Stil afișaj", "Display style"),
            L("Numărul simplu sau un gauge. Cifrele gauge-urilor au fonturile lor; fontul tile-ului se folosește la stilul Implicit.",
              "The plain number or a gauge. The gauges draw their own digits; the tile's font is used by the Default style."), style));

        // Where the clock starts each tile's scale (Octoglow.ino, *_GAUGE_FULL_DEFAULT).
        var defaultFull = tile switch { "beamng" => 250, "assetto" or "nfsu2" or "nfsmw" or "nfsc" => 300, _ => 130 };
        var full = new NumberBox
        {
            Minimum = SpeedGauge.FullMinimum,
            Maximum = SpeedGauge.FullMaximum,
            SmallChange = 10,
            LargeChange = 50,
            Value = state.Int(fullKey, defaultFull),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 120,
        };
        AutomationProperties.SetName(full, L("Capătul scalei în km/h", "End of the scale in km/h"));
        var fullCard = SettingsCard("", L("Capătul scalei", "End of the scale"),
            L("Viteza la care gauge-ul se umple, în km/h, în oricare unitate.", "The speed at which the gauge is full, in km/h, whichever the unit."),
            Inline(full, new TextBlock { Text = "km/h", Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center }));
        Add(fullCard);

        // The end of the scale only means something to a gauge.
        void ShowGaugeParts() => SetExpanded(fullCard, ChoiceValue(style) != "0");

        unit.SelectionChanged += (_, _) =>
            QueueSave("speedunit:" + mphKey, token => Post(token, "/speedunit", (mphKey, ChoiceValue(unit))), 0);
        style.SelectionChanged += (_, _) =>
        {
            QueueSave("speedunit:" + gaugeKey, token => Post(token, "/speedunit", (gaugeKey, ChoiceValue(style))), 0);
            ShowGaugeParts();
        };
        full.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue))
            {
                full.Value = state.Int(fullKey, defaultFull);
                return;
            }
            var value = Math.Clamp((int)Math.Round(args.NewValue), SpeedGauge.FullMinimum, SpeedGauge.FullMaximum);
            QueueSave("speedunit:" + fullKey, token => Post(token, "/speedunit", (fullKey, Str(value))), 450);
        };
        ShowGaugeParts();

        if (tile == "beamng")
        {
            var note = Secondary(L("Adresa și portul OutGauge se setează în Integrări.",
                                   "The OutGauge address and port are set in Integrations."));
            note.Margin = new Thickness(4, 8, 4, 0);
            Add(note);
        }
    }
}
