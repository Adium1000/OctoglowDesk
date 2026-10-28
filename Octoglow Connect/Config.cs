using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace OctoglowSender;
public class NowPlayingPlayer
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool BuiltIn { get; set; } = false;

    public NowPlayingPlayer Clone() => new() { Name = Name, Match = Match, Enabled = Enabled, BuiltIn = BuiltIn };
}
public class AppConfig
{
    public string Esp32Ip { get; set; } = "";
    public int Esp32Port { get; set; } = 80;
    // The saved clock's own id (its MAC) and name on the network, as its
    // /authstate gives them: what finds it again when the router moves it.
    public string Esp32Id { get; set; } = "";
    public string Esp32Host { get; set; } = "";
    public string ScUser { get; set; } = "";
    public string ScPass { get; set; } = ""; // decrypted in-memory; encrypted on disk
    public int PollIntervalSeconds { get; set; } = 3;
    public int SendIntervalSeconds { get; set; } = 10;
    public int NotifMaxChars { get; set; } = 110;
    public int Ets2PollIntervalMs { get; set; } = 500;
    public bool StopPriorityTileWhenTruckStops { get; set; } = true;
    // BeamNG.drive's OutGauge packets are heard at this address and port - the
    // ones set in the game (Options > Advanced > Protocols > OutGauge UDP protocol):
    // 127.0.0.1 when it runs on this computer.
    public string BeamNgOutGaugeAddress { get; set; } = BeamNgListenAddress.Default;
    public int BeamNgOutGaugePort { get; set; } = 4444;
    public bool StopBeamNgTileWhenVehicleStops { get; set; } = true;
    // Assetto Corsa's speed comes from the game's shared memory: nothing to set
    // but whether a car standing still keeps the tile up.
    public bool StopAssettoTileWhenCarStops { get; set; } = true;
    // NFS Underground 2's speed is read from the game itself: the same choice.
    public bool StopNfsu2TileWhenCarStops { get; set; } = true;
    public bool StopNfsMwTileWhenCarStops { get; set; } = true;
    public bool StopNfsCarbonTileWhenCarStops { get; set; } = true;
    public bool RunAtStartup { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool InlineClockConfigurationPages { get; set; } = false;
    public bool HideDisconnectedUsbDevices { get; set; } = true;
    public bool HideDisconnectedWifiDevices { get; set; } = false;
    public bool ShowNetworkDeviceRefreshButtons { get; set; } = true;
    public int WindowWidth { get; set; } = 1050;
    public int WindowHeight { get; set; } = 790;
    // Zero keeps the natural preview height until the divider is first dragged.
    public double ClockPreviewFraction { get; set; } = 0;
    public double MacroPreviewFraction { get; set; } = 0;
    /// <summary>"system" | "light" | "dark"</summary>
    public string Theme { get; set; } = "system";
    /// <summary>"none" | "mica" | "micaalt" | "acrylic"</summary>
    public string BackdropMaterial { get; set; } = "micaalt";
    public bool KeepNowPlayingWhenPaused { get; set; } = false;
    /// <summary>"B1" | "B2" | "B3"</summary>
    public string VoicemeeterBus { get; set; } = "B1";
    public bool VoicemeeterAutoStopOnSilence { get; set; } = true;
    public int VoicemeeterSilenceSeconds { get; set; } = 4;
    // OBS Studio's WebSocket server (obs-websocket 5.x). Kept on this computer
    // only; the password never goes to the clock.
    public string ObsHost { get; set; } = "127.0.0.1";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = ""; // decrypted in-memory; encrypted on disk
    // qBittorrent's WebUI. Kept on this computer only; the key and the password
    // never go to the clock, and are encrypted on disk.
    public string QbitHost { get; set; } = "127.0.0.1";
    public int QbitPort { get; set; } = 8080;
    /// <summary>"password" (user name and password, every version) | "apikey" (qBittorrent 5.2+)</summary>
    public string QbitAuth { get; set; } = "password";
    public string QbitUsername { get; set; } = "";
    public string QbitPassword { get; set; } = ""; // decrypted in-memory; encrypted on disk
    public string QbitApiKey { get; set; } = ""; // decrypted in-memory; encrypted on disk
    // Local Bambu printer telemetry. The LAN access code never reaches the clock.
    public string BambuHost { get; set; } = "";
    public string BambuSerial { get; set; } = "";
    public string BambuAccessCode { get; set; } = "";
    // Which GPU's temperature the MSI Afterburner tile shows: "" for the first
    // one in Afterburner's list that has a temperature, otherwise its GPU id
    // (VEN_...&DEV_...&BUS_...), which stays the same when GPUs come and go.
    public string AfterburnerGpu { get; set; } = "";
    /// <summary>"system" | "ro" | "en"</summary>
    public string Language { get; set; } = "system";
    public List<NowPlayingPlayer> VideoPlayers { get; set; } = DefaultVideoPlayers();
    public List<NowPlayingPlayer> MusicPlayers { get; set; } = DefaultMusicPlayers();
    public static List<NowPlayingPlayer> DefaultVideoPlayers() =>
    [
        new() { Name = "Google Chrome", Match = "chrome", BuiltIn = true },
        new() { Name = "Microsoft Edge", Match = "msedge", BuiltIn = true },
        new() { Name = "Opera", Match = "opera", BuiltIn = true },
        new() { Name = "Firefox", Match = "firefox", BuiltIn = true },
        new() { Name = "Brave", Match = "brave", BuiltIn = true },
    ];

    public static List<NowPlayingPlayer> DefaultMusicPlayers() =>
    [
        new() { Name = "Apple Music", Match = "applemusic", BuiltIn = true },
        new() { Name = "SoundCloud", Match = "soundcloud", BuiltIn = true },
        new() { Name = "Spotify", Match = "spotify", BuiltIn = true },
    ];
}

public static partial class ConfigStore
{
    private static volatile bool _stopPriorityTileWhenTruckStops = true;
    private static volatile string _beamNgOutGaugeAddress = BeamNgListenAddress.Default;
    private static volatile int _beamNgOutGaugePort = 4444;
    private static volatile bool _stopBeamNgTileWhenVehicleStops = true;
    private static volatile bool _stopAssettoTileWhenCarStops = true;
    private static volatile bool _stopNfsu2TileWhenCarStops = true;
    private static volatile bool _stopNfsMwTileWhenCarStops = true;
    private static volatile bool _stopNfsCarbonTileWhenCarStops = true;
    private static volatile bool _keepNowPlayingWhenPaused;
    private static volatile bool _inlineClockConfigurationPages;
    private static volatile bool _hideDisconnectedUsbDevices = true;
    private static volatile bool _hideDisconnectedWifiDevices;
    private static volatile bool _showNetworkDeviceRefreshButtons = true;
    private static volatile string _voicemeeterBus = "B1";
    private static volatile bool _voicemeeterAutoStopOnSilence = true;
    private static volatile int _voicemeeterSilenceSeconds = 4;
    private static volatile string _obsHost = "127.0.0.1";
    private static volatile int _obsPort = 4455;
    private static volatile string _obsPassword = "";
    private static int _obsSettingsVersion;
    private static volatile string _qbitHost = "127.0.0.1";
    private static volatile int _qbitPort = 8080;
    private static volatile bool _qbitUseApiKey;
    private static volatile string _qbitUsername = "";
    private static volatile string _qbitPassword = "";
    private static volatile string _qbitApiKey = "";
    private static int _qbitSettingsVersion;
    private static volatile string _bambuHost = "";
    private static volatile string _bambuSerial = "";
    private static volatile string _bambuAccessCode = "";
    private static int _bambuSettingsVersion;
    private static volatile string _afterburnerGpu = "";
    private static volatile NowPlayingPlayer[] _videoPlayers = AppConfig.DefaultVideoPlayers().ToArray();
    private static volatile NowPlayingPlayer[] _musicPlayers = AppConfig.DefaultMusicPlayers().ToArray();

