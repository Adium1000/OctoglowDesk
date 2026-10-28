using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace OctoglowSender;

internal enum Nfsu2State { NotRunning, NoAccess, UnsupportedVersion, WaitingForRace, Paused, Driving }

// A reading: the player's car speed in metres per second while Driving, 0 otherwise.
internal readonly record struct Nfsu2Reading(Nfsu2State State, float MetresPerSecond)
{
    // Whole numbers cut toward zero, as the game's own speedometer shows them
    // ("%3d" of the speed times 3.6, or times 2.237 in mph).
    public int Kmh => (int)Math.Floor(MetresPerSecond * 3.6);
    public int Mph => (int)Math.Floor(MetresPerSecond * 2.2369362921);
}

// Need for Speed Underground 2 has no telemetry of its own. The speed is read
// from SPEED2.EXE's memory, from outside and read-only - no plug-in in the game
// folder, no write of any kind - and only for the build it was checked on:
// v1.2 NTSC (the one NFSU2 Extra Options and the widescreen fix require, entry
// point 0x75BCC7), no-DVD copies of it included.
//
//   0x8654A4          int      the game flow state; 6 while a race or free roam runs
//   0x8900AC          Player*  player one, while in the game
//   Player + 0x04     Car*     the player's car
//   Car + 0x070       float[3] its velocity, m/s
//   Car + 0x430       float    its speed, m/s: what the game's speedometer
//                              draws (code at 0x4C9D73), always the length of
//                              the velocity above
//   Car + 0x9AC       float    the car's simulation clock, s: stands still
//                              while the game is paused
//
// Checked against the running game on 2026-09-26: the speed matched the
// distance the car covered over time within the measuring error, equalled the
// velocity's length in every one of 29,126 readings, and the clock stood still
// through every pause. A game whose executable doesn't carry the instructions
// these come from is refused as an unsupported version, not guessed at.
internal sealed class Nfsu2Telemetry : IDisposable
{
    public const string ProcessName = "SPEED2";
    private const uint ExpectedEntryPoint = 0x75BCC7;
    private const uint GameFlowAddress = 0x8654A4;
    private const uint GameFlowInGame = 6;
    private const uint PlayersAddress = 0x8900AC;
    private const int PlayerCarOffset = 0x04;
    private const int VelocityOffset = 0x70;
    private const int SpeedOffset = 0x430;
    private const int ClockOffset = 0x9AC;
    // Faster than any car the game has (a tuned one tops out near 100 m/s).
    private const float MaxMetresPerSecond = 150f;

    // The instructions the addresses above are read from, as SPEED2.EXE holds
    // them (.text is mapped at its file offset + 0x400000). Checked in the file
    // the running game was started from: mods such as the widescreen fix patch
    // code in memory, which says nothing about the build.
    private static readonly (uint Address, byte[] Bytes)[] Signature =
    [
        // The HUD speedometer: mov eax,[ebp+4] ; fld [eax+430h] ; mov al,[83AA1Ah]
        (0x4C9D73, [0x8B, 0x45, 0x04, 0xD9, 0x80, 0x30, 0x04, 0x00, 0x00, 0xA0, 0x1A, 0xAA, 0x83, 0x00]),
        // The car's clock and speed in one object: fstp [esi+9ACh] ; fld [esi+430h]
        (0x5F575D, [0xD9, 0x9E, 0xAC, 0x09, 0x00, 0x00, 0xD9, 0x86, 0x30, 0x04, 0x00, 0x00]),
        // Player one: mov esi,[8900ACh]
        (0x403C4E, [0x8B, 0x35, 0xAC, 0x00, 0x89, 0x00]),
        // The game flow state: mov eax,[8654A4h]
        (0x412953, [0xA1, 0xA4, 0x54, 0x86, 0x00]),
    ];

