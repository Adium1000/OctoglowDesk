using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// MSI Afterburner: what this computer reads from MSI Afterburner and RivaTuner
// Statistics Server right now, and which GPU's temperature goes to the clock
// when there are several. Which value the tile shows, and in which unit, are
// the tile's own settings in the Tile Manager. The reading itself is the
// backend's (SenderBackend.Afterburner); this page only shows what it last saw.
public sealed partial class ClockNativeSettingsPage
{
    private Action? BuildAfterburnerPage()
    {
        var state = ValueText(AbShortText(AfterburnerStatus.Current));
        var stateCard = SettingsCard(TileGlyph(14)!, L("Stare", "Status"), AbStateText(AfterburnerStatus.Current), state);
        var stateDescription = CardDescription(stateCard);
        Add(stateCard);

        AddSection(L("Valori citite", "Values read"));
        var fps = ValueText("");
        var fpsCard = SettingsCard("", "FPS", "", fps);
        var cpu = ValueText("");
        var cpuCard = SettingsCard("", L("Temperatura CPU", "CPU temperature"), "", cpu);
        var gpu = ValueText("");
        var gpuCard = SettingsCard("", L("Temperatura GPU", "GPU temperature"), "", gpu);
        // Whether the In-game FPS tile takes this frame rate: only a game's.
        var game = ValueText("");
        var gameCard = SettingsCard("", L("Joc în prim-plan", "Game in front"), "", game);
        var gameDescription = CardDescription(gameCard);
        var fpsDescription = CardDescription(fpsCard);
        var cpuDescription = CardDescription(cpuCard);
        var gpuDescription = CardDescription(gpuCard);
        Add(fpsCard);
        Add(gameCard);
        Add(cpuCard);
        Add(gpuCard);

        // Only when there is a GPU to choose between: the GPUs MSI Afterburner
        // has a temperature for, as it lists them when the page opens.
        var config = ConfigStore.Load();
        var withTemperature = AfterburnerStatus.Current.Reading.Gpus.Where(g => g.HasTemperature).ToList();
        if (withTemperature.Count > 1 || config.AfterburnerGpu.Length > 0)
        {
            var choices = new List<(string, string)> { (L("Automat", "Automatic"), "") };
            choices.AddRange(withTemperature.Select(g => ($"GPU{g.Index + 1} · {g.Name}", g.Id)));
            if (config.AfterburnerGpu.Length > 0 && withTemperature.All(g => g.Id != config.AfterburnerGpu))
                choices.Add((L("Placa aleasă (nu apare acum)", "The chosen card (not listed now)"), config.AfterburnerGpu));
            var box = ChoiceBox(choices.ToArray(), config.AfterburnerGpu, 300);
            box.SelectionChanged += (_, _) => SaveAppConfig(saved => saved.AfterburnerGpu = ChoiceValue(box));
            AddSection(L("Placa video", "Graphics card"));
            Add(SettingsCard("", L("Temperatura GPU de la", "GPU temperature from"),
                L("Automat: prima placă video din lista MSI Afterburner (GPU1, GPU2...) care are temperatura bifată.",
                  "Automatic: the first graphics card in MSI Afterburner's list (GPU1, GPU2...) whose temperature is ticked."), box));
        }

        void Refresh()
        {
            var now = AfterburnerStatus.Current;
            state.Text = AbShortText(now);
            if (stateDescription is not null) stateDescription.Text = AbStateText(now);
            var reading = now.Reading;
            var reading0 = now.State == AbState.Reading;
            fps.Text = reading0 && reading.Fps is { } f ? Str(f) : "-";
            cpu.Text = reading0 && reading.CpuCelsius is { } c ? Celsius(c) : "-";
            gpu.Text = reading0 && reading.GpuCelsius is { } g ? Celsius(g) : "-";
            if (fpsDescription is not null)
                fpsDescription.Text = !reading0 ? AbStateText(now)
                    : reading.Fps is not null ? L("Din RivaTuner Statistics Server, pentru ", "From RivaTuner Statistics Server, for ") + reading.FpsApplication
                    : AbMissingText(reading.FpsMissing, "fps");
            if (cpuDescription is not null)
                cpuDescription.Text = !reading0 ? AbStateText(now)
                    : reading.CpuCelsius is not null ? L("Din MSI Afterburner, sursa „", "From MSI Afterburner, the \"") + reading.CpuSource + L("”", "\" source")
                    : AbMissingText(reading.CpuMissing, "cpu");
            if (gpuDescription is not null)
                gpuDescription.Text = !reading0 ? AbStateText(now)
                    : reading.GpuCelsius is not null && reading.Gpu is { } card ? L("Din MSI Afterburner, ", "From MSI Afterburner, ") + $"GPU{card.Index + 1} · {card.Name}"
                    : AbMissingText(reading.GpuMissing, "gpu");
            game.Text = reading0 && reading.FpsIsGame ? L("Da", "Yes") : L("Nu", "No");
            if (gameDescription is not null)
                gameDescription.Text = !reading0 ? AbStateText(now) : AbGameText(reading);
            fpsCard.Opacity = reading.Fps is null ? 0.6 : 1;
            gameCard.Opacity = reading0 && reading.FpsIsGame ? 1 : 0.6;
            cpuCard.Opacity = reading.CpuCelsius is null ? 0.6 : 1;
            gpuCard.Opacity = reading.GpuCelsius is null ? 0.6 : 1;
        }
        Refresh();

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        return timer.Stop;
    }

    private static string Celsius(double value) =>
        value.ToString("0.0", AppStrings.IsEnglish ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("ro-RO")) + " °C";