    public static bool StopPriorityTileWhenTruckStops => _stopPriorityTileWhenTruckStops;
    public static string BeamNgOutGaugeAddress => _beamNgOutGaugeAddress;
    public static int BeamNgOutGaugePort => _beamNgOutGaugePort;
    public static bool StopBeamNgTileWhenVehicleStops => _stopBeamNgTileWhenVehicleStops;
    public static bool StopAssettoTileWhenCarStops => _stopAssettoTileWhenCarStops;
    public static bool StopNfsu2TileWhenCarStops => _stopNfsu2TileWhenCarStops;
    public static bool StopNfsMwTileWhenCarStops => _stopNfsMwTileWhenCarStops;
    public static bool StopNfsCarbonTileWhenCarStops => _stopNfsCarbonTileWhenCarStops;
    public static bool KeepNowPlayingWhenPaused => _keepNowPlayingWhenPaused;
    public static bool InlineClockConfigurationPages => _inlineClockConfigurationPages;
    public static bool HideDisconnectedUsbDevices => _hideDisconnectedUsbDevices;
    public static bool HideDisconnectedWifiDevices => _hideDisconnectedWifiDevices;
    public static bool ShowNetworkDeviceRefreshButtons => _showNetworkDeviceRefreshButtons;
    public static string VoicemeeterBus => _voicemeeterBus;
    public static bool VoicemeeterAutoStopOnSilence => _voicemeeterAutoStopOnSilence;
    public static int VoicemeeterSilenceSeconds => _voicemeeterSilenceSeconds;
    public static string ObsHost => _obsHost;
    public static int ObsPort => _obsPort;
    public static string ObsPassword => _obsPassword;
    /// <summary>Moves on whenever the OBS address, port or password change, or a reconnect is asked for; the OBS loop reconnects when it does.</summary>
    public static int ObsSettingsVersion => Volatile.Read(ref _obsSettingsVersion);
    public static void RequestObsReconnect() => Interlocked.Increment(ref _obsSettingsVersion);
    public static string QbitHost => _qbitHost;
    public static int QbitPort => _qbitPort;
    public static bool QbitUseApiKey => _qbitUseApiKey;
    public static string QbitUsername => _qbitUsername;
    public static string QbitPassword => _qbitPassword;
    public static string QbitApiKey => _qbitApiKey;
    /// <summary>Moves on whenever anything the qBittorrent loop signs in with changes, or a reconnect is asked for.</summary>
    public static int QbitSettingsVersion => Volatile.Read(ref _qbitSettingsVersion);
    public static void RequestQbitReconnect() => Interlocked.Increment(ref _qbitSettingsVersion);
    public static string BambuHost => _bambuHost;
    public static string BambuSerial => _bambuSerial;
    public static string BambuAccessCode => _bambuAccessCode;
    public static int BambuSettingsVersion => Volatile.Read(ref _bambuSettingsVersion);
    public static void RequestBambuReconnect() => Interlocked.Increment(ref _bambuSettingsVersion);
    public static string AfterburnerGpu => _afterburnerGpu;
    public static IReadOnlyList<NowPlayingPlayer> VideoPlayers => _videoPlayers;
    public static IReadOnlyList<NowPlayingPlayer> MusicPlayers => _musicPlayers;

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OctoglowSender");

