using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

public sealed partial class ClockNativeSettingsPage
{
    private void BuildVolumeContent(ClockDeviceState state)
    {
        Add(Secondary(L(
            "Necesită Octoglow Connect pornit. Rămâne afișat 3 secunde după ultima schimbare, fără să reia tranziția la fiecare pas.",
            "Requires Octoglow Connect running. Stays visible for 3 seconds after the last change, without replaying the transition at each step.")));
        var adaptive = Toggle(state.Bool("volumeAdaptiveIcon", true));
        Add(SettingsCard("\uE767", L("Iconiță după ieșirea audio", "Follow the audio output icon"),
            L("Căști, boxe sau Bluetooth; dezactivează pentru o iconiță fixă.",
              "Headphones, speakers or Bluetooth; turn off to use a fixed icon."), adaptive));
        var icon = IconChoiceBox(state.Int("iconSelVolume"), 38);
        icon.IsEnabled = !adaptive.IsOn;
        adaptive.Toggled += (_, _) =>
        {
            icon.IsEnabled = !adaptive.IsOn;
            QueueSetting("volumeAdaptiveIcon", adaptive.IsOn ? "1" : "0");
        };
        icon.SelectionChanged += (_, _) => QueueSetting("iconSelVolume", ChoiceValue(icon));
        Add(SettingsCard("\uE91B", L("Iconiță fixă", "Fixed icon"), null, icon));
        var font = ChoiceBox(FontChoices(), Str(state.Int("fontTypeVolume")));
        font.SelectionChanged += (_, _) => QueueSetting("fontTypeVolume", ChoiceValue(font));
        Add(SettingsCard(FontGlyph, L("Font", "Font"), null, FontPicker(font, PriorityName("volume"))));
        var transition = ChoiceBox(TransitionChoices(), Str(state.Int("tileTransVolume")));
        transition.SelectionChanged += (_, _) => QueueSetting("tileTransVolume", ChoiceValue(transition));
        Add(SettingsCard("\uE945", L("Tranziție", "Transition"),
            L("Se aplică doar la apariția tile-ului.", "Plays only when the tile appears."),
            EffectPicker(transition, PriorityName("volume"), true,
                new EffectLook(Pace: state.Int("tileTransSpdVolume", 100), Font: state.Int("fontTypeVolume")))));
    }
}
