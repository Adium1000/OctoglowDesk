using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// The Integrations tab: what Octoglow Connect sends the clock from this
// computer. One card per integration, named and nothing more - with whether it
// is active where there is something live to tell - and each opens a page of
// its own, whose header says what the integration does: a panel, or a page in
// the tab when the app is set to open configurations in place. These settings
// live in the app, not on the clock, so they apply at once.
public sealed partial class ClockNativeSettingsPage
{
    private void AddCompatibleVersionNotice(string message) => Add(new InfoBar
    {
        Title = L("Versiune compatibilă necesară", "Compatible version required"),
        Message = message,
        Severity = InfoBarSeverity.Informational,
        IsClosable = true,
        IsOpen = true,
        Margin = new Thickness(0, 4, 0, 8),
    });

    private Action? BuildIntegrations(ClockDeviceState state)
    {
        var showUnavailable = state.Bool("showGrayed", true);
        var nowPlayingAvailable = TileInManager(state, NowPlayingTileKey) &&
                                  state.Tiles().Any(tile => tile.Id == 3 && tile.Enabled);
        var obsAvailable = state.Bool("obsEnabled") && TileInManager(state, "obs");
        var browserDownloadsAvailable = state.Bool("browserDownloadsEnabled") && TileInManager(state, "browserdownloads");
        var qbitAvailable = state.Bool("qbitEnabled") && TileInManager(state, "qbittorrent");
        var bambuAvailable = state.Bool("bambuEnabled") && TileInManager(state, "bambu");
        // For the circuit tile - switched on by its own row in the Tile Manager -
        // or the In-game FPS priority tile, which shows the same frame rate.
        var afterburnerAvailable = (TileInManager(state, "14") && state.Tiles().Any(tile => tile.Id == 14 && tile.Enabled)) ||
                                   (state.Bool("gameFpsEnabled") && TileInManager(state, "gamefps"));
        var ets2Available = state.Bool("ets2Enabled") && TileInManager(state, "ets2");
        var beamNgAvailable = state.Bool("beamngEnabled") && TileInManager(state, "beamng");
        var assettoAvailable = state.Bool("assettoEnabled") && TileInManager(state, "assetto");
        var nfsu2Available = state.Bool("nfsu2Enabled") && TileInManager(state, "nfsu2");
        var nfsMwAvailable = state.Bool("nfsmwEnabled") && TileInManager(state, "nfsmw");
        var nfsCarbonAvailable = state.Bool("nfscEnabled") && TileInManager(state, "nfsc");
        var voicemeeterAvailable = state.Bool("voicemeeterEnabled") && TileInManager(state, "voicemeeter");
        TextBlock? obsStatus = null;
        TextBlock? browserStatus = null;
        TextBlock? qbitStatus = null;
        TextBlock? bambuStatus = null;
        TextBlock? afterburnerStatus = null;
        TextBlock? beamNgStatus = null;
        TextBlock? assettoStatus = null;
        TextBlock? nfsu2Status = null;
        TextBlock? nfsMwStatus = null;
        TextBlock? nfsCarbonStatus = null;
        // Apps and games each under a heading of their own; a heading with
        // nothing listed under it is left out.
        var apps = new List<FrameworkElement>();
        var games = new List<FrameworkElement>();

        // An integration whose tile is off is left out, or shown faded when the
        // clock is set to show what cannot be used.
        bool Listed(bool available) => available || showUnavailable;

        if (Listed(nowPlayingAvailable))
            apps.Add(IntegrationCard(ClockSettingsSection.MediaPlayer, nowPlayingAvailable, null));

        // A clock whose firmware predates the OBS tile gets no card for it.
        if (state.Has("obsEnabled") && Listed(obsAvailable))
        {
            if (obsAvailable) obsStatus = ValueText(ObsActiveText());
            apps.Add(IntegrationCard(ClockSettingsSection.ObsStudio, obsAvailable, obsStatus));
        }

        // No page of its own: whether the browser extension is connected is all
        // there is to see, besides the guide to loading it.
        if (Listed(browserDownloadsAvailable))
        {
            if (browserDownloadsAvailable) browserStatus = ValueText(ActiveText(BrowserDownloadsBridge.Snapshot().BrowserConnected));
            apps.Add(IntegrationAvailability(guide => SettingsCard(PriorityGlyph("browserdownloads"), "Browser Downloads",
                    browserDownloadsAvailable ? null : IntegrationOffReason(), Trailing(guide, browserStatus)),
                browserDownloadsAvailable, ClockSettingsSection.BrowserDownloadsGuide));
        }

        // Its connection state, not only active or not: a wrong password or a
        // WebUI that is off is what someone setting it up needs to see.
        if (state.Has("qbitEnabled") && Listed(qbitAvailable))
        {
            if (qbitAvailable) qbitStatus = ValueText(QbitShortText(QBittorrentStatus.Current));
            apps.Add(IntegrationCard(ClockSettingsSection.QBittorrent, qbitAvailable, qbitStatus));
        }

        // No page of its own either: nothing about it is set in the app. What to
        // do in Bambu Studio when no percentage comes is on the state's tooltip.
        if (state.Has("bambuEnabled") && Listed(bambuAvailable))
        {
            if (bambuAvailable)
            {
                bambuStatus = ValueText(BambuShortText(BambuStatus.Current));
                ToolTipService.SetToolTip(bambuStatus, BambuStateText(BambuStatus.Current));
            }
            apps.Add(IntegrationAvailability(_ => SettingsCard(PriorityGlyph("bambu"), "Bambu Studio",
                    bambuAvailable ? null : IntegrationOffReason(), bambuStatus),
                bambuAvailable, null));
        }

        // What is read from MSI Afterburner and RTSS right now. A clock whose
        // firmware predates the tile gets no card for it.
        if (state.Has("abMode") && Listed(afterburnerAvailable))
        {
            if (afterburnerAvailable) afterburnerStatus = ValueText(AbShortText(AfterburnerStatus.Current));
            apps.Add(IntegrationCard(ClockSettingsSection.MsiAfterburner, afterburnerAvailable, afterburnerStatus));
        }

        if (Listed(voicemeeterAvailable))
            apps.Add(IntegrationCard(ClockSettingsSection.Voicemeeter, voicemeeterAvailable, null));

        if (Listed(ets2Available))
            games.Add(IntegrationCard(ClockSettingsSection.Ets2, ets2Available, null));

        // Active while OutGauge packets arrive. A clock whose firmware predates
        // the tile gets no card for it.
        if (state.Has("beamngEnabled") && Listed(beamNgAvailable))
        {
            if (beamNgAvailable) beamNgStatus = ValueText(ActiveText(BeamNgStatus.Receiving));
            games.Add(IntegrationCard(ClockSettingsSection.BeamNg, beamNgAvailable, beamNgStatus));
        }

        // Active while the car's speed comes in: a session being driven.
        if (state.Has("assettoEnabled") && Listed(assettoAvailable))
        {
            if (assettoAvailable) assettoStatus = ValueText(ActiveText(AssettoCorsaStatus.Receiving));
            games.Add(IntegrationCard(ClockSettingsSection.AssettoCorsa, assettoAvailable, assettoStatus));
        }

        // Whether the game is found, supported, in a race: the speed counts as
        // coming in only once it can be read and passes its checks.
        if (state.Has("nfsu2Enabled") && Listed(nfsu2Available))
        {
            if (nfsu2Available) nfsu2Status = ValueText(Nfsu2ShortText(Nfsu2Status.Current));
            games.Add(IntegrationCard(ClockSettingsSection.Nfsu2, nfsu2Available, nfsu2Status));
        }
        if (state.Has("nfsmwEnabled") && Listed(nfsMwAvailable))
        {
            if (nfsMwAvailable) nfsMwStatus = ValueText(NfsMwShortText(NfsMwStatus.Current));
            games.Add(IntegrationCard(ClockSettingsSection.NfsMw, nfsMwAvailable, nfsMwStatus));
        }
        if (state.Has("nfscEnabled") && Listed(nfsCarbonAvailable))
        {
            if (nfsCarbonAvailable) nfsCarbonStatus = ValueText(NfsCarbonShortText(NfsCarbonStatus.Current));
            games.Add(IntegrationCard(ClockSettingsSection.NfsCarbon, nfsCarbonAvailable, nfsCarbonStatus));
        }

        foreach (var (heading, cards) in new[] { (L("Aplicații", "Apps"), apps), (L("Jocuri", "Games"), games) })
        {
            if (cards.Count == 0) continue;
            AddSection(heading);
            foreach (var card in cards) Add(card);
        }

        if (apps.Count + games.Count == 0)
        {
            AddSection(L("Nicio integrare activă", "No active integrations"),
                L("Pornește un tile compatibil din Manager de tile-uri pentru a-i vedea setările aici.",
                  "Enable a compatible tile in the Tile Manager to see its settings here."));
        }

        if (obsStatus is null && browserStatus is null && qbitStatus is null && bambuStatus is null && afterburnerStatus is null && beamNgStatus is null && assettoStatus is null && nfsu2Status is null && nfsMwStatus is null && nfsCarbonStatus is null) return null;
        // Active or inactive follows what happens while the tab is open.
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            if (obsStatus is not null) obsStatus.Text = ObsActiveText();
            if (browserStatus is not null) browserStatus.Text = ActiveText(BrowserDownloadsBridge.Snapshot().BrowserConnected);
            if (qbitStatus is not null) qbitStatus.Text = QbitShortText(QBittorrentStatus.Current);
            if (bambuStatus is not null)
            {
                bambuStatus.Text = BambuShortText(BambuStatus.Current);
                ToolTipService.SetToolTip(bambuStatus, BambuStateText(BambuStatus.Current));
            }
            if (afterburnerStatus is not null) afterburnerStatus.Text = AbShortText(AfterburnerStatus.Current);
            if (beamNgStatus is not null) beamNgStatus.Text = ActiveText(BeamNgStatus.Receiving);
            if (assettoStatus is not null) assettoStatus.Text = ActiveText(AssettoCorsaStatus.Receiving);
            if (nfsu2Status is not null) nfsu2Status.Text = Nfsu2ShortText(Nfsu2Status.Current);
            if (nfsMwStatus is not null) nfsMwStatus.Text = NfsMwShortText(NfsMwStatus.Current);
            if (nfsCarbonStatus is not null) nfsCarbonStatus.Text = NfsCarbonShortText(NfsCarbonStatus.Current);
        };
        timer.Start();
        return timer.Stop;
    }

    // One integration on the tab: its name, whether it is active where that is
    // known, the ⓘ to its setup guide where it needs one, and the chevron to its
    // page, which opens in a panel or in the tab as the configurations do. A
    // faded one says why it can't be used.
    private FrameworkElement IntegrationCard(ClockSettingsSection section, bool available, FrameworkElement? status) =>
        IntegrationAvailability(guide => NavCard(SectionGlyph(section), SectionHeader(section).Title,
                available ? null : IntegrationOffReason(), Trailing(guide, status), () => OpenConfigurationSection(section)),
            available, GuideSection(section));

    private static string IntegrationOffReason() =>
        L("Pornește-l din Manager de tile-uri.", "Turn it on in the Tile Manager.");

    private static string ActiveText(bool active) => active ? L("Activ", "Active") : L("Inactiv", "Inactive");

    // The guide button goes first, before whatever the card says on its right.
    private static FrameworkElement? Trailing(FrameworkElement? guide, FrameworkElement? status) =>
        guide is null ? status : status is null ? guide : Inline(guide, status);

    // `build` makes the card around the guide button it is given. A faded card
    // takes no clicks, but its guide still does - it is read before the tile is
    // turned on as much as after: the button inside the card only keeps its
    // place, and the one that answers lies over it, laid out the same way.
    private FrameworkElement IntegrationAvailability(Func<FrameworkElement?, FrameworkElement> build, bool available,
        ClockSettingsSection? guide)
    {
        var inner = guide is { } inside ? GuideButton(inside) : null;
        var card = build(inner);
        if (available) return card;
        card.Opacity = 0.45;
        card.IsHitTestVisible = false;
        if (inner is null || guide is not { } section) return card;
        // Unseen, and out of the way of the keyboard and screen readers.
        inner.Opacity = 0;
        inner.IsTabStop = false;
        AutomationProperties.SetAccessibilityView(inner, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        // The card's own header grid over its padding and border, holding only
        // the guide button - and an invisible chevron where a card that opens a
        // page has one.
        FrameworkElement button = GuideButton(section);
        var trailing = card is Button ? Inline(button, new FontIcon { Glyph = "\uE76C", FontSize = 14, Opacity = 0 }) : button;
        var over = HeaderGrid("", "", null, trailing);
        over.Margin = new Thickness(17, 13, 17, 13);
        over.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { card, over } };
    }

    // ------------------------------------------------------------------ pages

    private void BuildMediaPlayerPage()
    {
        var config = ConfigStore.Load();
        var keep = Toggle(config.KeepNowPlayingWhenPaused);
        keep.Toggled += (_, _) => SaveAppConfig(saved => saved.KeepNowPlayingWhenPaused = keep.IsOn);
        Add(SettingsCard("\uE769", L("Păstrează activitatea afișată cât timp melodia este pe pauză",
            "Keep the activity shown while the song is paused"), null, keep));
        Add(PlayerListExpander("\uE8D6", L("Playere audio", "Audio players"), L("Nume player audio", "Audio player name"),
            config.MusicPlayers, (saved, players) => saved.MusicPlayers = players));
        Add(PlayerListExpander("\uE714", L("Playere video", "Video players"), L("Nume player video", "Video player name"),
            config.VideoPlayers, (saved, players) => saved.VideoPlayers = players));
    }

    private Action? BuildEts2Page()
    {
        Add(SettingsCard("\uE946", L("Configurare necesară", "Setup required"),
            L("Pentru datele din joc, instalează și activează pluginul de telemetrie SCS.",
              "Install and enable the SCS telemetry plugin to receive game data.")));
        var config = ConfigStore.Load();
        var truck = Toggle(config.StopPriorityTileWhenTruckStops);
        truck.Toggled += (_, _) => SaveAppConfig(saved => saved.StopPriorityTileWhenTruckStops = truck.IsOn);
        Add(SettingsCard("\uE7FC", L("Oprește tile-ul prioritar când camionul stă pe loc",
            "Stop the priority tile when the truck stops moving"), null, truck));
        return null;
    }

    // Where OutGauge is heard, and whether a standing vehicle keeps the tile up.
    // How the speed is drawn is the tile's own, in the Tile Manager.
    private Action? BuildBeamNgPage()
    {
        Add(SettingsCard("\uE946", L("Configurare necesară", "Setup required"),
            L("Activează OutGauge UDP în opțiunile BeamNG.drive și folosește adresa și portul de mai jos.",
              "Enable OutGauge UDP in BeamNG.drive and use the address and port below.")));
        var config = ConfigStore.Load();
        var savedAddress = config.BeamNgOutGaugeAddress;
        var address = new TextBox { Text = savedAddress, PlaceholderText = BeamNgListenAddress.Default };
        var addressField = SaveFieldCard("\uE968", L("Adresă OutGauge", "OutGauge address"),
            L("Unde ascultă Octoglow Connect: 127.0.0.1 dacă BeamNG.drive rulează pe acest calculator, altfel adresa acestui calculator din rețea (aceeași ca în joc) sau 0.0.0.0 pentru toate.",
              "Where Octoglow Connect listens: 127.0.0.1 if BeamNG.drive runs on this computer, otherwise this computer's network address (the same as in the game) or 0.0.0.0 for all of them."),
            address,
            () => address.Text.Trim() != savedAddress,
            () =>
            {
                var value = address.Text.Trim();
                if (!BeamNgListenAddress.TryParse(value, out var parsed))
                {
                    ShowError(L("Scrie o adresă IPv4, de exemplu 127.0.0.1.", "Enter an IPv4 address, for example 127.0.0.1."));
                    return Task.CompletedTask;
                }
                if (!BeamNgListenAddress.IsLocal(parsed))
                {
                    ShowError(L($"Adresa {value} nu este a acestui calculator. Folosește 127.0.0.1, 0.0.0.0 sau una dintre adresele lui din rețea.",
                                $"{value} isn't an address of this computer. Use 127.0.0.1, 0.0.0.0 or one of its network addresses."));
                    return Task.CompletedTask;
                }
                SaveAppConfig(saved => saved.BeamNgOutGaugeAddress = value);
                savedAddress = value;
                address.Text = value;
                return Task.CompletedTask;
            });
        address.TextChanged += (_, _) => addressField.Refresh();
        OnEnter(address, addressField.Submit);
        Add(addressField.Card);

        var savedPort = config.BeamNgOutGaugePort;
        var port = new TextBox { Text = Str(savedPort), PlaceholderText = "4444" };
        var portField = SaveFieldCard("\uE71B", L("Port OutGauge", "OutGauge port"),
            L("Același port ca în BeamNG.drive, la Options › Advanced › Protocols › OutGauge UDP protocol. Implicit 4444.",
              "The same port as in BeamNG.drive, under Options › Advanced › Protocols › OutGauge UDP protocol. 4444 by default."),
            port,
            () => port.Text.Trim() != Str(savedPort),
            () =>
            {
                if (!int.TryParse(port.Text.Trim(), NumberStyles.Integer, Inv, out var number) || number is < 1 or > 65535)
                {
                    ShowError(L("Portul este un număr între 1 și 65535.", "The port is a number from 1 to 65535."));
                    return Task.CompletedTask;
                }
                SaveAppConfig(saved => saved.BeamNgOutGaugePort = number);
                savedPort = number;
                port.Text = Str(number);
                return Task.CompletedTask;
            });
        port.TextChanged += (_, _) => portField.Refresh();
        OnEnter(port, portField.Submit);
        Add(portField.Card);

        var stop = Toggle(config.StopBeamNgTileWhenVehicleStops);
        stop.Toggled += (_, _) => SaveAppConfig(saved => saved.StopBeamNgTileWhenVehicleStops = stop.IsOn);
        Add(SettingsCard("\uE7FC", L("Oprește tile-ul prioritar când vehiculul stă pe loc",
            "Stop the priority tile when the vehicle stands still"), null, stop));
        return null;
    }

    // The game needs nothing set up, so all there is is whether a standing car
    // keeps the tile up. How the speed is drawn is the tile's own, in the Tile Manager.
    private Action? BuildAssettoCorsaPage()
    {
        var config = ConfigStore.Load();
        var stop = Toggle(config.StopAssettoTileWhenCarStops);
        stop.Toggled += (_, _) => SaveAppConfig(saved => saved.StopAssettoTileWhenCarStops = stop.IsOn);
        Add(SettingsCard("\uE7FC", L("Oprește tile-ul prioritar când mașina stă pe loc",
            "Stop the priority tile when the car stands still"), null, stop));
        return null;
    }

    // What is read from the game now, the unit the clock shows the speed in,
    // and whether a car standing still keeps the tile up. The gauge and the
    // font are the tile's own, in the Tile Manager.
    private Action? BuildNfsu2Page()
    {
        AddCompatibleVersionNotice(L("Citirea vitezei este verificată pentru SPEED2.EXE v1.2 NTSC.",
            "Speed reading is verified for SPEED2.EXE v1.2 NTSC."));
        var status = ValueText(Nfsu2ShortText(Nfsu2Status.Current));
        var statusCard = SettingsCard(PriorityGlyph("nfsu2"), L("Stare", "Status"), Nfsu2StateText(Nfsu2Status.Current), status);
        var statusDescription = CardDescription(statusCard);
        Add(statusCard);
        var speed = ValueText("-");
        var speedCard = SettingsCard("\uEC4A", L("Viteza citită", "Speed read"),
            L("Ce trimite Octoglow Connect ceasului, cum o arată și vitezometrul jocului.",
              "What Octoglow Connect sends the clock, as the game's speedometer shows it."), speed);
        Add(speedCard);

        AddSection(L("Afișare", "Display"));
        // The clock keeps the unit; it is read when the page opens.
        var unit = ChoiceBox([("KM/H", "0"), ("MPH", "1")], "0", 140);
        unit.IsEnabled = false;
        Add(SettingsCard("\uEC4A", L("Unitate de viteză", "Speed unit"),
            L("Aceeași cu cea din setările tile-ului, în Manager de tile-uri.", "The same as in the tile's settings, in the Tile Manager."), unit));
        var config = ConfigStore.Load();
        var stop = Toggle(config.StopNfsu2TileWhenCarStops);
        stop.Toggled += (_, _) => SaveAppConfig(saved => saved.StopNfsu2TileWhenCarStops = stop.IsOn);
        Add(SettingsCard("\uE7FC", L("Oprește tile-ul prioritar când mașina stă pe loc",
            "Stop the priority tile when the car stands still"), null, stop));

        var cancellation = new CancellationTokenSource();
        async void ReadUnit()
        {
            var clock = await TryGetJsonAsync("/nfsu2state", cancellation.Token);
            if (clock is null || cancellation.IsCancellationRequested) return;
            SelectChoice(unit, clock.Bool("mph") ? "1" : "0");
            unit.IsEnabled = true;
            unit.SelectionChanged += (_, _) => QueueSave("speedunit:nfsu2Mph",
                token => Post(token, "/speedunit", ("nfsu2Mph", ChoiceValue(unit))), 0);
        }
        ReadUnit();

        void Refresh()
        {
            var now = Nfsu2Status.Current;
            status.Text = Nfsu2ShortText(now);
            if (statusDescription is not null) statusDescription.Text = Nfsu2StateText(now);
            var driving = now.Reading && now.Game.State == Nfsu2State.Driving;
            speed.Text = driving ? Str(now.Game.Kmh) + " km/h · " + Str(now.Game.Mph) + " mph" : "-";
            speedCard.Opacity = driving ? 1 : 0.6;
        }
        Refresh();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        return () =>
        {
            timer.Stop();
            cancellation.Cancel();
        };
    }

    // A word or two, for the Integrations card and beside the status.
    private static string Nfsu2ShortText(Nfsu2Snapshot snapshot) =>
        snapshot.ClockUnsupported ? L("Firmware vechi", "Old firmware")
        : !snapshot.Reading ? L("Inactiv", "Inactive")
        : snapshot.Game.State switch
        {
            Nfsu2State.Driving => L("Activ", "Active"),
            Nfsu2State.Paused => L("Pe pauză", "Paused"),
            Nfsu2State.WaitingForRace => L("Așteaptă cursa", "Waiting for a race"),
            Nfsu2State.UnsupportedVersion => L("Versiune neacceptată", "Unsupported version"),
            Nfsu2State.NoAccess => L("Fără acces", "No access"),
            _ => L("Joc negăsit", "Game not found"),
        };

    // What the state means and what to do about it.
    private static string Nfsu2StateText(Nfsu2Snapshot snapshot)
    {
        if (snapshot.ClockUnsupported)
            return L("Firmware-ul ceasului nu are încă tile-ul NFS Underground 2. Actualizează firmware-ul ceasului.",
                "The clock's firmware has no NFS Underground 2 tile yet. Update the clock's firmware.");
        if (!snapshot.Reading)
            return L("Oprit. Pornește tile-ul NFS Underground 2 din Manager de tile-uri; până atunci jocul nu este citit.",
                "Off. Turn the NFS Underground 2 tile on in the Tile Manager; until then the game isn't read.");
        return snapshot.Game.State switch
        {
            Nfsu2State.Driving => L("Viteza mașinii tale se citește din joc și ajunge pe ceas.",
                "Your car's speed is read from the game and reaches the clock."),
            Nfsu2State.Paused => L("Jocul este pe pauză: tile-ul nu apare până nu continui cursa.",
                "The game is paused: the tile stays down until the race goes on."),
            Nfsu2State.WaitingForRace => snapshot.Problem is { } problem
                ? L("Datele citite din joc nu trec verificarea (", "The data read from the game fails its checks (") + problem +
                  L("), așa că viteza nu se afișează.", "), so the speed isn't shown.")
                : L("Jocul rulează. Viteza apare când ești într-o cursă sau în free roam.",
                    "The game is running. The speed shows once you're in a race or in free roam."),
            Nfsu2State.UnsupportedVersion => L("SPEED2.EXE nu este versiunea v1.2 NTSC (4.800.512 bytes) pentru care s-a verificat citirea vitezei, așa că nu este citit.",
                "SPEED2.EXE isn't the v1.2 NTSC build (4,800,512 bytes) the speed reading was checked on, so it isn't read."),
            Nfsu2State.NoAccess => L("Jocul rulează ca administrator, iar Octoglow Connect nu. Pornește-le pe amândouă la fel.",
                "The game runs as administrator and Octoglow Connect doesn't. Start both the same way."),
            _ => L("Need for Speed Underground 2 nu rulează.", "Need for Speed Underground 2 isn't running."),
        };
    }

    // What is read from the game now, the unit the clock shows the speed in,
    // and whether a car standing still keeps the tile up. The gauge and the
    // font are the tile's own, in the Tile Manager.
    private Action? BuildNfsMwPage()
    {
        AddCompatibleVersionNotice(L("Citirea vitezei este verificată pentru speed.exe v1.3.",
            "Speed reading is verified for speed.exe v1.3."));
        var status = ValueText(NfsMwShortText(NfsMwStatus.Current));
        var statusCard = SettingsCard(PriorityGlyph("nfsmw"), L("Stare", "Status"), NfsMwStateText(NfsMwStatus.Current), status);
        var statusDescription = CardDescription(statusCard);
        Add(statusCard);
        var speed = ValueText("-");
        var speedCard = SettingsCard("\uEC4A", L("Viteza citită", "Speed read"),
            L("Ce trimite Octoglow Connect ceasului, cum o arată și vitezometrul jocului.",
              "What Octoglow Connect sends the clock, as the game's speedometer shows it."), speed);
        Add(speedCard);

        AddSection(L("Afișare", "Display"));
        // The clock keeps the unit; it is read when the page opens.
        var unit = ChoiceBox([("KM/H", "0"), ("MPH", "1")], "0", 140);
        unit.IsEnabled = false;
        Add(SettingsCard("\uEC4A", L("Unitate de viteză", "Speed unit"),
            L("Aceeași cu cea din setările tile-ului, în Manager de tile-uri.", "The same as in the tile's settings, in the Tile Manager."), unit));
        var config = ConfigStore.Load();
        var stop = Toggle(config.StopNfsMwTileWhenCarStops);
        stop.Toggled += (_, _) => SaveAppConfig(saved => saved.StopNfsMwTileWhenCarStops = stop.IsOn);
        Add(SettingsCard("\uE7FC", L("Oprește tile-ul prioritar când mașina stă pe loc",
            "Stop the priority tile when the car stands still"), null, stop));

        var cancellation = new CancellationTokenSource();
        async void ReadUnit()
        {
            var clock = await TryGetJsonAsync("/nfsmwstate", cancellation.Token);
            if (clock is null || cancellation.IsCancellationRequested) return;
            SelectChoice(unit, clock.Bool("mph") ? "1" : "0");
            unit.IsEnabled = true;
            unit.SelectionChanged += (_, _) => QueueSave("speedunit:nfsmwMph",
                token => Post(token, "/speedunit", ("nfsmwMph", ChoiceValue(unit))), 0);
        }
        ReadUnit();

        void Refresh()
        {
            var now = NfsMwStatus.Current;
            status.Text = NfsMwShortText(now);
            if (statusDescription is not null) statusDescription.Text = NfsMwStateText(now);
            var driving = now.Reading && now.Game.State == NfsMwState.Driving;
            speed.Text = driving ? Str(now.Game.Kmh) + " km/h · " + Str(now.Game.Mph) + " mph" : "-";
            speedCard.Opacity = driving ? 1 : 0.6;
        }
        Refresh();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        return () =>
        {
            timer.Stop();
            cancellation.Cancel();
        };
    }

    // A word or two, for the Integrations card and beside the status.
    private static string NfsMwShortText(NfsMwSnapshot snapshot) =>
        snapshot.ClockUnsupported ? L("Firmware vechi", "Old firmware")
        : !snapshot.Reading ? L("Inactiv", "Inactive")
        : snapshot.Game.State switch
        {
            NfsMwState.Driving => L("Activ", "Active"),
            NfsMwState.Paused => L("Pe pauză", "Paused"),
            NfsMwState.WaitingForRace => L("Așteaptă cursa", "Waiting for a race"),
            NfsMwState.UnsupportedVersion => L("Versiune neacceptată", "Unsupported version"),
            NfsMwState.NoAccess => L("Fără acces", "No access"),
            _ => L("Joc negăsit", "Game not found"),
        };

    // What the state means and what to do about it.
    private static string NfsMwStateText(NfsMwSnapshot snapshot)
    {
        if (snapshot.ClockUnsupported)
            return L("Firmware-ul ceasului nu are încă tile-ul NFS Most Wanted. Actualizează firmware-ul ceasului.",
                "The clock's firmware has no NFS Most Wanted tile yet. Update the clock's firmware.");
        if (!snapshot.Reading)
            return L("Oprit. Pornește tile-ul NFS Most Wanted din Manager de tile-uri; până atunci jocul nu este citit.",
                "Off. Turn the NFS Most Wanted tile on in the Tile Manager; until then the game isn't read.");
        return snapshot.Game.State switch
        {
            NfsMwState.Driving => L("Viteza mașinii tale se citește din joc și ajunge pe ceas.",
                "Your car's speed is read from the game and reaches the clock."),
            NfsMwState.Paused => L("Jocul este pe pauză: tile-ul nu apare până nu continui cursa.",
                "The game is paused: the tile stays down until the race goes on."),
            NfsMwState.WaitingForRace => snapshot.Problem is { } problem
                ? L("Datele citite din joc nu trec verificarea (", "The data read from the game fails its checks (") + problem +
                  L("), așa că viteza nu se afișează.", "), so the speed isn't shown.")
                : L("Jocul rulează. Viteza apare când ești într-o cursă sau în free roam.",
                    "The game is running. The speed shows once you're in a race or in free roam."),
            NfsMwState.UnsupportedVersion => L("speed.exe nu este versiunea v1.3 (6.029.312 bytes) pentru care s-a verificat citirea vitezei, așa că nu este citit.",
                "speed.exe isn't the v1.3 build (6,029,312 bytes) the speed reading was checked on, so it isn't read."),
            NfsMwState.NoAccess => L("Jocul rulează ca administrator, iar Octoglow Connect nu. Pornește-le pe amândouă la fel.",
                "The game runs as administrator and Octoglow Connect doesn't. Start both the same way."),
            _ => L("Need for Speed Most Wanted nu rulează.", "Need for Speed Most Wanted isn't running."),
        };
    }

    // What is read from the game now, the unit the clock shows the speed in,
    // and whether a car standing still keeps the tile up. The gauge and the
    // font are the tile's own, in the Tile Manager.
    private Action? BuildNfsCarbonPage()
    {
        AddCompatibleVersionNotice(L("Citirea vitezei este verificată pentru NFSC.exe v1.4.",
            "Speed reading is verified for NFSC.exe v1.4."));
        var status = ValueText(NfsCarbonShortText(NfsCarbonStatus.Current));
        var statusCard = SettingsCard(PriorityGlyph("nfsc"), L("Stare", "Status"), NfsCarbonStateText(NfsCarbonStatus.Current), status);
        var statusDescription = CardDescription(statusCard);
        Add(statusCard);
        var speed = ValueText("-");
        var speedCard = SettingsCard("\uEC4A", L("Viteza citită", "Speed read"),
            L("Ce trimite Octoglow Connect ceasului, cum o arată și vitezometrul jocului.",
              "What Octoglow Connect sends the clock, as the game's speedometer shows it."), speed);
        Add(speedCard);

        AddSection(L("Afișare", "Display"));
        // The clock keeps the unit; it is read when the page opens.
        var unit = ChoiceBox([("KM/H", "0"), ("MPH", "1")], "0", 140);
        unit.IsEnabled = false;
        Add(SettingsCard("\uEC4A", L("Unitate de viteză", "Speed unit"),
            L("Aceeași cu cea din setările tile-ului, în Manager de tile-uri.", "The same as in the tile's settings, in the Tile Manager."), unit));
        var config = ConfigStore.Load();
        var stop = Toggle(config.StopNfsCarbonTileWhenCarStops);
        stop.Toggled += (_, _) => SaveAppConfig(saved => saved.StopNfsCarbonTileWhenCarStops = stop.IsOn);
        Add(SettingsCard("\uE7FC", L("Oprește tile-ul prioritar când mașina stă pe loc",
            "Stop the priority tile when the car stands still"), null, stop));

        var cancellation = new CancellationTokenSource();
        async void ReadUnit()
        {
            var clock = await TryGetJsonAsync("/nfscstate", cancellation.Token);
            if (clock is null || cancellation.IsCancellationRequested) return;
            SelectChoice(unit, clock.Bool("mph") ? "1" : "0");
            unit.IsEnabled = true;
            unit.SelectionChanged += (_, _) => QueueSave("speedunit:nfscMph",
                token => Post(token, "/speedunit", ("nfscMph", ChoiceValue(unit))), 0);
        }
        ReadUnit();

        void Refresh()
        {
            var now = NfsCarbonStatus.Current;
            status.Text = NfsCarbonShortText(now);
            if (statusDescription is not null) statusDescription.Text = NfsCarbonStateText(now);
            var driving = now.Reading && now.Game.State == NfsCarbonState.Driving;
            speed.Text = driving ? Str(now.Game.Kmh) + " km/h · " + Str(now.Game.Mph) + " mph" : "-";
            speedCard.Opacity = driving ? 1 : 0.6;
        }
        Refresh();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        return () =>
        {
            timer.Stop();
            cancellation.Cancel();
        };
    }

    // A word or two, for the Integrations card and beside the status.
    private static string NfsCarbonShortText(NfsCarbonSnapshot snapshot) =>
        snapshot.ClockUnsupported ? L("Firmware vechi", "Old firmware")
        : !snapshot.Reading ? L("Inactiv", "Inactive")
        : snapshot.Game.State switch
        {
            NfsCarbonState.Driving => L("Activ", "Active"),
            NfsCarbonState.Paused => L("Pe pauză", "Paused"),
            NfsCarbonState.WaitingForRace => L("Așteaptă cursa", "Waiting for a race"),
            NfsCarbonState.UnsupportedVersion => L("Versiune neacceptată", "Unsupported version"),
            NfsCarbonState.NoAccess => L("Fără acces", "No access"),
            _ => L("Joc negăsit", "Game not found"),
        };

    // What the state means and what to do about it.
    private static string NfsCarbonStateText(NfsCarbonSnapshot snapshot)
    {
        if (snapshot.ClockUnsupported)
            return L("Firmware-ul ceasului nu are încă tile-ul NFS Carbon. Actualizează firmware-ul ceasului.",
                "The clock's firmware has no NFS Carbon tile yet. Update the clock's firmware.");
        if (!snapshot.Reading)
            return L("Oprit. Pornește tile-ul NFS Carbon din Manager de tile-uri; până atunci jocul nu este citit.",
                "Off. Turn the NFS Carbon tile on in the Tile Manager; until then the game isn't read.");
        return snapshot.Game.State switch
        {
            NfsCarbonState.Driving => L("Viteza mașinii tale se citește din joc și ajunge pe ceas.",
                "Your car's speed is read from the game and reaches the clock."),
            NfsCarbonState.Paused => L("Jocul este pe pauză: tile-ul nu apare până nu continui cursa.",
                "The game is paused: the tile stays down until the race goes on."),
            NfsCarbonState.WaitingForRace => snapshot.Problem is { } problem
                ? L("Datele citite din joc nu trec verificarea (", "The data read from the game fails its checks (") + problem +
                  L("), așa că viteza nu se afișează.", "), so the speed isn't shown.")
                : L("Jocul rulează. Viteza apare când ești într-o cursă sau în free roam.",
                    "The game is running. The speed shows once you're in a race or in free roam."),
            NfsCarbonState.UnsupportedVersion => L("NFSC.exe nu este versiunea v1.4 (7.217.152 bytes) pentru care s-a verificat citirea vitezei, așa că nu este citit.",
                "NFSC.exe isn't the v1.4 build (7,217,152 bytes) the speed reading was checked on, so it isn't read."),
            NfsCarbonState.NoAccess => L("Jocul rulează ca administrator, iar Octoglow Connect nu. Pornește-le pe amândouă la fel.",
                "The game runs as administrator and Octoglow Connect doesn't. Start both the same way."),
            _ => L("Need for Speed Carbon nu rulează.", "Need for Speed Carbon isn't running."),
        };
    }

    private void BuildVoicemeeterPage()
    {
        var config = ConfigStore.Load();
        var bus = ChoiceBox(
            [
                ("B1 · Voicemeeter Output", "B1"),
                ("B2 · Voicemeeter AUX Output", "B2"),
                ("B3 · Voicemeeter VAIO3 Output", "B3"),
            ],
            config.VoicemeeterBus,
            260);
        bus.SelectionChanged += (_, _) => SaveAppConfig(saved => saved.VoicemeeterBus = ChoiceValue(bus));
        Add(SettingsCard("\uE767", L("Bus monitorizat", "Monitored bus"),
            L("Sunetul trimis către busul ales controlează tile-ul Voicemeeter.",
              "Audio routed to the selected bus controls the Voicemeeter tile."), bus));

        var autoStop = Toggle(config.VoicemeeterAutoStopOnSilence);
        var silence = new NumberBox
        {
            Minimum = 1,
            Maximum = 30,
            Value = config.VoicemeeterSilenceSeconds,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 110,
        };
        autoStop.Toggled += (_, _) => SaveAppConfig(saved => saved.VoicemeeterAutoStopOnSilence = autoStop.IsOn);
        silence.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue)) return;
            var seconds = Math.Clamp((int)Math.Round(args.NewValue), 1, 30);
            SaveAppConfig(saved => saved.VoicemeeterSilenceSeconds = seconds);
        };
        Add(ToggleGroup("\uE71A", L("Oprește tile-ul la liniște", "Stop the tile during silence"),
            L("Ceasul revine automat la circuit când busul nu mai produce sunet.",
              "The clock automatically returns to its circuit when the bus becomes silent."), autoStop,
            [
                SubRow(L("Timp până la oprire", "Time before stopping"),
                    L("Câte secunde de liniște sunt ignorate înainte ca tile-ul să dispară.",
                      "How many silent seconds are ignored before the tile disappears."),
                    Inline(silence, new TextBlock { Text = "s", Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center })),
            ]));
    }

    private Expander PlayerListExpander(
        string glyph,
        string title,
        string placeholder,
        IEnumerable<NowPlayingPlayer> initial,
        Action<AppConfig, List<NowPlayingPlayer>> store)
    {
        var players = initial.Select(player => player.Clone()).ToList();
        var count = ValueText("");
        var list = new StackPanel();

        var name = new TextBox { PlaceholderText = placeholder, MaxLength = 64 };
        var add = ActionButton(L("Adaugă", "Add"), "");
        var addRow = new Grid { ColumnSpacing = 8, Padding = new Thickness(52, 12, 16, 12) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(name);
        Grid.SetColumn(add, 1);
        addRow.Children.Add(add);

        void Save() => SaveAppConfig(saved => store(saved, players.Select(player => player.Clone()).ToList()));

        void Render()
        {
            count.Text = players.Count == 1
                ? L("1 player", "1 player")
                : L($"{players.Count} playere", $"{players.Count} players");
            list.Children.Clear();
            if (players.Count == 0)
            {
                var empty = Secondary(L("Niciun player în listă.", "No players in the list."));
                empty.Margin = new Thickness(52, 14, 16, 14);
                list.Children.Add(empty);
            }
            foreach (var player in players)
            {
                if (list.Children.Count > 0) list.Children.Add(Divider());
                var remove = new Button
                {
                    Content = new FontIcon { Glyph = "", FontSize = 14 },
                    Width = 36,
                    Height = 36,
                    Padding = new Thickness(0),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                };
                var label = L("Elimină ", "Remove ") + player.Name;
                ToolTipService.SetToolTip(remove, label);
                AutomationProperties.SetName(remove, label);
                remove.Click += (_, _) =>
                {
                    players.Remove(player);
                    Save();
                    Render();
                };
                list.Children.Add(SubRow(player.Name, null, remove));
            }
        }

        void AddPlayer()
        {
            var text = name.Text.Trim();
            if (text.Length == 0) return;
            if (players.Any(player => string.Equals(player.Name, text, StringComparison.OrdinalIgnoreCase)))
            {
                ShowError(L($"„{text}” e deja în listă.", $"\"{text}\" is already in the list."));
                return;
            }
            // How the app recognises the player: its name in lower case, letters and digits only.
            players.Add(new NowPlayingPlayer
            {
                Name = text,
                Match = string.Concat(text.ToLowerInvariant().Where(char.IsLetterOrDigit)),
                Enabled = true,
            });
            name.Text = "";
            Save();
            Render();
        }

        add.Click += (_, _) => AddPlayer();
        OnEnter(name, AddPlayer);
        Render();
        return SettingsExpander(glyph, title, null, count, [list, addRow]);
    }

    // Read again right before saving, so a change made elsewhere in the app meanwhile isn't overwritten.
    private void SaveAppConfig(Action<AppConfig> change)
    {
        try
        {
            ConfigStore.Update(change);
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }
}
