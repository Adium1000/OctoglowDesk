using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OctoglowSender.Devices.Macro.Services;
using Windows.Storage.Pickers;

namespace OctoglowSender.Devices.Macro;

public sealed partial class MacroDevicePage
{
    private void AddBackupControls(StackPanel panel)
    {
        panel.Children.Add(Heading(M("Copie de siguranță", "Configuration backup")));
        var export = new Button { Content = M("Exportă", "Export"), IsEnabled = MacroSerialBus.Instance.IsConnected };
        export.Click += async (_, _) => await ExportMacroBackupAsync();
        panel.Children.Add(SettingCard("\uE896", M("Exportă toate setările", "Export all settings"),
            M("Taste, taburi, iluminare, tonuri, aplicații și legături cu dispozitive. Fără parolele ceasului.",
                "Keys, tabs, lighting, tones, applications and device bindings. No clock passwords."), export));
        var import = new Button { Content = M("Importă", "Import"), IsEnabled = MacroSerialBus.Instance.IsConnected && MacroSerialBus.Instance.SupportsBackupRestore };
        import.Click += async (_, _) => await ImportMacroBackupAsync();
        panel.Children.Add(SettingCard("\uE898", M("Încarcă o copie de siguranță", "Restore a backup"),
            MacroSerialBus.Instance.SupportsBackupRestore
                ? M("Înlocuiește setările Macro și cele externe. Se salvează automat o copie înainte de import.",
                    "Replaces Macro and host settings. A recovery copy is saved automatically first.")
                : M("Necesită noul firmware 0.2 cu restaurare sigură. Exportul funcționează și cu firmware mai vechi.",
                    "Requires the new 0.2 firmware with safe restore support. Export also works with older firmware."), import));
    }

    private async Task ExportMacroBackupAsync()
    {
        CloseMacroPanel(animate: false);
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"Octoglow-Macro-{DateTime.Now:yyyyMMdd-HHmmss}" };
            picker.FileTypeChoices.Add(M("Backup Octoglow", "Octoglow backup"), new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            var backup = await MacroSerialBus.Instance.ExportBackupAsync(_toneDeviceKey);
            await File.WriteAllTextAsync(file.Path, MacroBackup.Encode(backup));
            ShowMacroMessage(InfoBarSeverity.Success, M("Backup exportat. Păstrează fișierul într-un loc sigur: conține căile aplicațiilor.",
                "Backup exported. Keep it in a safe place: it contains application paths."));
        }
        catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, M("Exportul a eșuat: ", "Export failed: ") + error.Message); }
    }

    private async Task ImportMacroBackupAsync()
    {
        CloseMacroPanel(animate: false);
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            if (new FileInfo(file.Path).Length > MacroBackup.MaxBytes)
                throw new InvalidDataException(M("Fișierul este prea mare.", "The file is too large."));
            var backup = MacroBackup.Decode(await File.ReadAllTextAsync(file.Path));
            var confirm = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                Title = M("Înlocuiești configurația Macro?", "Replace the Macro configuration?"),
                Content = M($"Vor fi înlocuite toate setările de pe placă și cele Macro din Connect, inclusiv {backup.Host.Applications.Count} acțiuni externe. Importă doar fișiere de încredere: tastele vor putea deschide aplicațiile din backup și controla ceasul dacă integrarea este activată. Parolele și conturile Clock nu se importă. Se păstrează automat un backup al configurației actuale.",
                    $"All board and Macro host settings will be replaced, including {backup.Host.Applications.Count} external actions. Only import trusted files: keys can launch the backed-up applications and control the clock if integration is enabled. Clock passwords and accounts are not imported. A recovery copy of the current configuration will be kept automatically."),
                PrimaryButtonText = M("Importă și înlocuiește", "Import and replace"), CloseButtonText = M("Anulează", "Cancel"),
                DefaultButton = ContentDialogButton.Close };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            IsEnabled = false;
            ShowMacroMessage(InfoBarSeverity.Informational, M("Se restaurează configurația. Nu deconecta Macro.", "Restoring configuration. Do not unplug Macro."));
            var recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OctoglowSender", "Backups", $"Macro-before-import-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            _configuration = await MacroSerialBus.Instance.RestoreBackupAsync(backup, _toneDeviceKey, recovery);
            _selectedTab = Math.Min(_selectedTab, _configuration.TabCount);
            _previewFraction = backup.PreviewFraction;
            FitLayout();
            if (_isLoaded) { RenderMacroSection(); UpdateLedPreview(); }
            ShowMacroMessage(InfoBarSeverity.Success, M("Setările au fost restaurate. Backup anterior: ", "Settings restored. Recovery backup: ") + recovery);
        }
        catch (Exception error) { ShowMacroMessage(InfoBarSeverity.Error, M("Importul nu s-a finalizat: ", "Import did not complete: ") + error.Message); }
        finally { IsEnabled = true; }
    }
}
