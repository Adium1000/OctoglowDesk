using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

public sealed partial class ClockNativeSettingsPage
{
    private void BuildVoicemeeterContent(ClockDeviceState state)
    {
        AddSection(L("Vizualizare", "Visualization"),
            L("Aceste opțiuni sunt păstrate pe ceas și se aplică atât din aplicație, cât și din interfața web.",
              "These options are stored on the clock and apply from both the app and the web interface."));

        var mode = ChoiceBox(
            [
                (L("Spectru", "Spectrum"), "0"),
                (L("Indicator VU", "VU meter"), "1"),
                ("Stereo", "2"),
                (L("Formă de undă", "Waveform"), "3"),
                (L("Bare orizontale L / R", "Horizontal L / R bars"), "4"),
            ],
            Str(state.Int("vmMode")),
            220);
        mode.SelectionChanged += (_, _) => QueueSave("vm-mode",
            token => Post(token, "/settings", ("vmMode", ChoiceValue(mode))), 0);
        Add(SettingsCard("\uE9D9", L("Mod de afișare", "Display mode"),
            L("Alege felul în care sunetul ocupă matricea 32×8.", "Choose how audio fills the 32×8 matrix."), mode));

        var sensitivity = NewSlider(10, 100, Math.Clamp(state.Int("vmSensitivity", 60), 10, 100), 250);
        sensitivity.ValueChanged += (_, args) => QueueSave("vm-sensitivity",
            token => Post(token, "/settings", ("vmSensitivity", Str((int)Math.Round(args.NewValue)))), 250);
        Add(SettingsCard("\uE767", L("Sensibilitate", "Sensitivity"),
            L("Ridică nivelul pentru surse mai silențioase; valorile mari aprind mai mulți pixeli.",
              "Raises quieter sources; higher values light more pixels."),
            SliderWithValue(sensitivity, Percent)));
    }
}
