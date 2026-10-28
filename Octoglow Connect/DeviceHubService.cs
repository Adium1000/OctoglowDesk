using OctoglowSender.Devices.Macro.Services;

namespace OctoglowSender;

/// <summary>
/// Application-lifetime catalog. Paired clocks and USB Macros belong to the
/// shell; clocks merely discovered on the LAN stay in the add-device picker.
/// </summary>
public sealed class DeviceHubService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private const int FullScanEveryChecks = 5;
    private const int ClockMissesBeforeOffline = 3;
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceDescriptor> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceDescriptor> _nearbyClocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceDescriptor> _unconfiguredClocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceDescriptor> _manualCandidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _clockMissCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeskClockDiscoveryService _clockDiscovery = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _refreshLoop;
    private string? _configuredClockId;
    private bool _started;
    private bool _disposed;
    private bool _macroSeen;
    private string? _lastDiscoveryError;

    public event EventHandler? DevicesChanged;
    public event EventHandler? CandidatesChanged;
    public event EventHandler? ScanStateChanged;

    public bool IsScanning { get; private set; }

    public IReadOnlyList<DeviceDescriptor> Devices
    {
        get
        {
            lock (_gate)
            {
                return _devices.Values
                    .OrderByDescending(device => device.IsOnline)
                    .ThenBy(device => device.Kind)
                    .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(device => device.Address, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    public IReadOnlyList<DeviceDescriptor> NearbyClocks
    {
        get
        {
            lock (_gate)
                return _nearbyClocks.Values.OrderBy(device => device.Address, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public IReadOnlyList<DeviceDescriptor> OtherClocks
    {
        get
        {
            lock (_gate)
                return _unconfiguredClocks.Values.Concat(_manualCandidates.Values
                    .Where(device => !_nearbyClocks.ContainsKey(device.Id) &&
                                     !_unconfiguredClocks.ContainsKey(device.Id) && device.Id != _configuredClockId))
                    .Select(device => device.CanSignIn ? device : device with
                    {
                        Detail = $"{AppStrings.Get("hub.needsSetup")} · {device.Detail}",
                    })
                    .OrderBy(device => device.Address, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        MacropadConnectionState.Initialize();
        MacropadConnectionState.Changed += OnMacroConnectionChanged;
        ConnectionStatus.Changed += OnConnectionStatusChanged;
        UpdateMacro(MacropadConnectionState.CheckNow());
        RememberConfiguredClock(ConfigStore.Load());

        _refreshLoop = RunRefreshLoopAsync(_lifetime.Token);
    }

    public async Task RefreshAsync(bool scanNearby = false, bool force = false)
    {
        if (_disposed) return;
        // A user-requested LAN scan must not silently vanish behind a quick
        // scheduled check of the saved clock.
        bool acquired;
        try
        {
            acquired = scanNearby || force
                ? await _refreshGate.WaitAsync(Timeout.Infinite, _lifetime.Token).ConfigureAwait(false)
                : await _refreshGate.WaitAsync(0, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!acquired) return;
        if (scanNearby || force) SetScanning(true);

        try
        {
            if (force) UpdateMacro(MacropadConnectionState.CheckNow());
            var scannedConfig = ConfigStore.Load();
            RememberConfiguredClock(scannedConfig);
            var endpoints = await _clockDiscovery.DiscoverAsync(scannedConfig, _lifetime.Token, scanNearby).ConfigureAwait(false);
            // LAN scans can outlive a sign-in or a settings change. Never use
            // their old config snapshot to put a former account back in Home.
            var config = ConfigStore.Load();
            if (!SameConfiguredClock(scannedConfig, config))
            {
                RememberConfiguredClock(config);
                return;
            }
            FollowConfiguredClock(config, endpoints);
            config = ConfigStore.Load();
            RememberConfiguredClock(config);
            ApplyClockResults(config, endpoints, scanNearby);
            _lastDiscoveryError = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (_lastDiscoveryError != error.Message)
            {
                _lastDiscoveryError = error.Message;
                AppLog.Write($"[DISCOVERY WARN] Scanarea dispozitivelor a eșuat: {error.Message}");
            }
        }
        finally
        {
            if (scanNearby || force) SetScanning(false);
            _refreshGate.Release();
        }
    }

    private void OnConnectionStatusChanged(object? sender, EventArgs e)
    {
        // Old configurations without a firmware id only learn it after their
        // stored credentials have actually worked against this clock.
        if (ConnectionStatus.IsConnected) _ = RefreshAsync();
    }

    public bool RememberManualCandidate(string address, int port)
    {
        var descriptor = DeviceDescriptor.DeskClock(address, port, false, $"{address}:{port}");
        var changed = false;
        lock (_gate)
        {
            if (descriptor.Id != _configuredClockId && !_manualCandidates.ContainsKey(descriptor.Id))
            {
                _manualCandidates[descriptor.Id] = descriptor;
                changed = true;
            }
        }
        if (changed) CandidatesChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    public void ForgetManualCandidate(string id)
    {
        bool removed;
        lock (_gate) removed = _manualCandidates.Remove(id);
        if (removed) CandidatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RememberConfiguredClock(AppConfig config, bool authenticatedNow = false)
    {
        DeskClockDiscoveryService.DeskClockCandidate configured = default;
        var paired = HasPairedClock(config) && DeskClockDiscoveryService.TryNormalizeConfiguredAddress(
            config.Esp32Ip, config.Esp32Port, out configured);
        var id = paired ? $"clock:{configured.Address.ToLowerInvariant()}:{configured.Port}" : null;
        var devicesChanged = false;
        var candidatesChanged = false;
        lock (_gate)
        {
            // A different account/clock replaces the previous paired clock.
            // Keeping an online old entry here exposed another user's clock.
            foreach (var old in _devices.Keys.Where(key => key.StartsWith("clock:", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(key, id, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _devices.Remove(old);
                _clockMissCounts.Remove(old);
                devicesChanged = true;
            }

            _configuredClockId = id;
            if (id is not null)
            {
                if (!_devices.TryGetValue(id, out var existing))
                {
                    if (authenticatedNow || !config.HideDisconnectedWifiDevices)
                    {
                        _devices[id] = DeviceDescriptor.DeskClock(
                            configured.Address,
                            configured.Port,
                            isOnline: authenticatedNow,
                            detail: authenticatedNow ? $"Connected · {configured.Address}:{configured.Port}"
                                                     : $"Salvat · {configured.Address}:{configured.Port}");
                        devicesChanged = true;
                    }
                }
                else if (!existing.IsOnline && config.HideDisconnectedWifiDevices && !authenticatedNow)
                {
                    _devices.Remove(id);
                    devicesChanged = true;
                }
                else if (!existing.IsOnline || authenticatedNow)
                {
                    var updated = existing with
                    {
                        IsOnline = authenticatedNow || existing.IsOnline,
                        Detail = authenticatedNow ? $"Connected · {configured.Address}:{configured.Port}"
                            : $"Salvat · {configured.Address}:{configured.Port}",
                    };
                    if (updated != existing)
                    {
                        _devices[id] = updated;
                        devicesChanged = true;
                    }
                }
            }
            if (id is not null)
            {
                if (_nearbyClocks.Remove(id)) candidatesChanged = true;
                if (_unconfiguredClocks.Remove(id)) candidatesChanged = true;
                if (_manualCandidates.Remove(id)) candidatesChanged = true;
            }
        }

        if (devicesChanged) DevicesChanged?.Invoke(this, EventArgs.Empty);
        if (candidatesChanged) CandidatesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool HasPairedClock(AppConfig config) =>
        !string.IsNullOrWhiteSpace(config.ScUser) && !string.IsNullOrEmpty(config.ScPass);

    private static bool SameConfiguredClock(AppConfig first, AppConfig second) =>
        string.Equals(first.Esp32Ip, second.Esp32Ip, StringComparison.OrdinalIgnoreCase) &&
        first.Esp32Port == second.Esp32Port &&
        first.Esp32Id == second.Esp32Id &&
        first.ScUser == second.ScUser && first.ScPass == second.ScPass;

    public async Task<bool> PairClockAsync(string address, int port, string user, string password, CancellationToken token)
    {
        if (!DeskClockDiscoveryService.TryNormalizeConfiguredAddress(address, port, out var endpoint))
            throw new InvalidOperationException(AppStrings.Get("clock.invalidAddressMessage"));
        if (!await ClockDeviceApi.TrySignInAsync(endpoint.Address, endpoint.Port, user, password, token))
            return false;

        // A scan can follow the previous clock to a new IP and save that
        // address. Finish that scan before replacing the paired account.
        await _refreshGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var config = ConfigStore.Update(saved =>
            {
                saved.Esp32Ip = endpoint.Address;
                saved.Esp32Port = endpoint.Port;
                saved.Esp32Id = ""; // Never carry the former clock's physical identity to a new account.
                saved.Esp32Host = "";
                saved.ScUser = user;
                saved.ScPass = password;
            });
            ClockDeviceApi.ForgetSession();
            RememberConfiguredClock(config, authenticatedNow: true);
            DeskClockSession.Start(config);
        }
        finally
        {
            _refreshGate.Release();
        }
        _ = RefreshAsync();
        return true;
    }

    public async Task ClearAllDevicesAsync()
    {
        if (_disposed) return;

        await _refreshGate.WaitAsync().ConfigureAwait(false);
        var changed = false;
        var candidatesChanged = false;
        try
        {
            DeskClockSession.Stop();
            var config = ConfigStore.Load();
            var oldKey = ClockSession.Key(config.Esp32Ip, config.Esp32Port, config.ScUser);
            var oldCookie = ClockSession.Current(oldKey);
            ConfigStore.Update(saved =>
            {
                saved.Esp32Ip = "";
                saved.Esp32Port = 80;
                saved.Esp32Id = "";
                saved.Esp32Host = "";
                saved.ScUser = "";
                saved.ScPass = "";
            });
            if (oldCookie is not null) ClockSession.Forget(oldKey, oldCookie);
            ClockSession.ClearRefusal();
            ClockDeviceApi.ForgetSession();
            lock (_gate)
            {
                changed = _devices.Count > 0;
                candidatesChanged = _nearbyClocks.Count > 0 || _unconfiguredClocks.Count > 0 || _manualCandidates.Count > 0;
                _devices.Clear();
                _nearbyClocks.Clear();
                _unconfiguredClocks.Clear();
                _manualCandidates.Clear();
                _clockMissCounts.Clear();
                _configuredClockId = null;
            }
        }
        finally
        {
            _refreshGate.Release();
        }

        if (changed)
        {
            AppLog.Write("[DEVICE] Toate dispozitivele au fost eliminate din hub.");
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
        if (candidatesChanged) CandidatesChanged?.Invoke(this, EventArgs.Empty);
        // A USB Macro is still physically attached after clearing saved clocks.
        UpdateMacro(MacropadConnectionState.CheckNow());
    }

    public void RefreshDeviceVisibility()
    {
        if (_disposed) return;
        RememberConfiguredClock(ConfigStore.Load());
        UpdateMacro(MacropadConnectionState.CheckNow());
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        // Discover on launch, then mostly check just the saved clock. A rarer
        // full scan still finds it if DHCP moved it and mDNS is unavailable.
        await RefreshAsync(scanNearby: true).ConfigureAwait(false);
        using var timer = new PeriodicTimer(RefreshInterval);
        var checks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshAsync(scanNearby: ++checks % FullScanEveryChecks == 0).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyClockResults(AppConfig config, IReadOnlyList<DeskClockEndpoint> endpoints, bool scanNearby)
    {
        var devicesChanged = false;
        var candidatesChanged = false;

        lock (_gate)
        {
            var configuredId = _configuredClockId;
            var expectedId = HasPairedClock(config) &&
                DeskClockDiscoveryService.TryNormalizeConfiguredAddress(config.Esp32Ip, config.Esp32Port, out var normalized)
                    ? $"clock:{normalized.Address.ToLowerInvariant()}:{normalized.Port}" : null;
            if (!string.Equals(configuredId, expectedId, StringComparison.OrdinalIgnoreCase)) return;
            var pairedEndpoint = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, configuredId, StringComparison.OrdinalIgnoreCase) &&
                (config.Esp32Id.Length == 0 ||
                 string.Equals(endpoint.DeviceId, config.Esp32Id, StringComparison.OrdinalIgnoreCase)));
            var differentClockAtSavedAddress = config.Esp32Id.Length > 0 && endpoints.Any(endpoint =>
                string.Equals(endpoint.Id, configuredId, StringComparison.OrdinalIgnoreCase) &&
                endpoint.DeviceId is { Length: > 0 } foundId &&
                !string.Equals(foundId, config.Esp32Id, StringComparison.OrdinalIgnoreCase));
            if (configuredId is not null && _devices.TryGetValue(configuredId, out var previous))
            {
                if (pairedEndpoint is not null)
                {
                    _clockMissCounts[configuredId] = 0;
                }
                else if (previous.IsOnline)
                {
                    var misses = _clockMissCounts.GetValueOrDefault(configuredId) + 1;
                    _clockMissCounts[configuredId] = misses;
                    if (differentClockAtSavedAddress || misses >= ClockMissesBeforeOffline)
                    {
                        if (config.HideDisconnectedWifiDevices)
                            _devices.Remove(configuredId);
                        else _devices[configuredId] = previous with
                        {
                            IsOnline = false,
                            Detail = $"Disconnected · {previous.Address}:{previous.Port}",
                        };
                        devicesChanged = true;
                    }
                }
            }

            if (pairedEndpoint is not null && configuredId is not null)
            {
                // With more than one clock on the network, the name tells them apart.
                var where = pairedEndpoint.HostName is { Length: > 0 } name
                    ? $"{name}.local · {pairedEndpoint.Address}:{pairedEndpoint.Port}"
                    : $"{pairedEndpoint.Address}:{pairedEndpoint.Port}";
                var descriptor = DeviceDescriptor.DeskClock(pairedEndpoint.Address, pairedEndpoint.Port,
                    isOnline: true, $"Connected · {where}");

                if (!_devices.TryGetValue(configuredId, out var current) ||
                    current.IsOnline != descriptor.IsOnline ||
                    current.Detail != descriptor.Detail ||
                    current.Address != descriptor.Address ||
                    current.Port != descriptor.Port)
                {
                    _devices[configuredId] = descriptor;
                    devicesChanged = true;
                }
                else
                {
                    // Keep LastSeen current without causing an unnecessary UI refresh.
                    _devices[configuredId] = descriptor;
                }
            }

            if (scanNearby)
            {
                var unpaired = endpoints.Where(endpoint => !ReferenceEquals(endpoint, pairedEndpoint));
                var discovered = unpaired.Where(endpoint => endpoint.HasAccount)
                    .Select(endpoint => DeviceDescriptor.DeskClock(endpoint.Address, endpoint.Port, true,
                        endpoint.HostName is { Length: > 0 } host
                            ? $"{host}.local · {endpoint.Address}:{endpoint.Port}"
                            : $"{endpoint.Address}:{endpoint.Port}"))
                    .ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
                var unconfigured = unpaired.Where(endpoint => !endpoint.HasAccount)
                    .Select(endpoint => DeviceDescriptor.DeskClock(endpoint.Address, endpoint.Port, true,
                        $"{endpoint.Address}:{endpoint.Port}", canSignIn: false))
                    .ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
                if (!SameCandidates(_nearbyClocks, discovered) ||
                    !SameCandidates(_unconfiguredClocks, unconfigured)) candidatesChanged = true;
                _nearbyClocks.Clear();
                foreach (var (id, device) in discovered) _nearbyClocks[id] = device;
                _unconfiguredClocks.Clear();
                foreach (var (id, device) in unconfigured) _unconfiguredClocks[id] = device;
            }
        }

        if (devicesChanged) DevicesChanged?.Invoke(this, EventArgs.Empty);
        if (candidatesChanged) CandidatesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameCandidates(
        Dictionary<string, DeviceDescriptor> current, Dictionary<string, DeviceDescriptor> next) =>
        current.Count == next.Count && next.All(pair => current.TryGetValue(pair.Key, out var old) &&
            old.Detail == pair.Value.Detail && old.IsOnline == pair.Value.IsOnline);

    // The saved clock is known by its id (the MAC /authstate reports), not only
    // by the address it had: learnt the first time it answers, and when it next
    // answers somewhere else - the router gave it another address - the app
    // follows it there, session and all. A clock that answers at the old address
    // with another id is some other clock, and is left alone.
    private void FollowConfiguredClock(AppConfig config, IReadOnlyList<DeskClockEndpoint> endpoints)
    {
        if (!HasPairedClock(config)) return;
        if (!DeskClockDiscoveryService.TryNormalizeConfiguredAddress(config.Esp32Ip, config.Esp32Port, out var configured)) return;
        var atSaved = endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Address, configured.Address, StringComparison.OrdinalIgnoreCase) && endpoint.Port == configured.Port);

        if (config.Esp32Id.Length == 0)
        {
            if (!ConnectionStatus.IsConnected || !DeskClockSession.Matches(configured.Address, configured.Port)) return;
            if (atSaved?.DeviceId is not { Length: > 0 } firstId) return;
            var learnedHost = atSaved.HostName ?? "";
            ConfigStore.TryUpdate(latest =>
            {
                if (!SameConfiguredClock(latest, config)) return false;
                latest.Esp32Id = firstId;
                latest.Esp32Host = learnedHost;
                return true;
            });
            return;
        }

        var ours = endpoints.FirstOrDefault(endpoint => string.Equals(endpoint.DeviceId, config.Esp32Id, StringComparison.OrdinalIgnoreCase));
        if (ours is null) return;
        var host = ours.HostName ?? "";
        if (ReferenceEquals(ours, atSaved))
        {
            if (host == config.Esp32Host) return;
            ConfigStore.TryUpdate(latest =>
            {
                if (!SameConfiguredClock(latest, config)) return false;
                latest.Esp32Host = host;
                return true;
            });
            return;
        }

        var oldKey = ClockSession.Key(config.Esp32Ip, config.Esp32Port, config.ScUser);
        AppLog.Write($"[DISCOVERY] Ceasul salvat răspunde acum la {ours.Address} (era {config.Esp32Ip}); îl urmez acolo.");
        if (!ConfigStore.TryUpdate(latest =>
            {
                if (!SameConfiguredClock(latest, config)) return false;
                latest.Esp32Ip = ours.Address;
                latest.Esp32Port = ours.Port;
                latest.Esp32Host = host;
                return true;
            })) return;
        config.Esp32Ip = ours.Address;
        config.Esp32Port = ours.Port;
        config.Esp32Host = host;
        ClockSession.Move(oldKey, ClockSession.Key(ours.Address, ours.Port, config.ScUser));
        ClockDeviceApi.ForgetSession();
        RememberConfiguredClock(config);
        if (DeskClockSession.IsRunning) DeskClockSession.Start(config);
    }

    private void OnMacroConnectionChanged(object? sender, bool connected) => UpdateMacro(connected);

    private void UpdateMacro(bool connected)
    {
        var id = "macro:rp2040-macropad";
        var changed = false;

        lock (_gate)
        {
            if (connected)
            {
                _macroSeen = true;
                var descriptor = DeviceDescriptor.Macro(true, "Connected · USB HID");
                if (!_devices.TryGetValue(id, out var previous) || previous != descriptor)
                {
                    _devices[id] = descriptor;
                    changed = true;
                }
            }
            else if (_devices.TryGetValue(id, out var existing) && ConfigStore.HideDisconnectedUsbDevices)
            {
                _devices.Remove(id);
                changed = true;
            }
            else if (_devices.TryGetValue(id, out existing) && existing.IsOnline)
            {
                _devices[id] = existing with { IsOnline = false, Detail = "Disconnected · USB HID" };
                changed = true;
            }
            else if (_macroSeen && !ConfigStore.HideDisconnectedUsbDevices && !_devices.ContainsKey(id))
            {
                _devices[id] = DeviceDescriptor.Macro(false, "Disconnected · USB HID");
                changed = true;
            }
        }

        if (changed)
        {
            AppLog.Write(connected
                ? "[DEVICE] Octoglow Macro detectat prin USB."
                : "[DEVICE] Octoglow Macro a fost deconectat.");
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetScanning(bool scanning)
    {
        if (IsScanning == scanning) return;
        IsScanning = scanning;
        ScanStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MacropadConnectionState.Changed -= OnMacroConnectionChanged;
        ConnectionStatus.Changed -= OnConnectionStatusChanged;
        MacropadConnectionState.Shutdown();
        _lifetime.Cancel();
        _ = DisposeInfrastructureAsync();
    }

    private async Task DisposeInfrastructureAsync()
    {
        try
        {
            if (_refreshLoop is not null) await _refreshLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            AppLog.Write($"[DISCOVERY WARN] Oprirea scanării a eșuat: {error.Message}");
        }
        try
        {
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            _clockDiscovery.Dispose();
            _lifetime.Dispose();
            _refreshGate.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
