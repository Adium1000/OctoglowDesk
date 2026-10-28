using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using OctoglowSender.Devices.Macro.Services;
namespace OctoglowSender;
public partial class App : Application
{
    private const string InstanceMutexName = @"Local\OctoglowConnect.SingleInstance";
    private const string ActivationEventName = @"Local\OctoglowConnect.ActivateExisting";
    public static Window? MainWindow { get; private set; }
    public static DeviceHubService DeviceHub { get; private set; } = null!;
    private TrayService? _trayService;
    private NativeWindowMessageRouter? _messageRouter;
    private CancellationTokenSource? _memoryReductionCts;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationListenerCts;
    private bool _isExiting;
    private bool _isCleanedUp;
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!ClaimSingleInstance()) return;

        DeviceHub = new DeviceHubService();
        MainWindow = new MainWindow();
        ListenForSecondLaunch();
        AppLog.Initialize(MainWindow.DispatcherQueue);
        var config = ConfigStore.Load();
        ThemeService.Apply(config.Theme);
        ThemeService.ApplyBackdrop(config.BackdropMaterial);
        StartupService.SetRunAtStartup(config.RunAtStartup);
        // Pentru aplicații neîmpachetate (unpackaged), WinUI 3 nu populează fiabil
        // LaunchActivatedEventArgs.Arguments la lansarea prin linia de comandă (ex: din
        // cheia de Run din registry), așa că citim argumentele direct din linia de comandă.
        var launchedAtStartup = config.RunAtStartup &&
            Environment.GetCommandLineArgs().Skip(1).Any(a => string.Equals(a.Trim(), "--startup", StringComparison.OrdinalIgnoreCase));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "octoglow_logo.ico");
        if (File.Exists(iconPath))
            MainWindow.AppWindow.SetIcon(iconPath);
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        try
        {
            _messageRouter = new NativeWindowMessageRouter(windowHandle);
            _trayService = new TrayService(windowHandle, _messageRouter, ShowMainWindow, ExitApplication, iconPath);
            MacropadInputBus.Initialize(windowHandle, _messageRouter);
        }
        catch (Exception error)
        {
            AppLog.Write($"[WINDOW WARN] Serviciile native nu au putut fi inițializate: {error.Message}");
        }

        // Macro's CDC channel can still work if raw HID/tray setup fails.
        MacroSerialBus.Instance.Start();
        DeviceHub.Start();
        BrowserDownloadsBridge.Start();
        MainWindow.AppWindow.Closing += (_, args) =>
        {
            if (_trayService is not null && !_isExiting && ConfigStore.Load().MinimizeToTray)
            {
                args.Cancel = true;
                _trayService.Show();
                MainWindow.AppWindow.Hide();
                StartTrayMemoryReduction();
                return;
            }

            CleanupServices();
        };
        MainWindow.Closed += (_, _) => CleanupServices();
        MainWindow.Activate();

        if (launchedAtStartup && _trayService is not null)
        {
            _trayService.Show();
            MainWindow.AppWindow.Hide();
            StartTrayMemoryReduction();
        }

        if (!string.IsNullOrWhiteSpace(config.Esp32Ip) &&
            !string.IsNullOrWhiteSpace(config.ScUser) &&
            !string.IsNullOrWhiteSpace(config.ScPass))
        {
            DeskClockSession.Start(config);
        }
    }

    private bool ClaimSingleInstance()
    {
        // The event is created before taking the mutex: a second launch can
        // always signal the first, even while its window is still starting.
        var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        var mutex = new Mutex(false, InstanceMutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }

        if (!acquired)
        {
            activationEvent.Set();
            activationEvent.Dispose();
            mutex.Dispose();
            Exit();
            return false;
        }

        _activationEvent = activationEvent;
        _instanceMutex = mutex;
        return true;
    }

    private void ListenForSecondLaunch()
    {
        var dispatcher = MainWindow!.DispatcherQueue;
        var activationEvent = _activationEvent!;
        _activationListenerCts = new CancellationTokenSource();
        var stop = _activationListenerCts.Token;
        _ = Task.Run(() =>
        {
            try
            {
                var handles = new WaitHandle[] { activationEvent, stop.WaitHandle };
                while (WaitHandle.WaitAny(handles) == 0 && !stop.IsCancellationRequested)
                    dispatcher.TryEnqueue(ShowMainWindow);
            }
            catch (ObjectDisposedException)
            {
                // Normal shutdown can dispose the event while the wait ends.
            }
        });
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null || _isCleanedUp) return;
        StopTrayMemoryReduction();
        _trayService?.Hide();
        MainWindow.AppWindow.Show();
        MainWindow.Activate();
        PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(MainWindow), WM_NCMOUSELEAVE, nint.Zero, nint.Zero);
    }

    private void StartTrayMemoryReduction()
    {
        _memoryReductionCts?.Cancel();
        _memoryReductionCts?.Dispose();
        _memoryReductionCts = new CancellationTokenSource();
        _ = ReduceMemoryFootprintAsync(_memoryReductionCts.Token);
    }

    private async Task ReduceMemoryFootprintAsync(CancellationToken token)
    {
        try
        {
            if (MainWindow is OctoglowSender.MainWindow window)
                await window.EnterTrayLowMemoryModeAsync();

            await Task.Delay(250, token);
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
                GC.WaitForPendingFinalizers();
                token.ThrowIfCancellationRequested();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);

                using var process = Process.GetCurrentProcess();
                EmptyWorkingSet(process.Handle);
            }, token);
        }
        catch (OperationCanceledException)
        {
            // The app was reopened before the delayed trim ran.
        }
    }

    private void StopTrayMemoryReduction()
    {
        var cancellation = Interlocked.Exchange(ref _memoryReductionCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();

        if (MainWindow is OctoglowSender.MainWindow window)
            window.ExitTrayLowMemoryMode();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        MainWindow?.Close();
    }

    private void CleanupServices()
    {
        if (_isCleanedUp) return;
        _isCleanedUp = true;
        StopTrayMemoryReduction();
        DeskClockSession.Stop();
        BrowserDownloadsBridge.Stop();
        DeviceHub.Dispose();
        MacropadInputBus.Shutdown();
        MacroSerialBus.Instance.Dispose();
        _trayService?.Dispose();
        _messageRouter?.Dispose();
        _activationListenerCts?.Cancel();
        _activationEvent?.Set();
        _activationEvent?.Dispose();
        _activationListenerCts?.Dispose();
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }
    }
    private const uint WM_NCMOUSELEAVE = 0x02A2;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(nint process);

}
