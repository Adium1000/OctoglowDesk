using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using OctoglowSender.Devices.Macro.Models;

namespace OctoglowSender.Devices.Macro.Services;

// CDC is the configuration/event channel; keyboard, media and mouse actions
// remain native HID and keep working when Connect is not running.
internal sealed partial class MacroSerialService : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint SetDtr = 5;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenComPort(string name, uint access, uint share,
        nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EscapeCommFunction(SafeFileHandle handle, uint function);

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle handle, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle handle, [Out] byte[] buffer,
        uint count, out uint read, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle handle, [In] byte[] buffer,
        uint count, out uint written, nint overlapped);

    private sealed class SerialWriteRequest(byte[] bytes)
    {
        internal byte[] Bytes { get; } = bytes;
        internal TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private volatile bool _restoring;
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _session;
    private SafeFileHandle? _handle;
    private Channel<string>? _replies;
    private Channel<SerialWriteRequest>? _writes;
    private Task? _worker;
    private string _currentPort = "";
    private volatile bool _connected;
    private string _firmwareVersion = "";
    private int _protocolLevel;

    public bool IsConnected => _connected;
    public string PortName => Volatile.Read(ref _currentPort);
    public string FirmwareVersion => Volatile.Read(ref _firmwareVersion);
    public bool SupportsV5 => Volatile.Read(ref _protocolLevel) >= 5;
    public bool SupportsV6 => Volatile.Read(ref _protocolLevel) >= 6;
    public bool SupportsV7 => Volatile.Read(ref _protocolLevel) >= 7;
    public bool SupportsV8 => Volatile.Read(ref _protocolLevel) >= 8;
    public bool SupportsV9 => Volatile.Read(ref _protocolLevel) >= 9;
    public bool SupportsV10 => Volatile.Read(ref _protocolLevel) >= 10;
    public bool SupportsV11 => Volatile.Read(ref _protocolLevel) >= 11;
    public bool SupportsBuzzerVolume => SupportsV5;
    public string LastError { get; private set; } = "";
    public event Action<bool>? ConnectionChanged;
    public event Action? StatusChanged;
    public event Action<string>? PhysicalInput;
    public event Action<int>? ActiveTabChanged;

    public static IReadOnlyList<string> AvailablePorts()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is null) return [];
            // Windows often lists Bluetooth virtual ports beside actual USB CDC.
            // Keep all available ports, but put USB serial ports first.
            return key.GetValueNames()
                .Select(name => (Device: name, Port: key.GetValue(name) as string))
                .Where(entry => entry.Port is not null && IsPortName(entry.Port))
                .GroupBy(entry => entry.Port!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(entry =>
                    entry.Device.Contains("USBSER", StringComparison.OrdinalIgnoreCase)).First())
                .OrderByDescending(entry => entry.Device.Contains("USBSER", StringComparison.OrdinalIgnoreCase))
                .ThenBy(entry => entry.Port, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Port!)
                .ToArray();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Write($"[MACRO SERIAL] Could not list COM ports: {error.Message}");
            return [];
        }
    }

    private static bool IsPortName(string name) =>
        name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(name.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
        number is > 0 and <= 256;

    public void Start() => _worker ??= Task.Run(RunAsync);

    private void SetError(string message)
    {
        if (LastError == message) return;
        LastError = message;
        StatusChanged?.Invoke();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var ports = MacroPortDiscovery.FindPorts(out var macroPresent);
                if (ports.Count == 0)
                {
                    SetError(macroPresent
                        ? "The Macro was found, but its USB configuration interface is unavailable. Check the USB connection and flash V4 or newer firmware."
                        : "");
                }
                else
                {
                    string? failure = null;
                    foreach (var port in ports)
                    {
                        Volatile.Write(ref _currentPort, port);
                        try
                        {
                            // A successful session runs until unplug/restart, then we
                            // rediscover its new port instead of trusting a saved COM number.
                            await RunSessionAsync(port, _stop.Token);
                            failure = null;
                            break;
                        }
                        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                        catch (Exception error)
                        {
                            failure = error.Message;
                            if (LastError != failure) AppLog.Write($"[MACRO SERIAL] {port}: {failure}");
                        }
                        finally
                        {
                            SetConnected(false);
                            Volatile.Write(ref _currentPort, "");
                        }
                    }
                    if (failure is not null) SetError(failure);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                if (LastError != error.Message) AppLog.Write($"[MACRO SERIAL] Discovery: {error.Message}");
                SetError(error.Message);
            }
            SetConnected(false);
            try { await Task.Delay(1500, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunSessionAsync(string port, CancellationToken stop)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _session = session;
        var handle = OpenComPort(@"\\.\" + port, GenericRead | GenericWrite, 0,
            nint.Zero, OpenExisting, 0, nint.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == 5)
                throw new IOException("The Macro USB configuration port is in use. Close a serial monitor or another app using it.");
            throw new Win32Exception(error, $"Could not open {port}.");
        }
        try
        {
            if (!EscapeCommFunction(handle, SetDtr))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enable USB CDC data.");
            var timeouts = new CommTimeouts
            {
                // Return one byte promptly, or let the cancellation loop wake
                // every 50 ms. A zero-byte timeout is not a disconnected port.
                ReadIntervalTimeout = uint.MaxValue,
                ReadTotalTimeoutMultiplier = 0,
                ReadTotalTimeoutConstant = 50,
                WriteTotalTimeoutMultiplier = 0,
                WriteTotalTimeoutConstant = 2000,
            };
            if (!SetCommTimeouts(handle, ref timeouts))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure USB CDC timeouts.");
            _handle = handle;
            _replies = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            _writes = Channel.CreateUnbounded<SerialWriteRequest>(new UnboundedChannelOptions { SingleReader = true });
            var readTask = Task.Run(() => ReadLoop(handle, _writes, _replies.Writer, session.Token), session.Token);
            try
            {
                // Opening CDC can reset some RP2040 builds. Allow enumeration to settle.
                await Task.Delay(750, session.Token);
                var hello = await CommandAsync("HELLO", session.Token);
                if (hello.Count != 1 ||
                    (hello[0] != "MACROPAD RP2040 V4 READY" &&
                     hello[0] != "MACROPAD RP2040 V5 READY" &&
                     hello[0] != "MACROPAD RP2040 V6 READY" &&
                     hello[0] != "OCTOGLOW MACRO 0.2 READY"))
                    throw new InvalidOperationException("The Macro needs compatible Octoglow firmware.");
                Volatile.Write(ref _firmwareVersion,
                    hello[0] == "OCTOGLOW MACRO 0.2 READY" ? "0.2" :
                    hello[0].Contains(" V6 ", StringComparison.Ordinal) ? "V6" :
                    hello[0].Contains(" V5 ", StringComparison.Ordinal) ? "V5" : "V4");
                Volatile.Write(ref _protocolLevel,
                    hello[0] == "OCTOGLOW MACRO 0.2 READY" ? 7 :
                    hello[0].Contains(" V6 ", StringComparison.Ordinal) ? 6 :
                    hello[0].Contains(" V5 ", StringComparison.Ordinal) ? 5 : 4);
                SetError("");
                SetConnected(true);
                await readTask;
            }
            finally
            {
                SetConnected(false);
                Volatile.Write(ref _firmwareVersion, "");
                Volatile.Write(ref _protocolLevel, 0);
                session.Cancel();
                try { await readTask; } catch (Exception) { }
                _replies = null;
                _writes = null;
                _handle = null;
            }
        }
        finally
        {
            handle.Dispose();
            if (ReferenceEquals(_session, session)) _session = null;
        }
    }

    private void ReadLoop(SafeFileHandle handle, Channel<SerialWriteRequest> writes,
        ChannelWriter<string> replies, CancellationToken token)
    {
        var single = new byte[1];
        var lineBuffer = new StringBuilder(128);
        try
        {
            while (!token.IsCancellationRequested)
            {
                // A synchronous serial handle is owned by this loop. A pending
                // ReadFile must not compete with a WriteFile on another thread.
                while (writes.Reader.TryRead(out var request))
                {
                    if (!WriteFile(handle, request.Bytes, (uint)request.Bytes.Length,
                            out var written, nint.Zero))
                    {
                        var code = Marshal.GetLastWin32Error();
                        var hint = code == 121
                            ? " The device is not accepting USB serial data. Reconnect it; if this repeats, check the Macro firmware."
                            : "";
                        var error = new IOException($"Macro USB write failed (Windows error {code}: " +
                            $"{new Win32Exception(code).Message}).{hint}");
                        request.Completion.TrySetException(error);
                        throw error;
                    }
                    if (written != request.Bytes.Length)
                    {
                        var error = new IOException($"Macro USB wrote only {written} of " +
                            $"{request.Bytes.Length} bytes. Check the USB connection.");
                        request.Completion.TrySetException(error);
                        throw error;
                    }
                    request.Completion.TrySetResult(true);
                }
                if (!ReadFile(handle, single, 1, out var count, nint.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The Macro serial port disconnected.");
                if (count == 0) continue;
                var character = (char)single[0];
                if (character == '\r') continue;
                if (character != '\n')
                {
                    if (lineBuffer.Length < 512) lineBuffer.Append(character);
                    else lineBuffer.Clear();
                    continue;
                }
                var line = lineBuffer.ToString();
                lineBuffer.Clear();
                if (line.StartsWith("EVENT ", StringComparison.Ordinal)) HandleEvent(line);
                else if (line.StartsWith("TAB_ACTIVE ", StringComparison.Ordinal))
                {
                    if (int.TryParse(line.AsSpan(11), out var tab)) ActiveTabChanged?.Invoke(tab);
                }
                else replies.TryWrite(line);
            }
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                SetError(error.Message);
                AppLog.Write($"[MACRO SERIAL] {error.Message}");
            }
        }
        finally
        {
            writes.Writer.TryComplete();
            while (writes.Reader.TryRead(out var pending))
                pending.Completion.TrySetException(new IOException("The Macro USB session ended."));
            replies.TryComplete();
        }
    }

    private void HandleEvent(string line)
    {
        if (_restoring) return;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !MacroConfiguration.Slots.Contains(parts[2])) return;
        PhysicalInput?.Invoke(parts[2]);
        if (parts.Length == 5 && parts[3] == "APP" && int.TryParse(parts[4], out var id))
        {
            var app = MacroHostStore.Find(id);
            if (app?.IsClock == true) { _ = MacroClockControl.ExecuteAsync(app); return; }
            if (app is null || !File.Exists(app.Path)) return;
            try { Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true }); }
            catch (Exception error) { AppLog.Write($"[MACRO APP] {app.Name}: {error.Message}"); }
        }
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        ConnectionChanged?.Invoke(value);
    }

    private async Task<IReadOnlyList<string>> CommandAsync(string command, CancellationToken token)
    {
        if (_restoring) throw new InvalidOperationException(AppStrings.IsEnglish ? "A backup is being restored." : "Se restaurează o copie de siguranță.");
        await _commandGate.WaitAsync(token);
        try
        {
            if (_restoring) throw new InvalidOperationException("A backup is being restored.");
            return await CommandCoreAsync(command, token);
        }
        finally { _commandGate.Release(); }
    }

    // Caller owns _commandGate for the entire transaction, not just one command.
    private async Task<IReadOnlyList<string>> CommandCoreAsync(string command, CancellationToken token)
    {
        try
        {
            var replies = _replies?.Reader ?? throw new IOException("The Macro configuration channel is disconnected.");
            var writes = _writes?.Writer ?? throw new IOException("The Macro configuration channel is disconnected.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(command == "DUMP" ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(4));
            var request = new SerialWriteRequest(Encoding.ASCII.GetBytes(command + "\n"));
            if (!writes.TryWrite(request)) throw new IOException("The Macro USB session ended.");
            await request.Completion.Task.WaitAsync(timeout.Token);
            var lines = new List<string>();
            while (true)
            {
                var line = await replies.ReadAsync(timeout.Token);
                if (line.StartsWith("ERR", StringComparison.Ordinal)) throw new InvalidOperationException(line);
                lines.Add(line);
                if ((command == "HELLO" && (line.StartsWith("MACROPAD RP2040", StringComparison.Ordinal) ||
                    line.StartsWith("OCTOGLOW MACRO", StringComparison.Ordinal))) ||
                    (command == "DUMP" && line == "END") ||
                    (command != "HELLO" && command != "DUMP" &&
                        (line == "OK" || line.StartsWith("OK ", StringComparison.Ordinal))))
                    return lines;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            _session?.Cancel();
            throw new TimeoutException("The Macro did not respond to the configuration command.");
        }
    }

    public async Task<MacroConfiguration> ReadConfigurationAsync(CancellationToken token = default)
    {
        if (!IsConnected) throw new IOException("The Macro USB configuration channel is not connected yet.");
        var lines = await CommandAsync("DUMP", token);
        var config = MacroConfigurationParser.Parse(lines);
        Volatile.Write(ref _protocolLevel, int.Parse(lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture));
        return config;
    }

    public async Task SaveMappingAsync(int tab, string slot, MacroAction action)
    {
        if (tab is < 1 or > MacroConfiguration.MaxTabs || !MacroConfiguration.Slots.Contains(slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        await CommandAsync($"SETMAP {tab} {slot} {(int)action.Type} {action.Modifiers} {action.Key1} {action.Key2} {action.Value}", _stop.Token);
        await CommandAsync("SAVE", _stop.Token);
    }

    public async Task SaveTabCountAsync(int count)
    {
        if (count is < 1 or > MacroConfiguration.MaxTabs) throw new ArgumentOutOfRangeException(nameof(count));
        await CommandAsync($"SETTABCOUNT {count}", _stop.Token);
        await CommandAsync("SAVE", _stop.Token);
    }

    public async Task DeleteTabAsync(int tab)
    {
        if (!SupportsV8) throw new NotSupportedException("Update the Macro firmware for deleting any tab.");
        if (tab is < 1 or > MacroConfiguration.MaxTabs) throw new ArgumentOutOfRangeException(nameof(tab));
        await CommandAsync($"DELTAB {tab}", _stop.Token);
    }

    public async Task SaveTabIdentityAsync(int tab, string name, int icon)
    {
        if (!SupportsV8) throw new NotSupportedException("Update the Macro firmware for tab names and icons.");
        if (tab is < 1 or > MacroConfiguration.MaxTabs || icon is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(tab));
        var bytes = new UTF8Encoding(false, true).GetBytes(name.Trim());
        if (bytes.Length > 24 || bytes.Contains((byte)0))
            throw new ArgumentException("Tab names must be at most 24 UTF-8 bytes.", nameof(name));
        await CommandAsync($"TABMETA {tab} {icon} {(bytes.Length == 0 ? "-" : Convert.ToHexString(bytes))}", _stop.Token);
    }

    public async Task MoveTabAsync(int from, int to)
    {
        if (!SupportsV9) throw new NotSupportedException("Update the Macro firmware to reorder tabs.");
        if (from is < 1 or > MacroConfiguration.MaxTabs || to is < 1 or > MacroConfiguration.MaxTabs)
            throw new ArgumentOutOfRangeException(nameof(from));
        await CommandAsync($"MOVETAB {from} {to}", _stop.Token);
    }

    public async Task AddTabWithSwitchAsync(string slot)
    {
        if (!MacroConfiguration.Slots.Take(8).Contains(slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (!SupportsV5)
            throw new NotSupportedException("The Macro needs V5 firmware to add a tab atomically.");
        await CommandAsync($"ADDTAB {slot}", _stop.Token);
    }

    public async Task SetActiveTabAsync(int tab)
    {
        await CommandAsync($"ACTIVE {tab}", _stop.Token);
        ActiveTabChanged?.Invoke(tab);
    }

    public async Task SaveLedAsync(int effect)
    {
        if (effect is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(effect));
        await CommandAsync($"LED {effect}", _stop.Token);
    }

    public async Task SaveLedOutputModeAsync(MacroLedOutputMode mode)
    {
        if (!SupportsV6) throw new NotSupportedException("Update the Macro to V6 firmware to choose the LED output mode.");
        if (mode is not (MacroLedOutputMode.Combined or MacroLedOutputMode.AddressableOnly or MacroLedOutputMode.ExternalOnly) ||
            (mode == MacroLedOutputMode.ExternalOnly && !SupportsV7))
            throw new ArgumentOutOfRangeException(nameof(mode));
        await CommandAsync($"LEDMODE {(int)mode}", _stop.Token);
    }

    public async Task SaveTabColorAsync(int tab, MacroRgbColor color)
    {
        if (!SupportsV6) throw new NotSupportedException("Update the Macro to V6 firmware to set tab colors.");
        if (tab is < 1 or > MacroConfiguration.MaxTabs) throw new ArgumentOutOfRangeException(nameof(tab));
        await CommandAsync($"TABRGB {tab} {color.Red} {color.Green} {color.Blue}", _stop.Token);
    }

    public async Task SaveBuzzerAsync(bool enabled) =>
        await CommandAsync(enabled ? "BUZZER ON" : "BUZZER OFF", _stop.Token);

    public async Task SaveBuzzerVolumeAsync(int volume)
    {
        if (volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        if (!SupportsBuzzerVolume) throw new NotSupportedException("Update the Macro to V5 firmware to control buzzer volume.");
        await CommandAsync($"BUZZERVOL {volume}", _stop.Token);
    }

    public async Task SaveLedBrightnessAsync(int brightness)
    {
        if (!SupportsV8) throw new NotSupportedException("Update the Macro firmware for LED brightness.");
        if (brightness is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(brightness));
        await CommandAsync($"BRIGHT {brightness}", _stop.Token);
    }

    public async Task SaveOutputBrightnessAsync(bool addressable, int brightness)
    {
        if (!SupportsV11) throw new NotSupportedException("Update Macro firmware for separate lighting outputs.");
        if (brightness is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(brightness));
        await CommandAsync($"{(addressable ? "RGBBRIGHT" : "EXTBRIGHT")} {brightness}", _stop.Token);
    }

    public async Task SaveOutputEffectAsync(bool addressable, int tab, int effect)
    {
        if (!SupportsV11) throw new NotSupportedException("Update Macro firmware for separate lighting outputs.");
        if (tab is < 0 or > MacroConfiguration.MaxTabs || effect is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(tab));
        var command = addressable ? "RGBFX" : "EXTFX";
        await CommandAsync(tab == 0 ? $"{command}ALL {effect}" : $"{command} {tab} {effect}", _stop.Token);
    }

    public async Task SaveLightingEnabledAsync(bool enabled)
    {
        if (!SupportsV7) throw new NotSupportedException("Macro firmware 0.2 is required for lighting power.");
        await CommandAsync(enabled ? "LIGHT ON" : "LIGHT OFF", _stop.Token);
    }

    public async Task SaveTabEffectAsync(int tab, int effect)
    {
        if (!SupportsV7) throw new NotSupportedException("Macro firmware 0.2 is required for per-tab effects.");
        if (tab is < 1 or > MacroConfiguration.MaxTabs || effect is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(tab));
        await CommandAsync($"TABFX {tab} {effect}", _stop.Token);
    }

    public async Task SaveAllTabEffectsAsync(int effect)
    {
        if (!SupportsV7) throw new NotSupportedException("Macro firmware 0.2 is required for per-tab effects.");
        if (effect is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(effect));
        await CommandAsync($"TABFXALL {effect}", _stop.Token);
    }

    public async Task SaveTonePackAsync(int pack)
    {
        if (!SupportsV7) throw new NotSupportedException("Macro firmware 0.2 is required for tone packs.");
        if (pack is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(pack));
        await CommandAsync($"TONEPACK {pack}", _stop.Token);
    }

    public async Task SaveTabTonePackAsync(int tab, int pack)
    {
        if (!SupportsV8) throw new NotSupportedException("Update the Macro firmware for per-tab tones.");
        if (tab is < 1 or > MacroConfiguration.MaxTabs || pack is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(tab));
        await CommandAsync($"TABTONE {tab} {pack}", _stop.Token);
    }

    public async Task DeleteCustomToneAsync(int pack)
    {
        if (pack is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(pack));
        if (!SupportsV7) throw new NotSupportedException("Update the Macro firmware for custom tones.");
        if (SupportsV10)
        {
            try { await CommandAsync($"DELPACK {pack}", _stop.Token); return; }
            catch (InvalidOperationException error) when (error.Message == "ERR comanda necunoscuta (HELP pentru lista)")
            { /* Early 0.2 builds do not yet implement atomic preset deletion. */ }
        }
        var state = await ReadConfigurationAsync(_stop.Token);
        // On old firmware, clear assignments before sounds, so keys never refer
        // to a partly cleared preset. Restore unrelated per-tab themes if the
        // legacy global theme command temporarily changes them.
        if (state.TonePack == pack + 4) await SaveTonePackAsync(0);
        if (SupportsV8)
            for (var tab = 1; tab <= MacroConfiguration.MaxTabs; tab++)
                if (state.TonePack == pack + 4 || state.GetTabTonePack(tab) == pack + 4)
                    await SaveTabTonePackAsync(tab, state.GetTabTonePack(tab) == pack + 4 ? 0 : state.GetTabTonePack(tab));
        await SaveCustomToneGroupAsync(pack, false, 0);
        await SaveCustomToneGroupAsync(pack, true, 0);
        for (var toneEvent = 14; toneEvent < MacroConfiguration.ToneEvents; toneEvent++)
            await SaveCustomToneAsync(pack, toneEvent, 0);
    }

    public async Task SaveCustomToneNameAsync(int pack, string name)
    {
        if (!SupportsV10) throw new NotSupportedException("Update the Macro firmware for named tone presets.");
        var bytes = Encoding.UTF8.GetBytes(name);
        if (pack is < 1 or > 5 || bytes.Length is < 1 or > 24 || name.Any(char.IsControl))
            throw new ArgumentOutOfRangeException(nameof(name));
        await CommandAsync($"PACKNAME {pack} {Convert.ToHexString(bytes)}", _stop.Token);
    }

    public async Task PreviewToneAsync(int frequency)
    {
        if (!SupportsV10) throw new NotSupportedException("Update the Macro firmware for sound previews.");
        if (frequency is < 0 or > 8000) throw new ArgumentOutOfRangeException(nameof(frequency));
        await CommandAsync($"TESTTONE {frequency}", _stop.Token);
    }

    public async Task SaveLightingOutputAsync(int output)
    {
        if (!SupportsV10) throw new NotSupportedException("Update the Macro firmware for combined lighting control.");
        if (output is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(output));
        await CommandAsync($"LIGHTOUTPUT {output}", _stop.Token);
    }

    public async Task SaveCustomToneAsync(int pack, int toneEvent, int frequency)
    {
        if (!SupportsV7) throw new NotSupportedException("Macro firmware 0.2 is required for custom tones.");
        if (pack is < 1 or > MacroConfiguration.CustomTonePacks ||
            toneEvent is < 0 or >= MacroConfiguration.ToneEvents || frequency is < 0 or > 8000)
            throw new ArgumentOutOfRangeException(nameof(pack));
        await CommandAsync($"TONESET {pack} {toneEvent} {frequency}", _stop.Token);
    }

    public async Task SaveCustomToneGroupAsync(int pack, bool hold, int frequency)
    {
        if (!SupportsV7) throw new NotSupportedException("Macro firmware 0.2 is required for custom tones.");
        if (pack is < 1 or > MacroConfiguration.CustomTonePacks || frequency is < 0 or > 8000)
            throw new ArgumentOutOfRangeException(nameof(pack));
        await CommandAsync($"TONEALL {pack} {(hold ? "HOLD" : "PRESS")} {frequency}", _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _session?.Cancel();
        _handle?.Dispose();
        try { _worker?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        _stop.Dispose();
        _commandGate.Dispose();
    }
}

internal static class MacroSerialBus
{
    internal static readonly MacroSerialService Instance = new();
}