    // Kept internal so off-device regression tests can use an isolated file.
    internal static string FilePath = Path.Combine(ConfigDir, "config.json");
    private static string BackupPath => FilePath + ".bak";
    private static readonly object ConfigIoGate = new();
    // Read and written by generated code rather than by reflection, which a
    // Native AOT build can't do.
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(OnDisk))]
    private sealed partial class OnDiskJson : JsonSerializerContext
    {
    }

    private class OnDisk
    {
        public string Esp32Ip { get; set; } = "";
        public int Esp32Port { get; set; } = 80;
        public string Esp32Id { get; set; } = "";
        public string Esp32Host { get; set; } = "";
        public string ScUser { get; set; } = "";
        public string ScPassEncrypted { get; set; } = "";
        public int PollIntervalSeconds { get; set; } = 3;
        public int SendIntervalSeconds { get; set; } = 10;
        public int NotifMaxChars { get; set; } = 110;
        public int Ets2PollIntervalMs { get; set; } = 500;
        public bool StopPriorityTileWhenTruckStops { get; set; } = true;
        public string BeamNgOutGaugeAddress { get; set; } = BeamNgListenAddress.Default;
        public int BeamNgOutGaugePort { get; set; } = 4444;
        public bool StopBeamNgTileWhenVehicleStops { get; set; } = true;
        public bool StopAssettoTileWhenCarStops { get; set; } = true;
        public bool StopNfsu2TileWhenCarStops { get; set; } = true;
        public bool StopNfsMwTileWhenCarStops { get; set; } = true;
        public bool StopNfsCarbonTileWhenCarStops { get; set; } = true;
        public bool RunAtStartup { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool InlineClockConfigurationPages { get; set; } = false;
        public bool HideDisconnectedUsbDevices { get; set; } = true;
        public bool HideDisconnectedWifiDevices { get; set; } = false;
        public bool ShowNetworkDeviceRefreshButtons { get; set; } = true;
        public int WindowWidth { get; set; } = 1050;
        public int WindowHeight { get; set; } = 790;
        public double ClockPreviewFraction { get; set; } = 0;
        public double MacroPreviewFraction { get; set; } = 0;
        public bool DarkMode { get; set; } = false; // kept only to migrate configs saved before the Theme option existed
        public string? Theme { get; set; }
        public string? BackdropMaterial { get; set; }
        public bool KeepNowPlayingWhenPaused { get; set; } = false;
        public string VoicemeeterBus { get; set; } = "B1";
        public bool VoicemeeterAutoStopOnSilence { get; set; } = true;
        public int VoicemeeterSilenceSeconds { get; set; } = 4;
        public string ObsHost { get; set; } = "127.0.0.1";
        public int ObsPort { get; set; } = 4455;
        public string ObsPasswordEncrypted { get; set; } = "";
        public string QbitHost { get; set; } = "127.0.0.1";
        public int QbitPort { get; set; } = 8080;
        public string QbitAuth { get; set; } = "password";
        public string QbitUsername { get; set; } = "";
        public string QbitPasswordEncrypted { get; set; } = "";
        public string QbitApiKeyEncrypted { get; set; } = "";
        public string BambuHost { get; set; } = "";
        public string BambuSerial { get; set; } = "";
        public string BambuAccessCodeEncrypted { get; set; } = "";
        public string AfterburnerGpu { get; set; } = "";
        public string Language { get; set; } = "system";
        public List<NowPlayingPlayer> VideoPlayers { get; set; } = AppConfig.DefaultVideoPlayers();
        public List<NowPlayingPlayer> MusicPlayers { get; set; } = AppConfig.DefaultMusicPlayers();
    }

    public static AppConfig Load()
    {
        lock (ConfigIoGate) return LoadLocked();
    }

    // A read-modify-write must stay under the same lock. A background device
    // scan and an open Settings page may otherwise overwrite each other's
    // unrelated settings with snapshots taken several seconds apart.
    public static AppConfig Update(Action<AppConfig> change)
    {
        lock (ConfigIoGate)
        {
            var config = LoadLocked();
            change(config);
            SaveLocked(config);
            return config;
        }
    }

    public static bool TryUpdate(Func<AppConfig, bool> change)
    {
        lock (ConfigIoGate)
        {
            var config = LoadLocked();
            if (!change(config)) return false;
            SaveLocked(config);
            return true;
        }
    }

    private static AppConfig LoadLocked()
    {
        var cfg = new AppConfig();
        try
        {
            var onDisk = ReadDiskConfig(FilePath) ?? ReadDiskConfig(BackupPath);
            if (onDisk is null)
            {
                PublishRuntimeConfig(cfg);
                return cfg;
            }

            cfg.Esp32Ip = onDisk.Esp32Ip;
            cfg.Esp32Port = onDisk.Esp32Port;
            cfg.Esp32Id = onDisk.Esp32Id ?? "";
            cfg.Esp32Host = onDisk.Esp32Host ?? "";
            cfg.ScUser = onDisk.ScUser;
            cfg.PollIntervalSeconds = onDisk.PollIntervalSeconds;
            cfg.SendIntervalSeconds = onDisk.SendIntervalSeconds;
            cfg.NotifMaxChars = onDisk.NotifMaxChars;
            cfg.Ets2PollIntervalMs = onDisk.Ets2PollIntervalMs;
            cfg.StopPriorityTileWhenTruckStops = onDisk.StopPriorityTileWhenTruckStops;
            cfg.BeamNgOutGaugeAddress = ValidListenAddress(onDisk.BeamNgOutGaugeAddress);
            cfg.BeamNgOutGaugePort = ValidPort(onDisk.BeamNgOutGaugePort, 4444);
            cfg.StopBeamNgTileWhenVehicleStops = onDisk.StopBeamNgTileWhenVehicleStops;
            cfg.StopAssettoTileWhenCarStops = onDisk.StopAssettoTileWhenCarStops;
            cfg.StopNfsu2TileWhenCarStops = onDisk.StopNfsu2TileWhenCarStops;
            cfg.StopNfsMwTileWhenCarStops = onDisk.StopNfsMwTileWhenCarStops;
            cfg.StopNfsCarbonTileWhenCarStops = onDisk.StopNfsCarbonTileWhenCarStops;
            cfg.RunAtStartup = onDisk.RunAtStartup;
            cfg.MinimizeToTray = onDisk.MinimizeToTray;
            cfg.InlineClockConfigurationPages = onDisk.InlineClockConfigurationPages;
            cfg.HideDisconnectedUsbDevices = onDisk.HideDisconnectedUsbDevices;
            cfg.HideDisconnectedWifiDevices = onDisk.HideDisconnectedWifiDevices;
            cfg.ShowNetworkDeviceRefreshButtons = onDisk.ShowNetworkDeviceRefreshButtons;
            cfg.WindowWidth = Math.Clamp(onDisk.WindowWidth, 900, 3000);
            cfg.WindowHeight = Math.Clamp(onDisk.WindowHeight, 640, 2000);
            cfg.ClockPreviewFraction = onDisk.ClockPreviewFraction is > 0 and <= 0.85
                ? onDisk.ClockPreviewFraction : 0;
            cfg.MacroPreviewFraction = onDisk.MacroPreviewFraction is > 0 and <= 0.85
                ? onDisk.MacroPreviewFraction : 0;
            cfg.Theme = onDisk.Theme switch
            {
                "light" => "light",
                "dark" => "dark",
                "system" => "system",
                _ => onDisk.DarkMode ? "dark" : "system" // migrate configs saved before the Theme option existed
            };
            cfg.BackdropMaterial = onDisk.BackdropMaterial switch
            {
                "none" => "none",
                "mica" => "mica",
                "micaalt" => "micaalt",
                "acrylic" => "acrylic",
                _ => "micaalt" // default / migrate configs saved before this option existed (matches the previous hardcoded backdrop)
            };
            cfg.KeepNowPlayingWhenPaused = onDisk.KeepNowPlayingWhenPaused;
            cfg.VoicemeeterBus = onDisk.VoicemeeterBus is "B2" or "B3" ? onDisk.VoicemeeterBus : "B1";
            cfg.VoicemeeterAutoStopOnSilence = onDisk.VoicemeeterAutoStopOnSilence;
            cfg.VoicemeeterSilenceSeconds = Math.Clamp(onDisk.VoicemeeterSilenceSeconds, 1, 30);
            cfg.Language = onDisk.Language is "en" or "ro" ? onDisk.Language : "system";
            cfg.VideoPlayers = onDisk.VideoPlayers?.Select(player => player.Clone()).ToList() ?? AppConfig.DefaultVideoPlayers();
            cfg.MusicPlayers = onDisk.MusicPlayers?.Select(player => player.Clone()).ToList() ?? AppConfig.DefaultMusicPlayers();
            cfg.ScPass = Decrypt(onDisk.ScPassEncrypted);
            cfg.ObsHost = string.IsNullOrWhiteSpace(onDisk.ObsHost) ? "127.0.0.1" : onDisk.ObsHost.Trim();
            cfg.ObsPort = onDisk.ObsPort is > 0 and <= 65535 ? onDisk.ObsPort : 4455;
            cfg.ObsPassword = Decrypt(onDisk.ObsPasswordEncrypted);
            cfg.QbitHost = string.IsNullOrWhiteSpace(onDisk.QbitHost) ? "127.0.0.1" : onDisk.QbitHost.Trim();
            cfg.QbitPort = ValidPort(onDisk.QbitPort, 8080);
            cfg.QbitAuth = onDisk.QbitAuth == "apikey" ? "apikey" : "password";
            cfg.QbitUsername = onDisk.QbitUsername ?? "";
            cfg.QbitPassword = Decrypt(onDisk.QbitPasswordEncrypted);
            cfg.QbitApiKey = Decrypt(onDisk.QbitApiKeyEncrypted);
            cfg.BambuHost = onDisk.BambuHost?.Trim() ?? "";
            cfg.BambuSerial = onDisk.BambuSerial?.Trim() ?? "";
            cfg.BambuAccessCode = Decrypt(onDisk.BambuAccessCodeEncrypted);
            cfg.AfterburnerGpu = onDisk.AfterburnerGpu?.Trim() ?? "";
        }
        catch
        {

        }
        PublishRuntimeConfig(cfg);
        return cfg;
    }

    private static OnDisk? ReadDiskConfig(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), OnDiskJson.Default.OnDisk)
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void PublishRuntimeConfig(AppConfig cfg)
    {
        _stopPriorityTileWhenTruckStops = cfg.StopPriorityTileWhenTruckStops;
        _beamNgOutGaugeAddress = cfg.BeamNgOutGaugeAddress;
        _beamNgOutGaugePort = cfg.BeamNgOutGaugePort;
        _stopBeamNgTileWhenVehicleStops = cfg.StopBeamNgTileWhenVehicleStops;
        _stopAssettoTileWhenCarStops = cfg.StopAssettoTileWhenCarStops;
        _stopNfsu2TileWhenCarStops = cfg.StopNfsu2TileWhenCarStops;
        _stopNfsMwTileWhenCarStops = cfg.StopNfsMwTileWhenCarStops;
        _stopNfsCarbonTileWhenCarStops = cfg.StopNfsCarbonTileWhenCarStops;
        _keepNowPlayingWhenPaused = cfg.KeepNowPlayingWhenPaused;
        _inlineClockConfigurationPages = cfg.InlineClockConfigurationPages;
        _hideDisconnectedUsbDevices = cfg.HideDisconnectedUsbDevices;
        _hideDisconnectedWifiDevices = cfg.HideDisconnectedWifiDevices;
        _showNetworkDeviceRefreshButtons = cfg.ShowNetworkDeviceRefreshButtons;
        _voicemeeterBus = cfg.VoicemeeterBus;
        _voicemeeterAutoStopOnSilence = cfg.VoicemeeterAutoStopOnSilence;
        _voicemeeterSilenceSeconds = cfg.VoicemeeterSilenceSeconds;
        SetObs(cfg.ObsHost, cfg.ObsPort, cfg.ObsPassword);
        SetQbit(cfg.QbitHost, cfg.QbitPort, cfg.QbitAuth == "apikey", cfg.QbitUsername, cfg.QbitPassword, cfg.QbitApiKey);
        SetBambu(cfg.BambuHost, cfg.BambuSerial, cfg.BambuAccessCode);
        _afterburnerGpu = cfg.AfterburnerGpu;
        _videoPlayers = cfg.VideoPlayers.Select(player => player.Clone()).ToArray();
        _musicPlayers = cfg.MusicPlayers.Select(player => player.Clone()).ToArray();
    }

    public static void Save(AppConfig cfg)
    {
        lock (ConfigIoGate) SaveLocked(cfg);
    }

    private static void SaveLocked(AppConfig cfg)
    {
        _stopPriorityTileWhenTruckStops = cfg.StopPriorityTileWhenTruckStops;
        _beamNgOutGaugeAddress = ValidListenAddress(cfg.BeamNgOutGaugeAddress);
        _beamNgOutGaugePort = ValidPort(cfg.BeamNgOutGaugePort, 4444);
        _stopBeamNgTileWhenVehicleStops = cfg.StopBeamNgTileWhenVehicleStops;
        _stopAssettoTileWhenCarStops = cfg.StopAssettoTileWhenCarStops;
        _stopNfsu2TileWhenCarStops = cfg.StopNfsu2TileWhenCarStops;
        _stopNfsMwTileWhenCarStops = cfg.StopNfsMwTileWhenCarStops;
        _stopNfsCarbonTileWhenCarStops = cfg.StopNfsCarbonTileWhenCarStops;
        _keepNowPlayingWhenPaused = cfg.KeepNowPlayingWhenPaused;
        _inlineClockConfigurationPages = cfg.InlineClockConfigurationPages;
        _hideDisconnectedUsbDevices = cfg.HideDisconnectedUsbDevices;
        _hideDisconnectedWifiDevices = cfg.HideDisconnectedWifiDevices;
        _showNetworkDeviceRefreshButtons = cfg.ShowNetworkDeviceRefreshButtons;
        _voicemeeterBus = cfg.VoicemeeterBus is "B2" or "B3" ? cfg.VoicemeeterBus : "B1";
        _voicemeeterAutoStopOnSilence = cfg.VoicemeeterAutoStopOnSilence;
        _voicemeeterSilenceSeconds = Math.Clamp(cfg.VoicemeeterSilenceSeconds, 1, 30);
        var obsHost = string.IsNullOrWhiteSpace(cfg.ObsHost) ? "127.0.0.1" : cfg.ObsHost.Trim();
        var obsPort = cfg.ObsPort is > 0 and <= 65535 ? cfg.ObsPort : 4455;
        SetObs(obsHost, obsPort, cfg.ObsPassword ?? "");
        var qbitHost = string.IsNullOrWhiteSpace(cfg.QbitHost) ? "127.0.0.1" : cfg.QbitHost.Trim();
        var qbitPort = ValidPort(cfg.QbitPort, 8080);
        var qbitAuth = cfg.QbitAuth == "apikey" ? "apikey" : "password";
        SetQbit(qbitHost, qbitPort, qbitAuth == "apikey", cfg.QbitUsername ?? "", cfg.QbitPassword ?? "", cfg.QbitApiKey ?? "");
        SetBambu(cfg.BambuHost?.Trim() ?? "", cfg.BambuSerial?.Trim() ?? "", cfg.BambuAccessCode ?? "");
        _afterburnerGpu = cfg.AfterburnerGpu?.Trim() ?? "";
        _videoPlayers = cfg.VideoPlayers.Select(player => player.Clone()).ToArray();
        _musicPlayers = cfg.MusicPlayers.Select(player => player.Clone()).ToArray();
        var configDirectory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(configDirectory);
        var onDisk = new OnDisk
        {
            Esp32Ip = cfg.Esp32Ip,
            Esp32Port = cfg.Esp32Port,
            Esp32Id = cfg.Esp32Id,
            Esp32Host = cfg.Esp32Host,
            ScUser = cfg.ScUser,
            PollIntervalSeconds = cfg.PollIntervalSeconds,
            SendIntervalSeconds = cfg.SendIntervalSeconds,
            NotifMaxChars = cfg.NotifMaxChars,
            Ets2PollIntervalMs = cfg.Ets2PollIntervalMs,
            StopPriorityTileWhenTruckStops = cfg.StopPriorityTileWhenTruckStops,
            BeamNgOutGaugeAddress = _beamNgOutGaugeAddress,
            BeamNgOutGaugePort = _beamNgOutGaugePort,
            StopBeamNgTileWhenVehicleStops = cfg.StopBeamNgTileWhenVehicleStops,
            StopAssettoTileWhenCarStops = cfg.StopAssettoTileWhenCarStops,
            StopNfsu2TileWhenCarStops = cfg.StopNfsu2TileWhenCarStops,
            StopNfsMwTileWhenCarStops = cfg.StopNfsMwTileWhenCarStops,
            StopNfsCarbonTileWhenCarStops = cfg.StopNfsCarbonTileWhenCarStops,
            RunAtStartup = cfg.RunAtStartup,
            MinimizeToTray = cfg.MinimizeToTray,
            InlineClockConfigurationPages = cfg.InlineClockConfigurationPages,
            HideDisconnectedUsbDevices = cfg.HideDisconnectedUsbDevices,
            HideDisconnectedWifiDevices = cfg.HideDisconnectedWifiDevices,
            ShowNetworkDeviceRefreshButtons = cfg.ShowNetworkDeviceRefreshButtons,
            WindowWidth = Math.Clamp(cfg.WindowWidth, 900, 3000),
            WindowHeight = Math.Clamp(cfg.WindowHeight, 640, 2000),
            ClockPreviewFraction = cfg.ClockPreviewFraction is > 0 and <= 0.85
                ? cfg.ClockPreviewFraction : 0,
            MacroPreviewFraction = cfg.MacroPreviewFraction is > 0 and <= 0.85
                ? cfg.MacroPreviewFraction : 0,
            Theme = cfg.Theme,
            DarkMode = cfg.Theme == "dark", // kept in sync for backward compatibility with older builds
            BackdropMaterial = cfg.BackdropMaterial,
            KeepNowPlayingWhenPaused = cfg.KeepNowPlayingWhenPaused,
            VoicemeeterBus = _voicemeeterBus,
            VoicemeeterAutoStopOnSilence = _voicemeeterAutoStopOnSilence,
            VoicemeeterSilenceSeconds = _voicemeeterSilenceSeconds,
            ObsHost = obsHost,
            ObsPort = obsPort,
            ObsPasswordEncrypted = Encrypt(cfg.ObsPassword ?? ""),
            QbitHost = qbitHost,
            QbitPort = qbitPort,
            QbitAuth = qbitAuth,
            QbitUsername = cfg.QbitUsername ?? "",
            QbitPasswordEncrypted = Encrypt(cfg.QbitPassword ?? ""),
            QbitApiKeyEncrypted = Encrypt(cfg.QbitApiKey ?? ""),
            BambuHost = _bambuHost,
            BambuSerial = _bambuSerial,
            BambuAccessCodeEncrypted = Encrypt(_bambuAccessCode),
            AfterburnerGpu = _afterburnerGpu,
            Language = cfg.Language,
            VideoPlayers = cfg.VideoPlayers.Select(player => player.Clone()).ToList(),
            MusicPlayers = cfg.MusicPlayers.Select(player => player.Clone()).ToList(),
            ScPassEncrypted = Encrypt(cfg.ScPass),
        };
        var json = JsonSerializer.Serialize(onDisk, OnDiskJson.Default.OnDisk);
        // A scan can read settings while the UI saves them. Replace the whole
        // file at once so it never mistakes half-written JSON for defaults.
        lock (ConfigIoGate)
        {
            var temporary = Path.Combine(configDirectory, $"config.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporary, json);
                if (ReadDiskConfig(FilePath) is not null)
                {
                    var backupTemporary = BackupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(FilePath, backupTemporary);
                        File.Move(backupTemporary, BackupPath, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(backupTemporary)) File.Delete(backupTemporary);
                    }
                }
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
    private static int ValidPort(int port, int fallback) => port is >= 1 and <= 65535 ? port : fallback;

    private static string ValidListenAddress(string? address) =>
        BeamNgListenAddress.TryParse(address, out var parsed) ? parsed.ToString() : BeamNgListenAddress.Default;

    // The OBS loop is told to reconnect only when something it uses changed.
    private static void SetObs(string host, int port, string password)
    {
        if (_obsHost == host && _obsPort == port && _obsPassword == password) return;
        _obsHost = host;
        _obsPort = port;
        _obsPassword = password;
        RequestObsReconnect();
    }

    // The qBittorrent loop signs in again only when something it signs in with changed.
    private static void SetQbit(string host, int port, bool useApiKey, string username, string password, string apiKey)
    {
        if (_qbitHost == host && _qbitPort == port && _qbitUseApiKey == useApiKey && _qbitUsername == username &&
            _qbitPassword == password && _qbitApiKey == apiKey) return;
        _qbitHost = host;
        _qbitPort = port;
        _qbitUseApiKey = useApiKey;
        _qbitUsername = username;
        _qbitPassword = password;
        _qbitApiKey = apiKey;
        RequestQbitReconnect();
    }

    private static void SetBambu(string host, string serial, string accessCode)
    {
        if (_bambuHost == host && _bambuSerial == serial && _bambuAccessCode == accessCode) return;
        _bambuHost = host;
        _bambuSerial = serial;
        _bambuAccessCode = accessCode;
        RequestBambuReconnect();
    }

    private static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            var bytes = Convert.FromBase64String(stored);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return ""; // couldn't decrypt (moved to another PC/user) - don't crash, just ask again
        }
    }
}
