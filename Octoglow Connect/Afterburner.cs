using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace OctoglowSender;

// MSI Afterburner and RivaTuner Statistics Server, read the way their SDKs
// describe (SDK\Include\MAHMSharedMemory.h and RTSSSharedMemory.h in their
// install folders): each publishes a named shared memory block that any
// program on the computer may read. Nothing here writes to either one, and a
// value is taken only when the memory says it is live - a block can outlive
// its program (RTSS's hooks keep theirs open after RTSS.exe exits), so being
// there proves nothing.
//
// - Temperatures: MSI Afterburner's hardware monitoring ("MAHMSharedMemory"),
//   the sources ticked under Settings › Monitoring. CPU is source 0x80, "CPU
//   temperature"; GPU is source 0x00, "GPU temperature", one per GPU.
// - FPS: RTSS's statistics ("RTSSSharedMemoryV2"), the frame rate it measures
//   for the 3D application last in the foreground - the number its on-screen
//   display shows, which is also where Afterburner's own "Framerate" comes from.
// - Whether that frame rate is a game's, for the Game FPS priority tile: RTSS
//   measures every 3D application (a slicer, a browser), so see ForegroundGame.

// One GPU as MSI Afterburner lists it: Index is 0 for its GPU1.
public sealed record AbGpu(int Index, string Id, string Name, bool HasTemperature);

// Whether the frame rate belongs to a game being played. Only Listed and
// FullScreen put the Game FPS tile up.
public enum AbGame
{
    NoFps,
    Listed,       // its window is in front and Windows lists it as a game
    FullScreen,   // its window is in front and fills its screen
    NotInFront,   // another window is in front (alt-tabbed out)
    NotAGame,     // in front, but neither listed nor full screen: a slicer, a browser
}

// Why a value is not there, so the Integrations page can say what to do.
public enum AbMissing
{
    None,
    AfterburnerNotRunning,
    AfterburnerNoAccess,
    AfterburnerStale,
    SourceOff,
    NoValue,
    GpuNotFound,
    RtssNotRunning,
    RtssNoAccess,
    No3DApplication,
}

public sealed record AbReading(
    bool AfterburnerLive,
    bool RtssLive,
    int? Fps,
    string? FpsApplication,
    AbMissing FpsMissing,
    AbGame FpsGame,
    double? CpuCelsius,
    string? CpuSource,
    AbMissing CpuMissing,
    double? GpuCelsius,
    AbGpu? Gpu,
    AbMissing GpuMissing,
    IReadOnlyList<AbGpu> Gpus)
{
    public static readonly AbReading Nothing = new(false, false, null, null, AbMissing.RtssNotRunning, AbGame.NoFps, null, null,
        AbMissing.AfterburnerNotRunning, null, null, AbMissing.AfterburnerNotRunning, []);

    // What the Game FPS tile goes by.
    public bool FpsIsGame => Fps is not null && FpsGame is AbGame.Listed or AbGame.FullScreen;
}

public static class AfterburnerReader
{
    public const string MahmName = "MAHMSharedMemory";
    public const string RtssName = "RTSSSharedMemoryV2";

    // Multi-character constants 'MAHM' and 'RTSS' as the SDK's compiler builds them.
    private const uint MahmSignature = 0x4D41484D;
    private const uint RtssSignature = 0x52545353;
    private const uint Dead = 0xDEAD;

    private const uint SourceGpuTemperature = 0x00;
    private const uint SourceCpuTemperature = 0x80;

    // MAHM_SHARED_MEMORY_ENTRY: five MAX_PATH strings, then data, limits, flags, GPU, source.
    private const int MaxPath = 260;
    private const int EntryData = 5 * MaxPath;
    private const int EntryGpu = EntryData + 16;
    private const int EntrySrcId = EntryData + 20;
    private const int EntryMinSize = EntryData + 24;
    private const int GpuEntryMinSize = 5 * MaxPath + 4;