    // A word or two, for the Integrations card and beside the status.
    private static string AbShortText(AbSnapshot snapshot) => snapshot.State switch
    {
        AbState.Reading => snapshot.Reading.Fps is not null || snapshot.Reading.CpuCelsius is not null || snapshot.Reading.GpuCelsius is not null
            ? L("Activ", "Active")
            : L("Indisponibil", "Unavailable"),
        AbState.ClockUnsupported => L("Firmware vechi", "Old firmware"),
        _ => L("Inactiv", "Inactive"),
    };

    // What the state means and what to do about it.
    private static string AbStateText(AbSnapshot snapshot)
    {
        if (snapshot.State == AbState.ClockUnsupported)
            return L("Firmware-ul ceasului nu are încă tile-ul MSI Afterburner. Actualizează firmware-ul ceasului.",
                "The clock's firmware has no MSI Afterburner tile yet. Update the clock's firmware.");
        if (snapshot.State == AbState.Off)
            return L("Oprit. Pornește tile-ul MSI Afterburner sau FPS în joc din Manager de tile-uri; până atunci nu se citește nimic.",
                "Off. Turn the MSI Afterburner or In-game FPS tile on in the Tile Manager; until then nothing is read.");
        var reading = snapshot.Reading;
        var afterburner = reading.AfterburnerLive
            ? L("MSI Afterburner rulează", "MSI Afterburner is running")
            : AbMissingText(reading.CpuMissing is AbMissing.AfterburnerNoAccess or AbMissing.AfterburnerStale ? reading.CpuMissing : AbMissing.AfterburnerNotRunning, "cpu").TrimEnd('.');
        var rtss = reading.RtssLive
            ? L("RivaTuner Statistics Server rulează", "RivaTuner Statistics Server is running")
            : AbMissingText(reading.FpsMissing is AbMissing.RtssNoAccess ? AbMissing.RtssNoAccess : AbMissing.RtssNotRunning, "fps").TrimEnd('.');
        return afterburner + "; " + rtss + ". " +
               L("Cât timp valoarea aleasă pentru tile lipsește, tile-ul nu apare pe ceas.",
                 "While the value chosen for the tile is missing, the tile isn't shown on the clock.");
    }

    // Whether the In-game FPS tile shows this frame rate, and why.
    private static string AbGameText(AbReading reading)
    {
        var app = reading.FpsApplication ?? L("Aplicația", "The application");
        return reading.Fps is null ? L("Fără FPS, tile-ul FPS în joc nu apare.", "With no FPS, the In-game FPS tile isn't shown.")
            : reading.FpsGame switch
            {
                AbGame.Listed => app + L(" este în lista de jocuri a Windows: tile-ul FPS în joc îl afișează.",
                    " is in Windows' list of games: the In-game FPS tile shows it."),
                AbGame.FullScreen => app + L(" rulează pe tot ecranul: tile-ul FPS în joc îl afișează.",
                    " runs full screen: the In-game FPS tile shows it."),
                AbGame.NotInFront => app + L(" nu este fereastra din față: tile-ul FPS în joc nu apare.",
                    " isn't the window in front: the In-game FPS tile isn't shown."),
                _ => app + L(" nu este în lista de jocuri a Windows și nu rulează pe tot ecranul, deci nu e luat drept joc: tile-ul FPS în joc nu apare.",
                    " isn't in Windows' list of games and doesn't run full screen, so it isn't taken for a game: the In-game FPS tile isn't shown."),
            };
    }

    // Why a value is missing, and where to fix it.
    private static string AbMissingText(AbMissing missing, string what) => missing switch
    {
        AbMissing.AfterburnerNotRunning => L("MSI Afterburner nu rulează.", "MSI Afterburner isn't running."),
        AbMissing.AfterburnerNoAccess => L("Octoglow Connect nu are acces la datele MSI Afterburner. Pornește-le pe amândouă cu aceleași drepturi.",
            "Octoglow Connect can't read MSI Afterburner's data. Start both with the same rights."),
        AbMissing.AfterburnerStale => L("MSI Afterburner nu mai actualizează datele.", "MSI Afterburner stopped updating its data."),
        AbMissing.SourceOff => what == "gpu"
            ? L("„GPU temperature” nu este bifat în MSI Afterburner › Settings › Monitoring.",
                "\"GPU temperature\" isn't ticked in MSI Afterburner › Settings › Monitoring.")
            : L("„CPU temperature” nu este bifat în MSI Afterburner › Settings › Monitoring.",
                "\"CPU temperature\" isn't ticked in MSI Afterburner › Settings › Monitoring."),
        AbMissing.NoValue => L("MSI Afterburner nu are acum o valoare pentru ea.", "MSI Afterburner has no value for it right now."),
        AbMissing.GpuNotFound => L("Placa video aleasă nu apare acum în MSI Afterburner.", "The chosen graphics card isn't listed in MSI Afterburner now."),
        AbMissing.RtssNotRunning => L("RivaTuner Statistics Server nu rulează.", "RivaTuner Statistics Server isn't running."),
        AbMissing.RtssNoAccess => L("Octoglow Connect nu are acces la datele RivaTuner Statistics Server. Pornește-le pe amândouă cu aceleași drepturi.",
            "Octoglow Connect can't read RivaTuner Statistics Server's data. Start both with the same rights."),
        AbMissing.No3DApplication => L("Nicio aplicație 3D în prim-plan: FPS-ul apare cât timp rulează un joc.",
            "No 3D application in the foreground: the FPS appears while a game runs."),
        _ => "",
    };
}
