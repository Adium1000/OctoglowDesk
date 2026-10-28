using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace OctoglowSender;

internal enum NfsMwState { NotRunning, NoAccess, UnsupportedVersion, WaitingForRace, Paused, Driving }

// A reading: the player's car speed in metres per second while Driving, 0 otherwise.
internal readonly record struct NfsMwReading(NfsMwState State, float MetresPerSecond)
{
    // Whole numbers cut toward zero, as the game's own speedometer shows them
    // (the speed times 3.6, or times 2.23699 in mph, cut by _ftol).
    public int Kmh => (int)Math.Floor(MetresPerSecond * 3.6);
    public int Mph => (int)Math.Floor(MetresPerSecond * 2.2369362921);
}

// Need for Speed Most Wanted (2005) has no telemetry of its own. As with NFS
// Underground 2 the speed is read from speed.exe's memory, from outside and
// read-only - no plug-in in the game folder, no write of any kind - and only
// for the build it was checked on: v1.3 (entry point 0x7C4040, the one the
// widescreen fix and Extra Options require), no-CD copies of it included.
//
//   0x925E90          int       the game flow state; 6 while a race or free roam runs
//   0x92CD7C          IVehicle** the start of the "players" vehicle list (the
//   0x92CD84          int       game's VEHICLE_PLAYERS list) and how many it holds
//   IVehicle + 0x00   vtable    0x8AA828 for a PVehicle, the car a player drives
//   IVehicle + 0x74   float     its speed, m/s (IVehicle::GetSpeed, code at
//                               0x688190); the HUD speedometer draws the same
//                               number, signed (negative in reverse)
//   IVehicle + 0x88   float[3]  its velocity in the car's own frame, m/s
//                               (code at 0x688250): the speed is its length
//   0x9885D4          int       the simulation's step count: +1 for every
//                               simulated step (60 a second), still while the
//                               game is paused, back to 0 for each race
//
// Checked against the running game on 2026-09-26: the speed equalled the
// velocity's length in every one of 18,803 readings and the HUD's own number in
// all but the few read between two frames, stayed within the distance the car
// covered over time, and the step count stood still through every pause. A game
// whose executable doesn't carry the instructions these come from is refused
// as an unsupported version, not guessed at.
internal sealed class NfsMwTelemetry : IDisposable
{
    public const string ProcessName = "speed";
    private const uint ExpectedEntryPoint = 0x7C4040;
    private const uint GameFlowAddress = 0x925E90;
    private const uint GameFlowInGame = 6;
    private const uint PlayersBeginAddress = 0x92CD7C;
    private const uint PlayersCountAddress = 0x92CD84;
    // The list holds 20 at most; one player is the most a PC game has.
    private const uint PlayersMax = 20;
    private const uint PlayerVehicleVtable = 0x8AA828;
    private const int SpeedOffset = 0x74;
    private const int VelocityOffset = 0x88;
    private const uint SimStepsAddress = 0x9885D4;
    // Faster than any car the game has (a tuned one tops out near 100 m/s).
    private const float MaxMetresPerSecond = 150f;

    // The instructions the addresses above are read from, as speed.exe holds
    // them (every section is mapped at its file offset + 0x400000). Checked in
    // the file the running game was started from: mods such as the widescreen
    // fix patch code in memory, which says nothing about the build.
    private static readonly (uint Address, byte[] Bytes)[] Signature =
    [
        // The players list: mov eax,[92CD84h] ; push ebx ; push ebp ; push esi ; mov esi,[92CD7Ch]
        (0x419911, [0xA1, 0x84, 0xCD, 0x92, 0x00, 0x53, 0x55, 0x56, 0x8B, 0x35, 0x7C, 0xCD, 0x92, 0x00]),
        // The speed: fld [ecx+74h] ; ret
        (0x688190, [0xD9, 0x41, 0x74, 0xC3]),
        // The velocity: lea eax,[ecx+88h] ; ret
        (0x688250, [0x8D, 0x81, 0x88, 0x00, 0x00, 0x00, 0xC3]),
        // Both in the car's IVehicle table (0x8AA828), at slots 0x98 and 0xB4.
        (0x8AA8C0, [0x90, 0x81, 0x68, 0x00]),
        (0x8AA8DC, [0x50, 0x82, 0x68, 0x00]),
        // The car's constructor sets that table: mov [edi],8AA828h
        (0x689134, [0xC7, 0x07, 0x28, 0xA8, 0x8A, 0x00]),
        // The simulation step: mov ecx,[9885D4h] ... mov [9885D4h],ecx
        (0x6F6DCC, [0x8B, 0x0D, 0xD4, 0x85, 0x98, 0x00]),
        (0x6F6DE5, [0x89, 0x0D, 0xD4, 0x85, 0x98, 0x00]),
        // The game flow state: cmp dword ptr [925E90h],6
        (0x46E159, [0x83, 0x3D, 0x90, 0x5E, 0x92, 0x00, 0x06]),
    ];

