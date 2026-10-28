using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// The clock itself, each in a panel of its own under Configuration: its
// brightness, the Wi-Fi network it joins and the Access Point it can make,
// and the account the app and the browser sign in with.
public sealed partial class ClockNativeSettingsPage
{
    // ------------------------------------------------------------------ brightness

    private sealed class BrightnessSettings
    {
        public int Level { get; set; }
        public bool Auto { get; set; }
        public string From { get; set; } = "22:00";
        public string To { get; set; } = "07:00";
        public int DimLevel { get; set; }
    }

    private void BuildBrightness(ClockDeviceState state)
    {
        var settings = new BrightnessSettings
        {
            Level = state.Int("bright", 4),
            Auto = state.Bool("dimAuto"),
            From = state.String("dimFrom", "22:00"),
            To = state.String("dimTo", "07:00"),
            DimLevel = state.Int("dimLevel", 1),
        };
        // /brightness takes the whole set every time.
        void Send(int delay) => QueueSave("brightness", token => Post(token, "/brightness",
            ("level", Str(settings.Level)),
            ("dimAuto", settings.Auto ? "1" : "0"),
            ("dimFrom", settings.From),
            ("dimTo", settings.To),
            ("dimLevel", Str(settings.DimLevel))), delay);

        AddSection(L("Ecran", "Screen"));
        var level = NewSlider(0, 16, settings.Level, 240);
        level.ValueChanged += (_, args) =>
        {
            settings.Level = (int)Math.Round(args.NewValue);
            Send(150);
        };
        Add(SettingsCard("", L("Nivel", "Level"),
            L("Cât de puternic luminează matricea", "How bright the matrix glows"),
            SliderWithValue(level, value => Str((int)Math.Round(value)))));

        AddSection(L("Noapte", "Night"));
        var automatic = Toggle(settings.Auto);
        automatic.Toggled += (_, _) =>
        {
            settings.Auto = automatic.IsOn;
            Send(0);
        };
        var from = TimeBox(settings.From);
        from.TimeChanged += (_, args) =>
        {
            settings.From = FormatTime(args.NewTime);
            Send(0);
        };
        var to = TimeBox(settings.To);
        to.TimeChanged += (_, args) =>
        {
            settings.To = FormatTime(args.NewTime);
            Send(0);
        };
        var dimLevel = NewSlider(0, 16, settings.DimLevel, 200);
        dimLevel.ValueChanged += (_, args) =>
        {
            settings.DimLevel = (int)Math.Round(args.NewValue);
            Send(150);
        };
        Add(ToggleGroup("", L("Dim automat", "Auto Dim"),
            L("Reduce luminozitatea noaptea", "Reduce brightness at night"),
            automatic,
            [
                SubRow(L("Interval", "Interval"), L("De la / până la", "From / to"),
                    Inline(from, new TextBlock { Text = "-", Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center }, to)),
                SubRow(L("Luminozitate noapte", "Night brightness"), L("Nivelul folosit în interval", "The level used during the interval"),
                    SliderWithValue(dimLevel, value => Str((int)Math.Round(value)))),
            ]));
        AddLookingElseLinks((ClockSettingsSection.Power, L("Somn automat", "Automatic Sleep")));
    }

    // ------------------------------------------------------------------ wi-fi

    private sealed record WifiNetwork(string Ssid, int Rssi, bool Secured, string Auth);