    // RTSS_SHARED_MEMORY and its application entries.
    private const int RtssLastForegroundApp = 64;
    private const int RtssLastForegroundPid = 68;
    private const uint RtssForegroundFieldsVersion = 0x00020010; // v2.16
    private const int AppName = 4;
    private const int AppFlags = AppName + MaxPath;
    private const int AppTime0 = AppFlags + 4;
    private const int AppTime1 = AppFlags + 8;
    private const int AppFrames = AppFlags + 12;
    private const int AppMinSize = AppFrames + 4;
    private const uint AppFlagApiMask = 0x0000FFFF;

    // Afterburner polls once a second by default (up to every few seconds if
    // set so); a stamp older than this means it stopped.
    public static readonly TimeSpan MahmStaleAfter = TimeSpan.FromSeconds(15);
    // RTSS measures the frame rate over about a second; older than this, the
    // application stopped drawing (closed, paused, minimised).
    public const uint RtssStaleAfterMs = 2500;

    // gpuChoice: "" picks automatically - the first GPU in Afterburner's list
    // (GPU1, GPU2, ...) that has a temperature source - otherwise the Id of one GPU.
    public static AbReading Read(string gpuChoice, string mahmName = MahmName, string rtssName = RtssName)
    {
        var mahm = ReadMahm(mahmName);
        var rtss = ReadRtss(rtssName);
        return new AbReading(
            mahm.Live, rtss.Live,
            rtss.Fps, rtss.Application, rtss.Missing, rtss.Game,
            mahm.CpuCelsius, mahm.CpuSource, mahm.CpuMissing,
            PickGpu(mahm, gpuChoice, out var gpu, out var gpuMissing), gpu, gpuMissing,
            mahm.Gpus);
    }

    // ---------------------------------------------------------------- MSI Afterburner

    private sealed record MahmEntry(string Name, string Units, float Data, uint Gpu, uint SourceId);

    private sealed class MahmData
    {
        public bool Live;
        public AbMissing Missing = AbMissing.AfterburnerNotRunning;
        public double? CpuCelsius;
        public string? CpuSource;
        public AbMissing CpuMissing = AbMissing.AfterburnerNotRunning;
        public List<AbGpu> Gpus = [];
        public List<MahmEntry> Entries = [];
    }

    private static MahmData ReadMahm(string name)
    {
        var result = new MahmData();
        try
        {
            using var file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            long capacity = view.Capacity;
            if (capacity < 32) return Fail(result, AbMissing.AfterburnerNotRunning);
            var signature = view.ReadUInt32(0);
            if (signature != MahmSignature)
                return Fail(result, AbMissing.AfterburnerNotRunning); // 0xDEAD while it unloads, anything else before it starts
            var version = view.ReadUInt32(4);
            var headerSize = view.ReadUInt32(8);
            var count = view.ReadUInt32(12);
            var entrySize = view.ReadUInt32(16);
            var polled = view.ReadInt32(20); // __time32_t, UTC seconds
            if (version < 0x00020000 || headerSize < 32 || entrySize < EntryMinSize || count > 4096)
                return Fail(result, AbMissing.AfterburnerNotRunning);
            var gpuCount = view.ReadUInt32(24);
            var gpuEntrySize = view.ReadUInt32(28);
            if (gpuCount > 64 || (gpuCount > 0 && gpuEntrySize < GpuEntryMinSize)) return Fail(result, AbMissing.AfterburnerNotRunning);
            var entriesEnd = (long)headerSize + (long)count * entrySize;
            if (entriesEnd + (long)gpuCount * gpuEntrySize > capacity) return Fail(result, AbMissing.AfterburnerNotRunning);

            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(polled);
            if (polled <= 0 || age > MahmStaleAfter || age < -MahmStaleAfter)
                return Fail(result, AbMissing.AfterburnerStale);

            var buffer = new byte[MaxPath];
            for (uint i = 0; i < count; i++)
            {
                long at = headerSize + (long)i * entrySize;
                result.Entries.Add(new MahmEntry(
                    AnsiString(view, at, buffer),
                    AnsiString(view, at + MaxPath, buffer),
                    view.ReadSingle(at + EntryData),
                    view.ReadUInt32(at + EntryGpu),
                    view.ReadUInt32(at + EntrySrcId)));
            }
            for (uint g = 0; g < gpuCount; g++)
            {
                long at = entriesEnd + (long)g * gpuEntrySize;
                var id = AnsiString(view, at, buffer);
                var family = AnsiString(view, at + MaxPath, buffer);
                var device = AnsiString(view, at + 2 * MaxPath, buffer);
                var label = device.Length > 0 ? device : family.Length > 0 ? family : $"GPU{g + 1}";
                var hasTemperature = result.Entries.Any(e => e.SourceId == SourceGpuTemperature && e.Gpu == g);
                result.Gpus.Add(new AbGpu((int)g, id, label, hasTemperature));
            }
            result.Live = true;
            result.Missing = AbMissing.None;
            ReadCpu(result);
            return result;
        }
        catch (FileNotFoundException)
        {
            return Fail(result, AbMissing.AfterburnerNotRunning);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(result, AbMissing.AfterburnerNoAccess);
        }
        catch (IOException)
        {
            return Fail(result, AbMissing.AfterburnerNotRunning);
        }
    }

