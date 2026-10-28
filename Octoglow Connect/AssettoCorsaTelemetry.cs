using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace OctoglowSender;

internal enum AssettoCorsaState { NotRunning, NotDriving, Driving }

internal readonly record struct AssettoCorsaReading(AssettoCorsaState State, int Kmh)
{
    public static readonly AssettoCorsaReading NotRunning = new(AssettoCorsaState.NotRunning, 0);
    public static readonly AssettoCorsaReading NotDriving = new(AssettoCorsaState.NotDriving, 0);
}

// Assetto Corsa - the original game, not Competizione - keeps its telemetry in
// shared memory for as long as a session runs, written by its race executable,
// acs.exe (the launcher and Content Manager only start it). The layout is
// Kunos' Shared Memory Reference - SPageFilePhysics and SPageFileGraphic,
// #pragma pack(4) - and of it only this is mapped and read:
//
//   Local\acpmf_physics    0  int    packetId   moves on with every physics step
//                          28 float  speedKmh   the car's speed, in km/h
//   Local\acpmf_graphics   4  int    status     AC_OFF 0, AC_REPLAY 1, AC_LIVE 2, AC_PAUSE 3
//
// The car is driven while the status is AC_LIVE and packetId keeps moving:
// a paused game, a replay, a game that hung or has gone - whose pages another
// program may still hold open, frozen at the last speed - are not driving.
// Competizione writes pages of the same names; it runs as another process, so
// only acs.exe's are opened. The pages are let go the moment acs.exe exits.
internal sealed class AssettoCorsaTelemetry : IDisposable
{
    public const string ProcessName = "acs";
    private const string PhysicsName = @"Local\acpmf_physics";
    private const string GraphicsName = @"Local\acpmf_graphics";
    private const int PacketIdOffset = 0;
    private const int SpeedOffset = 28;
    private const int StatusOffset = 4;
    private const int StatusLive = 2;
    // Faster than any car in the game, or not a number at all: not a speed.
    private const float MaxKmh = 1500f;

    // Live, yet no physics step for this long: stopped writing.
    public static readonly TimeSpan FrozenAfter = TimeSpan.FromSeconds(1);
    // Frozen this long, the pages are let go and looked for again, in case the
    // game went without its exit being seen.
    public static readonly TimeSpan ReleaseAfter = TimeSpan.FromSeconds(10);
    // How often a game that isn't open is looked for.
    public static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(2);

    private readonly Func<Process?> _findGame;
    private readonly string _physicsName;
    private readonly string _graphicsName;
    private Process? _game;
    private MemoryMappedFile? _physicsMap;
    private MemoryMappedFile? _graphicsMap;
    private MemoryMappedViewAccessor? _physics;
    private MemoryMappedViewAccessor? _graphics;
    private int _lastPacketId;
    private DateTime _packetMovedAt;
    private DateTime _openedAt;
    private DateTime _nextLook = DateTime.MinValue;

    public AssettoCorsaTelemetry() : this(FindRunningGame, PhysicsName, GraphicsName)
    {
    }

    // For tests: another way to find the game, and other page names.
    internal AssettoCorsaTelemetry(Func<Process?> findGame, string physicsName, string graphicsName)
    {
        _findGame = findGame;
        _physicsName = physicsName;
        _graphicsName = graphicsName;
    }

    public bool IsOpen => _physics is not null;

    // Why the pages could not be read, when that is something other than the
    // game not running; null otherwise.
    public string? Problem { get; private set; }

    public AssettoCorsaReading Read(DateTime now)
    {
        if (!IsOpen && !TryOpen(now)) return AssettoCorsaReading.NotRunning;
        if (GameExited())
        {
            Close();
            return AssettoCorsaReading.NotRunning;
        }
        try
        {
            var packetId = _physics!.ReadInt32(PacketIdOffset);
            var status = _graphics!.ReadInt32(StatusOffset);
            var kmh = _physics.ReadSingle(SpeedOffset);
            if (packetId != _lastPacketId)
            {
                _lastPacketId = packetId;
                _packetMovedAt = now;
            }
            var stillFor = now - (_packetMovedAt == DateTime.MinValue ? _openedAt : _packetMovedAt);
            if (stillFor > ReleaseAfter)
            {
                Close();
                return AssettoCorsaReading.NotDriving;
            }
            if (status != StatusLive || _packetMovedAt == DateTime.MinValue || now - _packetMovedAt > FrozenAfter)
                return AssettoCorsaReading.NotDriving;
            if (!float.IsFinite(kmh) || Math.Abs(kmh) > MaxKmh) return AssettoCorsaReading.NotDriving;
            // Whole km/h, halves away from zero, as the clock is sent them.
            return new AssettoCorsaReading(AssettoCorsaState.Driving,
                (int)Math.Round(Math.Abs(kmh), MidpointRounding.AwayFromZero));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Problem = error.Message;
            Close();
            return AssettoCorsaReading.NotRunning;
        }
    }

    private bool TryOpen(DateTime now)
    {
        if (now < _nextLook) return false;
        _nextLook = now + LookEvery;
        var game = _findGame();
        if (game is null) return false;
        try
        {
            _physicsMap = MemoryMappedFile.OpenExisting(_physicsName, MemoryMappedFileRights.Read);
            _graphicsMap = MemoryMappedFile.OpenExisting(_graphicsName, MemoryMappedFileRights.Read);
            _physics = _physicsMap.CreateViewAccessor(0, SpeedOffset + sizeof(float), MemoryMappedFileAccess.Read);
            _graphics = _graphicsMap.CreateViewAccessor(0, StatusOffset + sizeof(int), MemoryMappedFileAccess.Read);
            _game = game;
            // A reading counts only once the physics has stepped since the pages
            // were opened: what was in them before could be any age.
            _lastPacketId = _physics.ReadInt32(PacketIdOffset);
            _packetMovedAt = DateTime.MinValue;
            _openedAt = now;
            Problem = null;
            return true;
        }
        catch (FileNotFoundException)
        {
            // acs.exe is still loading the session: the pages come a moment later.
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Problem = error.Message;
        }
        game.Dispose();
        Close();
        return false;
    }

    private bool GameExited()
    {
        if (_game is null) return false;
        try
        {
            return _game.HasExited;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // It can't be watched: the pages' freezing tells instead.
            _game.Dispose();
            _game = null;
            return false;
        }
    }

    private static Process? FindRunningGame()
    {
        try
        {
            var found = Process.GetProcessesByName(ProcessName);
            for (var i = 1; i < found.Length; i++) found[i].Dispose();
            return found.Length > 0 ? found[0] : null;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    public void Close()
    {
        _physics?.Dispose();
        _graphics?.Dispose();
        _physicsMap?.Dispose();
        _graphicsMap?.Dispose();
        _game?.Dispose();
        _physics = null;
        _graphics = null;
        _physicsMap = null;
        _graphicsMap = null;
        _game = null;
    }

    public void Dispose() => Close();
}

// What the Integrations tab shows: whether the car's speed is coming in.
internal static class AssettoCorsaStatus
{
    private static volatile bool _receiving;

    public static bool Receiving => _receiving;

    public static void Set(bool receiving) => _receiving = receiving;
}