    // The car's clock not moving for this long: the game is paused (or hung).
    // It steps with every frame, so this only has to outlast a slow frame.
    public static readonly TimeSpan PausedAfter = TimeSpan.FromMilliseconds(500);
    // How often a game that isn't running is looked for.
    public static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(2);

    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadProcessMemory(nint process, nint address, byte* buffer, nint size, out nint read);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    private static extern unsafe bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    private readonly Func<uint?> _findGame;
    private readonly Func<string, bool> _isSupportedBuild;
    private nint _process;
    private bool _unsupported;
    private bool _noAccess;
    private uint _pid;
    private DateTime _nextLook = DateTime.MinValue;
    private uint _car;
    private float _clock = float.NaN;
    private DateTime _clockMovedAt = DateTime.MinValue;

    public Nfsu2Telemetry() : this(FindRunningGame, IsSupportedBuild)
    {
    }

    // For tests: another way to find the game and to judge its executable.
    internal Nfsu2Telemetry(Func<uint?> findGame, Func<string, bool> isSupportedBuild)
    {
        _findGame = findGame;
        _isSupportedBuild = isSupportedBuild;
    }

    public bool IsOpen => _process != 0;

    // Why the data read was refused, in a few fixed words (so the log can say
    // it once): null while it passes.
    public string? Problem { get; private set; }

    public Nfsu2Reading Read(DateTime now)
    {
        if (!IsOpen && !TryOpen(now))
            return new(_unsupported ? Nfsu2State.UnsupportedVersion : _noAccess ? Nfsu2State.NoAccess : Nfsu2State.NotRunning, 0);
        if (WaitForSingleObject(_process, 0) == WaitObject0)
        {
            Close();
            return new(Nfsu2State.NotRunning, 0);
        }

        if (!TryReadUInt(GameFlowAddress, out var flow) || flow != GameFlowInGame ||
            !TryReadUInt(PlayersAddress, out var player) || !Plausible(player) ||
            !TryReadUInt(player + PlayerCarOffset, out var car) || !Plausible(car))
            return Waiting();

        if (car != _car)
        {
            // Another car (a new race): nothing counts until its clock has moved.
            _car = car;
            _clock = float.NaN;
            _clockMovedAt = DateTime.MinValue;
        }
        if (!TryReadFloats(car + VelocityOffset, 3, out var velocity) ||
            !TryReadFloats(car + SpeedOffset, 1, out var speedValue) ||
            !TryReadFloats(car + ClockOffset, 1, out var clockValue))
            return Waiting();
        var speed = speedValue[0];
        var clock = clockValue[0];

        if (!float.IsFinite(clock)) return Invalid("ceasul mașinii nu este un număr");
        if (clock != _clock)
        {
            if (!float.IsNaN(_clock)) _clockMovedAt = now;
            _clock = clock;
        }
        if (_clockMovedAt == DateTime.MinValue || now - _clockMovedAt > PausedAfter)
            return new(Nfsu2State.Paused, 0);

        // The speed is the length of the velocity, to the last bit the float
        // holds: anything else is not the car object this was checked on.
        if (!float.IsFinite(speed) || speed < 0 || speed > MaxMetresPerSecond)
            return Invalid("viteza este în afara limitelor");
        var length = Math.Sqrt((double)velocity[0] * velocity[0] + (double)velocity[1] * velocity[1] + (double)velocity[2] * velocity[2]);
        if (!double.IsFinite(length) || Math.Abs(length - speed) > 0.01 + speed * 0.001)
            return Invalid("viteza nu se potrivește cu vectorul de viteză");

        Problem = null;
        return new(Nfsu2State.Driving, speed);
    }

    private Nfsu2Reading Waiting()
    {
        _car = 0;
        _clock = float.NaN;
        _clockMovedAt = DateTime.MinValue;
        return new(Nfsu2State.WaitingForRace, 0);
    }

    private Nfsu2Reading Invalid(string why)
    {
        Problem = why;
        return new(Nfsu2State.WaitingForRace, 0);
    }

    // A user-mode address a 32-bit game can hand out for an object.
    private static bool Plausible(uint address) => address >= 0x10000 && (address & 3) == 0;