    private static MahmData Fail(MahmData result, AbMissing why)
    {
        result.Live = false;
        result.Missing = why;
        result.CpuMissing = why;
        result.Entries.Clear();
        result.Gpus.Clear();
        return result;
    }

    // Afterburner's own "CPU temperature" (the whole processor); without it,
    // the hottest of the per-core ones that are ticked ("CPU1 temperature"...).
    private static void ReadCpu(MahmData mahm)
    {
        var cpu = mahm.Entries.Where(e => e.SourceId == SourceCpuTemperature).ToList();
        if (cpu.Count == 0)
        {
            mahm.CpuMissing = AbMissing.SourceOff;
            return;
        }
        var whole = cpu.FirstOrDefault(e => e.Name.Equals("CPU temperature", StringComparison.OrdinalIgnoreCase));
        if (whole is not null)
        {
            Take(whole, out mahm.CpuCelsius, out mahm.CpuMissing);
            mahm.CpuSource = whole.Name;
            return;
        }
        MahmEntry? hottest = null;
        double hottestValue = double.MinValue;
        foreach (var core in cpu)
        {
            Take(core, out var value, out _);
            if (value is { } v && v > hottestValue)
            {
                hottestValue = v;
                hottest = core;
            }
        }
        if (hottest is null)
        {
            mahm.CpuMissing = AbMissing.NoValue;
            return;
        }
        mahm.CpuCelsius = hottestValue;
        mahm.CpuSource = hottest.Name;
        mahm.CpuMissing = AbMissing.None;
    }

    private static double? PickGpu(MahmData mahm, string choice, out AbGpu? gpu, out AbMissing missing)
    {
        gpu = null;
        if (!mahm.Live)
        {
            missing = mahm.Missing;
            return null;
        }
        if (choice.Length > 0)
        {
            gpu = mahm.Gpus.FirstOrDefault(g => g.Id == choice);
            if (gpu is null)
            {
                missing = AbMissing.GpuNotFound;
                return null;
            }
        }
        else
        {
            gpu = mahm.Gpus.FirstOrDefault(g => g.HasTemperature);
            if (gpu is null)
            {
                missing = AbMissing.SourceOff;
                return null;
            }
        }
        var index = (uint)gpu.Index;
        var entry = mahm.Entries.FirstOrDefault(e => e.SourceId == SourceGpuTemperature && e.Gpu == index);
        if (entry is null)
        {
            missing = AbMissing.SourceOff;
            return null;
        }
        Take(entry, out var celsius, out missing);
        return celsius;
    }

    // A reading in °C, or why there is none: Afterburner writes FLT_MAX while it
    // has no value, and a unit ending in F is converted.
    private static void Take(MahmEntry entry, out double? celsius, out AbMissing missing)
    {
        celsius = null;
        var data = entry.Data;
        if (data == float.MaxValue || float.IsNaN(data) || float.IsInfinity(data))
        {
            missing = AbMissing.NoValue;
            return;
        }
        double value = data;
        if (entry.Units.TrimEnd().EndsWith('F')) value = (value - 32) * 5 / 9;
        if (value is < -50 or > 150)
        {
            missing = AbMissing.NoValue;
            return;
        }
        celsius = value;
        missing = AbMissing.None;
    }