    private Action? BuildWifi(ClockDeviceState state)
    {
        var inAccessPoint = state.Bool("ap");
        var savedSsid = state.String("ssid");
        var apSsid = state.String("apSsid", "Octoglow");
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var scan = ActionButton(L("Căutați din nou", "Scan again"), "");
        Add(SectionWithAction(L("Rețele WiFi", "WiFi Networks"), scan));
        var networks = new StackPanel { Spacing = 4 };
        Add(networks);
        var hidden = NavCard("", L("Rețea ascunsă", "Hidden Network"),
            L("Conectați ceasul la o rețea care nu își afișează numele", "Connect the clock to a network that doesn't show its name"),
            () => _ = ConnectToNetworkAsync(null, true));
        hidden.Content = HeaderGrid("", L("Rețea ascunsă", "Hidden Network"),
            L("Conectați ceasul la o rețea care nu își afișează numele", "Connect the clock to a network that doesn't show its name"), null);
        Add(hidden);
        scan.Click += (_, _) => _ = ScanNetworksAsync(networks, scan, savedSsid, inAccessPoint, token);
        async Task AutoScanAsync()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await ScanNetworksAsync(networks, scan, savedSsid, inAccessPoint, token);
                    await Task.Delay(TimeSpan.FromSeconds(15), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        _ = AutoScanAsync();
        return cancellation.Cancel;
    }

    // ------------------------------------------------------------------ access point

    private void BuildAccessPoint(ClockDeviceState state)
    {
        var inAccessPoint = state.Bool("ap");
        var apSsid = state.String("apSsid", "Octoglow");

        AddSection(L("Configurare rețea AP", "AP Network Configuration"));
        var savedApName = apSsid;
        var apName = new TextBox { Text = apSsid, MaxLength = 32 };
        var nameField = SaveFieldCard("\uE701", L("Nume rețea (SSID)", "Network name (SSID)"),
            "",
            apName,
            () => apName.Text.Trim() is { Length: > 0 } name && name != savedApName,
            async () =>
            {
                var name = apName.Text.Trim();
                if (await SaveAccessPointAsync(("ssid", name))) savedApName = name;
            });
        apName.TextChanged += (_, _) => nameField.Refresh();
        OnEnter(apName, nameField.Submit);
        Add(nameField.Card);

        var (passwordView, apPassword) = SecretField("", L("Parola rețea", "Network password"));
        var passwordField = SaveFieldCard("\uE72E", L("Parola", "Password"),
            L("Minim 8 caractere; parola curentă rămâne până salvați alta", "Minimum 8 characters; the current password stays until you save another"),
            passwordView,
            () => apPassword.Password.Length > 0,
            async () =>
            {
                if (apPassword.Password.Length < 8)
                {
                    ShowError(L("Parola trebuie să aibă cel puțin 8 caractere.", "The password needs at least 8 characters."));
                    return;
                }
                // The network name goes along: the clock wants it with every change.
                if (await SaveAccessPointAsync(("ssid", savedApName), ("pass", apPassword.Password))) apPassword.Password = "";
            });
        apPassword.PasswordChanged += (_, _) => passwordField.Refresh();
        OnEnter(apPassword, passwordField.Submit);
        Add(passwordField.Card);

        var toggleAp = ActionCard("\uE88A",
            inAccessPoint ? L("Comutați în modul WiFi", "Switch to WiFi mode") : L("Comutați în modul AP", "Switch to AP mode"),
            inAccessPoint ? L("Modul Access Point este pornit acum.", "Access Point mode is on right now.")
                          : L("Ceasul este conectat acum la rețeaua WiFi.", "The clock is on its WiFi network right now."),
            "", null).Card;
        toggleAp.Click += async (_, _) =>
        {
            var confirmed = await ConfirmAsync(
                inAccessPoint ? L("Comutare în modul WiFi", "Switch to WiFi mode") : L("Comutare în modul AP", "Switch to AP mode"),
                inAccessPoint
                    ? L("Doriți să opriți modul Access Point și să vă reconectați la rețeaua WiFi salvată anterior?", "Do you want to stop Access Point mode and reconnect to the previously saved WiFi network?")
                    // Said first here: the app only finds the clock again from the clock's own network.
                    : L($"Aplicația pierde legătura cu ceasul până când și calculatorul se conectează la rețeaua „{apSsid}”. Doriți să opriți conexiunea WiFi și să porniți modul Access Point?",
                        $"The app loses the clock until this computer joins the \"{apSsid}\" network too. Do you want to stop the WiFi connection and start Access Point mode?"),
                L("Confirmați", "Confirm"));
            if (!confirmed) return;
            await RunActionAsync(toggleAp, action => Post(action, inAccessPoint ? "/stopap" : "/startap"),
                L("Comanda a fost trimisă. Legătura cu ceasul se poate întrerupe câteva secunde.", "Command sent. The connection to the clock may drop for a few seconds."));
        };
        Add(toggleAp);
        AddLookingElseLinks((ClockSettingsSection.About, L("Vedeți adresa IP", "View IP Address")));
    }

    private async Task<bool> SaveAccessPointAsync(params (string Key, string Value)[] values)
    {
        Surface.Status.IsOpen = false;
        try
        {
            await Post(CancellationToken.None, "/apsett", values);
            ShowSuccess(L("Setările Access Point-ului au fost salvate.", "The Access Point settings are saved."));
            return true;
        }
        catch (Exception error)
        {
            ShowError(FriendlyError(error));
            return false;
        }
    }

    private async Task ShowWifiConnectionAsync(StackPanel host, bool inAccessPoint, string apSsid, CancellationToken token)
    {
        FrameworkElement card;
        if (inAccessPoint)
        {
            card = SettingsCard("", L("Access Point pornit", "Access Point on"),
                L($"Ceasul nu e conectat la o rețea Wi-Fi; face propria rețea, „{apSsid}”.", $"The clock isn't on a Wi-Fi network; it makes its own, \"{apSsid}\"."));
        }
        else
        {
            ClockDeviceState info;
            try
            {
                info = await ClockDeviceApi.GetJsonAsync(_device, "/wifiinfo", token);
            }
            catch when (!token.IsCancellationRequested)
            {
                host.Children.Clear();
                host.Children.Add(SettingsCard("", L("Detaliile nu sunt disponibile", "Details unavailable"),
                    L("Ceasul nu a trimis detaliile conexiunii. Firmware-ul poate fi prea vechi.", "The clock didn't send its connection details. Its firmware may be too old.")));
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (token.IsCancellationRequested) return;

            if (!info.Bool("connected"))
            {
                card = SettingsCard("", L("Neconectat", "Not connected"),
                    L("Ceasul nu e conectat acum la nicio rețea Wi-Fi.", "The clock isn't connected to a Wi-Fi network right now."));
            }
            else
            {
                var rssi = info.Int("rssi", -100);
                var rows = new List<UIElement>
                {
                    SubRow(L("Semnal", "Signal"), SignalQuality(rssi), ValueText($"{rssi} dBm")),
                };
                if (info.String("phy").Length > 0)
                {
                    rows.Add(SubRow(L("Viteză maximă", "Top speed"), $"{info.String("phy")} · {info.Int("bw", 20)} MHz",
                        ValueText($"{info.Int("maxMbps")} Mbit/s")));
                }
                rows.Add(SubRow(L("Canal", "Channel"), null, ValueText(Str(info.Int("channel")))));
                rows.Add(SubRow(L("Adresa IP", "IP Address"), null, ValueText(info.String("ip", "-"))));
                rows.Add(SubRow(L("Router", "Router"), null, ValueText(info.String("gw", "-"))));
                rows.Add(SubRow(L("MAC-ul rețelei", "Network MAC"), null, ValueText(info.String("bssid", "-"))));
                rows.Add(SubRow(L("Mască de rețea", "Subnet mask"), null, ValueText(info.String("mask", "-"))));
                rows.Add(SubRow("DNS", null, ValueText(info.String("dns", "-"))));
                rows.Add(SubRow(L("Adresa MAC a ceasului", "The clock's MAC address"), null, ValueText(info.String("mac", "-"))));
                var details = new StackPanel { Spacing = 4 };
                foreach (var row in rows) details.Children.Add(row);
                card = details;
            }
        }
        host.Children.Clear();
        host.Children.Add(card);
    }

    // The clock scans in the background and answers "still scanning" until it has
    // something, so the list is asked for again a moment later, as the web interface does.
    private async Task ScanNetworksAsync(StackPanel host, Button scan, string savedSsid, bool inAccessPoint, CancellationToken token)
    {
        if (!scan.IsEnabled || token.IsCancellationRequested) return;
        scan.IsEnabled = false;
        if (host.Children.Count == 0)
            host.Children.Add(SettingsCard("", L("Se caută rețele…", "Scanning for networks…"), null,
                new ProgressRing { IsActive = true, Width = 18, Height = 18 }));
        try
        {
            var connection = await TryGetJsonAsync("/wifiinfo", token);
            if (connection is not null)
            {
                inAccessPoint = !connection.Bool("connected");
                savedSsid = inAccessPoint ? "" : connection.String("ssid");
            }
            ClockDeviceState? result = null;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                result = await ClockDeviceApi.GetJsonAsync(_device, "/scan", token);
                if (!result.Bool("scanning")) break;
                await Task.Delay(800, token);
            }
            if (token.IsCancellationRequested || result is null) return;

            host.Children.Clear();
            if (result.Bool("scanning"))
            {
                host.Children.Add(SettingsCard("", L("Scanarea durează prea mult", "The scan is taking too long"),
                    L("Încearcă din nou peste câteva secunde.", "Try again in a few seconds.")));
                return;
            }
            // One row per name: a network with several access points shows its strongest.
            var found = result.Objects("networks")
                .Select(network => new WifiNetwork(network.String("ssid"), network.Int("rssi", -100), network.Bool("secured"), network.String("auth")))
                .Where(network => network.Ssid.Length > 0)
                .GroupBy(network => network.Ssid)
                .Select(group => group.MaxBy(network => network.Rssi)!)
                .OrderByDescending(network => !inAccessPoint && network.Ssid == savedSsid)
                .ThenByDescending(network => network.Rssi)
                .ToList();
            if (!inAccessPoint && savedSsid.Length > 0 && found.All(network => network.Ssid != savedSsid))
                found.Insert(0, new WifiNetwork(savedSsid, connection?.Int("rssi", -100) ?? -100, true, ""));
            if (found.Count == 0)
            {
                host.Children.Add(SettingsCard("", L("Nicio rețea găsită", "No network found"),
                    L("Verifică dacă routerul e pornit și aproape, apoi scanează din nou.", "Check that the router is on and nearby, then scan again.")));
                return;
            }
            foreach (var network in found) host.Children.Add(NetworkCard(network, !inAccessPoint && network.Ssid == savedSsid));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            host.Children.Clear();
            host.Children.Add(SettingsCard("", L("Scanarea nu a reușit", "The scan failed"), FriendlyError(error)));
        }
        finally
        {
            if (!token.IsCancellationRequested) scan.IsEnabled = true;
        }
    }

    private FrameworkElement NetworkCard(WifiNetwork network, bool connected)
    {
        var security = network.Secured ? network.Auth : L("Deschisă", "Open");
        var description = connected ? L("Conectat · ", "Connected · ") + security : security;
        var lockIcon = network.Secured ? new FontIcon { Glyph = "", FontSize = 14, Opacity = 0.72 } : null;
        if (connected)
            return NavCard(SignalGlyph(network.Rssi), network.Ssid, description,
                () => _ = ShowWifiInfoDialogAsync(network.Ssid));

        var card = NavCard(SignalGlyph(network.Rssi), network.Ssid, description, () => _ = ConnectToNetworkAsync(network.Ssid, network.Secured));
        card.Content = HeaderGrid(SignalGlyph(network.Rssi), network.Ssid, description, lockIcon);
        AutomationProperties.SetName(card, L("Conectează la ", "Connect to ") + network.Ssid);
        return card;
    }

    private async Task ShowWifiInfoDialogAsync(string ssid)
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null || _tileDialog is not null) return;
        using var cancellation = new CancellationTokenSource();
        var host = new StackPanel { Children = { new ProgressRing { IsActive = true, Width = 28, Height = 28 } } };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = DialogHeading.Create("\uE701", ssid),
            Content = new ScrollViewer { Width = Math.Clamp(root.Size.Width - 120, 280, 440), Height = 340, Content = host },
            CloseButtonText = L("Închideți", "Close"),
            RequestedTheme = ThemeSource.ActualTheme,
        };
        _tileDialog = dialog;
        dialog.Opened += async (_, _) => await ShowWifiConnectionAsync(host, false, "", cancellation.Token);
        try { await dialog.ShowAsync(); }
        catch (System.Runtime.InteropServices.COMException) { }
        finally { cancellation.Cancel(); _tileDialog = null; }
    }

    // A null name asks for one, for a network that doesn't show its name.
    private async Task ConnectToNetworkAsync(string? ssid, bool secured)
    {
        if (XamlRoot is null) return;
        var name = new TextBox { Header = L("Nume rețea (SSID)", "Network name (SSID)"), MaxLength = 32 };
        var password = new PasswordBox { Header = L("Parola WiFi", "WiFi Password"), PlaceholderText = L("Introduceți parola", "Enter password"), PasswordRevealMode = PasswordRevealMode.Peek };
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(new TextBlock
        {
            Text = L("Ceasul se deconectează de la rețeaua actuală și se conectează la aceasta. Legătura cu aplicația se poate întrerupe, iar adresa IP a ceasului se poate schimba.",
                     "The clock leaves its current network and joins this one. The connection to the app may drop, and the clock's IP address may change."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        if (ssid is null)
        {
            content.Children.Add(name);
            password.PlaceholderText = L("Lăsați gol pentru o rețea deschisă", "Leave blank for an open network");
        }
        if (secured) content.Children.Add(password);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            // Named after the network, as the web interface's dialog is.
            Title = DialogHeading.Create("\uE701", ssid ?? L("Rețea ascunsă", "Hidden Network")),
            Content = content,
            PrimaryButtonText = L("Conectați", "Connect"),
            CloseButtonText = L("Anulați", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
        };
        void Validate() => dialog.IsPrimaryButtonEnabled =
            (ssid is not null || name.Text.Trim().Length > 0) && (ssid is null || !secured || password.Password.Length > 0);
        name.TextChanged += (_, _) => Validate();
        password.PasswordChanged += (_, _) => Validate();
        Validate();

        ContentDialogResult answer;
        try
        {
            answer = await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }
        if (answer != ContentDialogResult.Primary) return;

        var target = ssid ?? name.Text.Trim();
        try
        {
            await Post(CancellationToken.None, "/connect", ("ssid", target), ("pass", password.Password));
            ShowSuccess(L($"Ceasul se conectează la „{target}”. Dacă adresa lui IP se schimbă, aplicația îl găsește din nou la următoarea scanare.",
                          $"The clock is joining \"{target}\". If its IP address changes, the app finds it again on the next scan."));
        }
        catch (Exception error)
        {
            ShowError(FriendlyError(error));
        }
    }

    private static string SignalGlyph(int rssi) => rssi switch
    {
        >= -55 => "",
        >= -67 => "",
        >= -75 => "",
        _ => "",
    };

    private static string SignalQuality(int rssi) => rssi switch
    {
        >= -55 => L("excelent", "excellent"),
        >= -67 => L("bun", "good"),
        >= -75 => L("acceptabil", "fair"),
        _ => L("slab", "weak"),
    };

    // A section title with a button at its end, such as Scan again.
    private Grid SectionWithAction(string title, Button action)
    {
        var grid = new Grid { Margin = new Thickness(2, BuildTarget.Children.Count == 0 ? 4 : 28, 0, 6), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = title,
            Style = Resource<Style>("BodyStrongTextBlockStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        return grid;
    }

    // ------------------------------------------------------------------ account

    private void BuildAccount()
    {
        var config = ConfigStore.Load();

        // The web interface asks for the current password inside each change; here
        // it is typed once, above both.
        AddSection(L("Cont", "Account"));
        var (currentView, currentPassword) = SecretField(config.ScPass, L("Parola actuală", "Current password"));
        Add(StackedCard("\uE72E", L("Parola actuală", "Current password"),
            L("Cea cu care intrați acum în interfață. E cerută pentru orice schimbare.",
              "The one you sign in with now. It is needed for any change."), currentView).Card);

        var savedUser = config.ScUser;
        var user = new TextBox { Text = savedUser, MaxLength = 32 };
        var userField = SaveFieldCard("\uE77B", L("Schimbați utilizatorul", "Change Username"),
            savedUser.Length > 0 ? savedUser : "-",
            user,
            () => user.Text.Trim() is { Length: > 0 } name && name != savedUser,
            () => UpdateAccountAsync(user.Text.Trim(), currentPassword.Password, ""));
        user.TextChanged += (_, _) => userField.Refresh();
        OnEnter(user, userField.Submit);
        Add(userField.Card);

        var (newView, newPassword) = SecretField("", L("Parolă nouă", "New password"));
        var passwordField = SaveFieldCard("\uE8D7", L("Schimbați parola", "Change Password"),
            L("Parola cu care intrați în interfață", "The password you sign in with"),
            newView,
            () => newPassword.Password.Length > 0,
            async () =>
            {
                if (newPassword.Password.Length < 4)
                {
                    ShowError(L("Parola nouă trebuie să aibă cel puțin 4 caractere.", "The new password needs at least 4 characters."));
                    return;
                }
                await UpdateAccountAsync(savedUser, currentPassword.Password, newPassword.Password);
            });
        newPassword.PasswordChanged += (_, _) => passwordField.Refresh();
        OnEnter(newPassword, passwordField.Submit);
        Add(passwordField.Card);
    }

    // Changes the account on the clock, then in the app, which signs in with what it saved.
    private async Task UpdateAccountAsync(string user, string currentPassword, string newPassword)
    {
        Surface.Status.IsOpen = false;
        try
        {
            await Post(CancellationToken.None, "/updateaccount",
                ("user", user), ("curpass", currentPassword), ("newpass", newPassword));
            // Changing the password invalidates the firmware's stateless
            // cookies immediately. Stop the sender before it retries with the
            // old password and consumes the clock's limited login attempts.
            DeskClockSession.Stop();
            var nextPassword = newPassword.Length > 0 ? newPassword : currentPassword;
            AppConfig saved;
            try
            {
                saved = ConfigStore.Update(config =>
                {
                    config.ScUser = user;
                    config.ScPass = nextPassword;
                });
            }
            catch
            {
                // The clock has already changed its account. Keep this run
                // usable even if saving the new credentials to disk fails.
                saved = ConfigStore.Load();
                saved.ScUser = user;
                saved.ScPass = nextPassword;
                ClockSession.ClearRefusal();
                ClockDeviceApi.ForgetSession();
                DeskClockSession.Start(saved);
                throw;
            }
            ClockSession.ClearRefusal();
            ClockDeviceApi.ForgetSession();
            DeskClockSession.Start(saved);

            var surface = Surface;
            BuildSection(surface, null);
            ShowStatus(surface.Status, InfoBarSeverity.Success, L("Gata", "Done"),
                L("Contul a fost actualizat pe ceas și în aplicație.", "The account is updated on the clock and in the app."));
        }
        catch (Exception error)
        {
            ShowError(FriendlyError(error));
        }
    }

    // ------------------------------------------------------------------ sign-in details

    // What the app itself needs to reach the clock and sign in to it, apart from
    // the account kept on the clock: one Save at the bottom writes all four and
    // connects with them, rather than each field saving on its own.
    private void BuildCredentials()
    {
        var config = ConfigStore.Load();
        var savedAddress = config.Esp32Ip.Length > 0 ? config.Esp32Ip : _device?.Address ?? "";
        var savedPort = config.Esp32Port is > 0 and <= 65535 ? config.Esp32Port : 80;
        var address = new TextBox { Text = savedAddress, Width = 220, PlaceholderText = "192.168.1.100" };
        var port = new TextBox { Text = Str(savedPort), Width = 100 };
        var user = new TextBox { Text = config.ScUser, Width = 220 };
        var (passwordView, password) = SecretField(config.ScPass, AppStrings.Get("connection.password"));
        passwordView.Width = 260;

        AddSection(AppStrings.Get("clock.addressSection"));
        Add(SettingsCard("\uE968", AppStrings.Get("connection.ip"), AppStrings.Get("clock.ipDescription"), address));
        Add(SettingsCard("\uE71B", AppStrings.Get("connection.port"), AppStrings.Get("clock.portDescription"), port));

        AddSection(AppStrings.Get("clock.accountSection"));
        Add(SettingsCard("\uE77B", AppStrings.Get("connection.user"), AppStrings.Get("clock.userDescription"), user));
        Add(SettingsCard("\uE72E", AppStrings.Get("connection.password"), AppStrings.Get("clock.passwordDescription"), passwordView));

        var save = ActionButton(AppStrings.Get("clock.credentialsSave"), null, accent: true);
        save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Margin = new Thickness(0, 20, 0, 0);
        void Submit() => _ = SaveCredentialsAsync(save, address, port, user, password);
        save.Click += (_, _) => Submit();
        // Enter in any of the four does what the button does.
        foreach (var box in new UIElement[] { address, port, user, passwordView }) OnEnter(box, Submit);
        Add(save);
    }

    private async Task SaveCredentialsAsync(Button save, TextBox address, TextBox port, TextBox user, PasswordBox password)
    {
        Surface.Status.IsOpen = false;
        if (!DeskClockDiscoveryService.TryNormalizeConfiguredAddress(
                address.Text.Trim(),
                int.TryParse(port.Text.Trim(), out var parsed) ? parsed : 80,
                out var endpoint))
        {
            ShowError(AppStrings.Get("clock.invalidAddressMessage"));
            return;
        }
        var name = user.Text.Trim();
        var secret = password.Password;
        if (name.Length == 0 || secret.Length == 0)
        {
            ShowError(AppStrings.Get("clock.signInEmpty"));
            return;
        }

        save.IsEnabled = false;
        try
        {
            // The clock has to accept them before the app starts using them.
            if (!await App.DeviceHub.PairClockAsync(endpoint.Address, endpoint.Port, name, secret, CancellationToken.None))
            {
                ShowError(AppStrings.Get("clock.signInWrong"));
                return;
            }
            ClosePanel();
        }
        catch (Exception error)
        {
            ShowError(FriendlyError(error));
        }
        finally
        {
            save.IsEnabled = true;
        }
    }
}
