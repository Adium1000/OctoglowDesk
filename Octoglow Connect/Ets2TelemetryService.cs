using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
namespace OctoglowSender;

/// <summary>
/// ETS2/ATS speed telemetry.
///
/// Reliable part (always works): detects whether eurotrucks2.exe is running,
/// same as the python psutil/tasklist check.
///
/// Speed: RenCloud's scs-sdk-plugin (https://github.com/RenCloud/scs-sdk-plugin),
/// once scs-telemetry.dll is in the game's bin\win_x64\plugins, writes the game's
/// telemetry into the shared memory "Local\SCSTelemetry". The speed is read from
/// there directly - the layout is the plugin's scsTelemetryMap_t, whose zones sit
/// at fixed offsets - rather than through its C# client, SCSSdkClient.dll: that
/// had to be loaded by reflection, which a Native AOT build cannot do. Without the
/// plugin, ETS2 support only logs connect/disconnect, without speed.
/// </summary>
public class Ets2TelemetryService
{
    private const string MapName = @"Local\SCSTelemetry";
    private const int MapSize = 32 * 1024;
    // scsTelemetryMap_t: sdkActive is the first byte of zone 1, the plugin's
    // revision opens zone 2 (offset 40), and truck_f.speed follows common_f.scale
    // (4 bytes) and config_f (244 bytes) in zone 4, which starts at 700.
    private const int SdkActiveOffset = 0;
    private const int RevisionOffset = 40;
    private const int SpeedOffset = 700 + 4 + 244;

    private readonly Action<string> _log;
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private bool _loggedFound;
    private bool _loggedMissing;

    public Ets2TelemetryService(Action<string> log)
    {
        _log = log;
    }

    // Opened when the game has created it; kept while it is readable.
    private MemoryMappedViewAccessor? View()
    {
        if (_view is not null) return _view;
        try
        {
            _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            _view = _map.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);
            _loggedMissing = false;
            if (!_loggedFound)
            {
                _loggedFound = true;
                _log($"[ETS2] Telemetria scs-sdk-plugin găsită (revizia {_view.ReadUInt32(RevisionOffset)}) - viteză reală activă.");
            }
            return _view;
        }
        catch (FileNotFoundException)
        {
            if (!_loggedMissing)
            {
                _loggedMissing = true;
                _log("[ETS2] Telemetria scs-sdk-plugin lipsește - se detectează doar rularea jocului, fără viteză. " +
                     "Pune scs-telemetry.dll în bin\\win_x64\\plugins al jocului.");
            }
        }
        catch (Exception e)
        {
            _log($"[ETS2 WARN] Nu pot citi telemetria: {e.Message}");
        }
        Close();
        return null;
    }

    private void Close()
    {
        _view?.Dispose();
        _map?.Dispose();
        _view = null;
        _map = null;
    }

    private static bool _loggedDetectionError;
    public bool IsEts2RunningLogged()
    {
        try
        {
            var byName = Process.GetProcessesByName("eurotrucks2");
            if (byName.Length > 0) return true;
            return IsEts2RunningViaTasklist();
        }
        catch (Exception e)
        {
            if (!_loggedDetectionError)
            {
                _loggedDetectionError = true;
                _log($"[ETS2 WARN] Process.GetProcessesByName a eșuat ({e.Message}). " +
                     "Trec pe verificare de rezervă cu 'tasklist'.");
            }
            return IsEts2RunningViaTasklist();
        }
    }
    public static bool IsEts2Running()
    {
        try
        {
            return Process.GetProcessesByName("eurotrucks2").Length > 0
                   || IsEts2RunningViaTasklist();
        }
        catch
        {
            return IsEts2RunningViaTasklist();
        }
    }
    private static bool IsEts2RunningViaTasklist()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tasklist",
                Arguments = "/FI \"IMAGENAME eq eurotrucks2.exe\" /NH",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output.Contains("eurotrucks2.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    /// <summary>Returns speed in km/h while the plugin's telemetry is live, otherwise null.</summary>
    public int? GetSpeedKmh()
    {
        var view = View();
        if (view is null) return null;
        try
        {
            // The plugin clears sdkActive when the game shuts it down; what is
            // left in the memory after that is the last speed, not the current one.
            if (!view.ReadBoolean(SdkActiveOffset))
            {
                Close();
                return null;
            }
            // Metres per second, negative in reverse.
            var metresPerSecond = view.ReadSingle(SpeedOffset);
            if (float.IsNaN(metresPerSecond) || float.IsInfinity(metresPerSecond)) return null;
            return (int)Math.Round(Math.Abs(metresPerSecond) * 3.6f);
        }
        catch (Exception e)
        {
            _log($"[ETS2 WARN] Nu pot citi viteza din SDK: {e.Message}");
            Close();
            return null;
        }
    }
}