    private static string AnsiString(MemoryMappedViewAccessor view, long at, byte[] buffer)
    {
        view.ReadArray(at, buffer, 0, MaxPath);
        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.Latin1.GetString(buffer, 0, length < 0 ? MaxPath : length).Trim();
    }

    // ---------------------------------------------------------------- RTSS

    private sealed record RtssData(bool Live, int? Fps, string? Application, AbMissing Missing, AbGame Game = AbGame.NoFps);

    private static RtssData ReadRtss(string name)
    {
        try
        {
            using var file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            long capacity = view.Capacity;
            if (capacity < 72 || view.ReadUInt32(0) != RtssSignature) return NoRtss();
            var version = view.ReadUInt32(4);
            var appEntrySize = view.ReadUInt32(8);
            var appOffset = view.ReadUInt32(12);
            var appCount = view.ReadUInt32(16);
            if (version < 0x00020000 || appEntrySize < AppMinSize || appCount > 1024 ||
                appOffset + (long)appCount * appEntrySize > capacity)
                return NoRtss();
            if (version < RtssForegroundFieldsVersion)
                return new RtssData(true, null, null, AbMissing.No3DApplication); // too old to say which application is in front

            var index = view.ReadUInt32(RtssLastForegroundApp);
            var pid = view.ReadUInt32(RtssLastForegroundPid);
            if (pid == 0 || index >= appCount) return Idle();
            long at = appOffset + (long)index * appEntrySize;
            // The entry names its process, except after RTSS.exe itself restarts
            // while the application goes on running: RTSS then clears the
            // entry's process ID and name, and the hooks go on counting frames
            // into it (seen with RTSS 7.3.5). The header's foreground PID still
            // says whose it is. An entry naming another process is a reused one.
            var entryPid = view.ReadUInt32(at);
            if (entryPid != 0 && entryPid != pid) return Idle();
            var flags = view.ReadUInt32(at + AppFlags);
            var time0 = view.ReadUInt32(at + AppTime0);
            var time1 = view.ReadUInt32(at + AppTime1);
            var frames = view.ReadUInt32(at + AppFrames);
            // An application RTSS hooked but which draws nothing through a 3D API
            // (a browser, this app) has no frame rate.
            if ((flags & AppFlagApiMask) == 0 || time0 == 0 || time1 <= time0 || frames == 0) return Idle();
            // How long ago the last measurement ended, on the millisecond clock
            // GetTickCount reads. Signed, so a stamp a few milliseconds ahead of
            // ours (timeGetTime's base can differ slightly) still counts as now.
            var age = unchecked((int)((uint)Environment.TickCount - time1));
            if (Math.Abs((long)age) > RtssStaleAfterMs) return Idle();
            var fps = (int)Math.Round(1000.0 * frames / (time1 - time0), MidpointRounding.AwayFromZero);
            if (fps is < 0 or > 9999) return Idle();
            var buffer = new byte[MaxPath];
            var path = AnsiString(view, at + AppName, buffer);
            if (path.Length == 0) path = ForegroundGame.ImagePath(pid) ?? "";
            return new RtssData(true, fps, path.Length > 0 ? Path.GetFileName(path) : null, AbMissing.None,
                ForegroundGame.Classify(pid, path));
        }
        catch (FileNotFoundException)
        {
            return NoRtss();
        }
        catch (UnauthorizedAccessException)
        {
            return new RtssData(false, null, null, AbMissing.RtssNoAccess);
        }
        catch (IOException)
        {
            return NoRtss();
        }

        static RtssData NoRtss() => new(false, null, null, AbMissing.RtssNotRunning);

        // The memory is there; whether RTSS itself is running decides what the
        // missing frame rate means. Only the explanation depends on this, never the value.
        static RtssData Idle() => RtssProcessRunning()
            ? new RtssData(true, null, null, AbMissing.No3DApplication)
            : new RtssData(false, null, null, AbMissing.RtssNotRunning);
    }