    // The step count not moving for this long: the game is paused (or hung).
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
    private long _steps = -1;
    private DateTime _stepsMovedAt = DateTime.MinValue;

    public NfsMwTelemetry() : this(FindRunningGame, IsSupportedBuild)
    {
    }

    // For tests: another way to find the game and to judge its executable.
    internal NfsMwTelemetry(Func<uint?> findGame, Func<string, bool> isSupportedBuild)
    {
        _findGame = findGame;
        _isSupportedBuild = isSupportedBuild;
    }

    public bool IsOpen => _process != 0;

    // Why the data read was refused, in a few fixed words (so the log can say
    // it once): null while it passes.
    public string? Problem { get; private set; }

    public NfsMwReading Read(DateTime now)
    {
        if (!IsOpen && !TryOpen(now))
            return new(_unsupported ? NfsMwState.UnsupportedVersion : _noAccess ? NfsMwState.NoAccess : NfsMwState.NotRunning, 0);
        if (WaitForSingleObject(_process, 0) == WaitObject0)
        {
            Close();
            return new(NfsMwState.NotRunning, 0);
        }

        if (!TryReadUInt(GameFlowAddress, out var flow) || flow != GameFlowInGame ||
            !TryReadUInt(PlayersCountAddress, out var count) || count == 0 || count > PlayersMax ||
            !TryReadUInt(PlayersBeginAddress, out var begin) || !Plausible(begin) ||
            !TryReadUInt(begin, out var car) || !Plausible(car))
            return Waiting();
        // Only a player's car, as the game builds it: any other object here is
        // not what the offsets below were checked on.
        if (!TryReadUInt(car, out var vtable)) return Waiting();
        if (vtable != PlayerVehicleVtable) return Invalid("mașina jucătorului nu are forma așteptată");

        if (car != _car)
        {
            // Another car (a new race): nothing counts until the simulation has moved.
            _car = car;
            _steps = -1;
            _stepsMovedAt = DateTime.MinValue;
        }
        if (!TryReadFloats(car + VelocityOffset, 3, out var velocity) ||
            !TryReadFloats(car + SpeedOffset, 1, out var speedValue) ||
            !TryReadUInt(SimStepsAddress, out var steps))
            return Waiting();
        var speed = speedValue[0];

        if (steps != _steps)
        {
            if (_steps >= 0) _stepsMovedAt = now;
            _steps = steps;
        }
        if (_stepsMovedAt == DateTime.MinValue || now - _stepsMovedAt > PausedAfter)
            return new(NfsMwState.Paused, 0);

        // The speed is the length of the velocity, to the last bit the float
        // holds: anything else is not the car object this was checked on.
        if (!float.IsFinite(speed) || speed < 0 || speed > MaxMetresPerSecond)
            return Invalid("viteza este în afara limitelor");
        var length = Math.Sqrt((double)velocity[0] * velocity[0] + (double)velocity[1] * velocity[1] + (double)velocity[2] * velocity[2]);
        if (!double.IsFinite(length) || Math.Abs(length - speed) > 0.01 + speed * 0.001)
            return Invalid("viteza nu se potrivește cu vectorul de viteză");

        Problem = null;
        return new(NfsMwState.Driving, speed);
    }

    private NfsMwReading Waiting()
    {
        _car = 0;
        _steps = -1;
        _stepsMovedAt = DateTime.MinValue;
        return new(NfsMwState.WaitingForRace, 0);
    }

    private NfsMwReading Invalid(string why)
    {
        Problem = why;
        return new(NfsMwState.WaitingForRace, 0);
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
        _steps = -1;
        _stepsMovedAt = DateTime.MinValue;
        Problem = null;
        return true;
    }

    // speed.exe: 32-bit, the v1.3 entry point, and the instructions above in place.
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
        _steps = -1;
        _stepsMovedAt = DateTime.MinValue;
    }

    public void Dispose() => Close();
}

// What the Integrations tab and page show.
internal static class NfsMwStatus
{
    private static volatile NfsMwSnapshot _current = NfsMwSnapshot.Off;

    public static NfsMwSnapshot Current => _current;

    public static void Set(NfsMwSnapshot snapshot) => _current = snapshot;
}

// Off: the clock doesn't want the tile (or has no such tile: ClockUnsupported).
internal sealed record NfsMwSnapshot(bool Reading, bool ClockUnsupported, NfsMwReading Game, string? Problem)
{
    public static readonly NfsMwSnapshot Off = new(false, false, new(NfsMwState.NotRunning, 0), null);
}