    private bool TryOpen(DateTime now)
    {
        if (now < _nextLook) return false;
        _nextLook = now + LookEvery;
        var pid = _findGame();
        if (pid is null)
        {
            _unsupported = false;
            _noAccess = false;
            _pid = 0;
            return false;
        }
        // A game already found to be another build, or out of reach, stays so
        // until it exits: its file is not read again every two seconds.
        if (pid == _pid && (_unsupported || _noAccess)) return false;
        _pid = pid.Value;
        _unsupported = false;
        _noAccess = false;
        var process = OpenProcess(ProcessVmRead | ProcessQueryLimitedInformation | Synchronize, false, pid.Value);
        if (process == 0)
        {
            // Run as administrator while Connect isn't, most likely.
            _noAccess = Marshal.GetLastWin32Error() == 5;
            return false;
        }
        var path = ImagePath(process);
        if (path is null || !_isSupportedBuild(path))
        {
            _unsupported = path is not null;
            CloseHandle(process);
            return false;
        }
        _process = process;
        _car = 0;
        _clock = float.NaN;
        _clockMovedAt = DateTime.MinValue;
        Problem = null;
        return true;
    }

    // SPEED2.EXE: 32-bit, the v1.2 entry point, and the instructions above in place.
    internal static bool IsSupportedBuild(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[0x400];
            if (file.Read(header, 0, header.Length) != header.Length || header[0] != 'M' || header[1] != 'Z') return false;
            var pe = BitConverter.ToInt32(header, 0x3C);
            if (pe < 0 || pe + 0x40 > header.Length || BitConverter.ToUInt32(header, pe) != 0x00004550) return false;
            if (BitConverter.ToUInt16(header, pe + 4) != 0x014C) return false;
            var imageBase = BitConverter.ToUInt32(header, pe + 24 + 28);
            var entry = BitConverter.ToUInt32(header, pe + 24 + 16);
            if (imageBase != 0x400000 || imageBase + entry != ExpectedEntryPoint) return false;
            foreach (var (address, bytes) in Signature)
            {
                var found = new byte[bytes.Length];
                file.Position = address - 0x400000;
                if (file.Read(found, 0, found.Length) != found.Length || !found.AsSpan().SequenceEqual(bytes)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static unsafe string? ImagePath(nint process)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    private unsafe bool TryReadUInt(uint address, out uint value)
    {
        uint result = 0;
        var ok = ReadProcessMemory(_process, (nint)address, (byte*)&result, sizeof(uint), out var read) && read == sizeof(uint);
        value = result;
        return ok;
    }

    private unsafe bool TryReadFloats(uint address, int count, out float[] values)
    {
        values = new float[count];
        fixed (float* buffer = values)
            return ReadProcessMemory(_process, (nint)address, (byte*)buffer, count * sizeof(float), out var read) &&
                   read == count * sizeof(float);
    }

    private static uint? FindRunningGame()
    {
        try
        {
            var found = Process.GetProcessesByName(ProcessName);
            uint? pid = found.Length > 0 ? (uint)found[0].Id : null;
            foreach (var process in found) process.Dispose();
            return pid;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    public void Close()
    {
        if (_process != 0) CloseHandle(_process);
        _process = 0;
        _car = 0;
        _clock = float.NaN;
        _clockMovedAt = DateTime.MinValue;
    }

    public void Dispose() => Close();
}

// What the Integrations tab and page show.
internal static class Nfsu2Status
{
    private static volatile Nfsu2Snapshot _current = Nfsu2Snapshot.Off;

    public static Nfsu2Snapshot Current => _current;

    public static void Set(Nfsu2Snapshot snapshot) => _current = snapshot;
}

// Off: the clock doesn't want the tile (or has no such tile: ClockUnsupported).
internal sealed record Nfsu2Snapshot(bool Reading, bool ClockUnsupported, Nfsu2Reading Game, string? Problem)
{
    public static readonly Nfsu2Snapshot Off = new(false, false, new(Nfsu2State.NotRunning, 0), null);
}