    // Looked up at most every few seconds: listing the processes costs more
    // than reading the memory, and it only picks the words on the page.
    private static long _rtssCheckedAt = long.MinValue / 2;
    private static bool _rtssRunning;

    private static bool RtssProcessRunning()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _rtssCheckedAt) < 5000) return Volatile.Read(ref _rtssRunning);
        var processes = Process.GetProcessesByName("RTSS");
        foreach (var process in processes) process.Dispose();
        Volatile.Write(ref _rtssRunning, processes.Length > 0);
        Interlocked.Exchange(ref _rtssCheckedAt, now);
        return processes.Length > 0;
    }
}

// Whether the application RTSS measures is a game being played. RTSS counts
// frames for every 3D application it hooks - Bambu Studio, a browser - so the
// frame rate alone says nothing about play. A game here is the window in front
// that either Windows lists as a game (the list Game Bar and Game Mode go by,
// HKCU\System\GameConfigStore\Children, filled in as games are first run) or
// fills its whole screen, as games in full screen or borderless mode do.
// A maximised window does not count: it is an ordinary window, taskbar or not.
internal static class ForegroundGame
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MonitorDefaultToNearest = 2;
    private const string GameListKey = @"System\GameConfigStore\Children";
    // New games are added to the list as they are first run.
    private static readonly TimeSpan GameListReadEvery = TimeSpan.FromSeconds(10);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rectangle);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    private static extern unsafe bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    private static readonly object Gate = new();
    private static HashSet<string> _games = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _gamesReadAt = DateTime.MinValue;

    public static AbGame Classify(uint pid, string path)
    {
        var window = GetForegroundWindow();
        if (window == 0) return AbGame.NotInFront;
        GetWindowThreadProcessId(window, out var owner);
        if (owner != pid) return AbGame.NotInFront;
        if (path.Length > 0 && IsListedGame(path)) return AbGame.Listed;
        return FillsScreen(window) ? AbGame.FullScreen : AbGame.NotAGame;
    }

    // The executable's full path, for a process RTSS no longer names. Asked
    // with the least access Windows has, which it grants for other users' and
    // elevated processes too; null when the process is gone.
    public static unsafe string? ImagePath(uint pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0) return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static bool IsListedGame(string path)
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _gamesReadAt >= GameListReadEvery)
            {
                _games = ReadGameList();
                _gamesReadAt = DateTime.UtcNow;
            }
            return _games.Contains(path);
        }
    }

    private static HashSet<string> ReadGameList()
    {
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var children = Registry.CurrentUser.OpenSubKey(GameListKey);
            if (children is null) return games;
            foreach (var name in children.GetSubKeyNames())
            {
                using var child = children.OpenSubKey(name);
                if (child?.GetValue("MatchedExeFullPath") is string exe && exe.Length > 0) games.Add(exe);
            }
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            // No list: only the full-screen test is left.
        }
        return games;
    }

    private static bool FillsScreen(nint window)
    {
        if (IsIconic(window) || IsZoomed(window)) return false;
        if (!GetWindowRect(window, out var rect)) return false;
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Unsafe.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return false;
        return Covers(rect, info.Monitor);
    }

    internal static bool Covers(Rect window, Rect screen) =>
        window.Left <= screen.Left && window.Top <= screen.Top &&
        window.Right >= screen.Right && window.Bottom >= screen.Bottom;
}

// What the backend last read, for the Integrations card and page.
public enum AbState { Off, ClockUnsupported, Reading }

public sealed record AbSnapshot(AbState State, AbReading Reading)
{
    public static readonly AbSnapshot Off = new(AbState.Off, AbReading.Nothing);
}

public static class AfterburnerStatus
{
    private static volatile AbSnapshot _current = AbSnapshot.Off;
    public static AbSnapshot Current => _current;
    public static void Set(AbSnapshot snapshot) => _current = snapshot;
}
