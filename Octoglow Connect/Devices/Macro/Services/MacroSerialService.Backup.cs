using OctoglowSender.Devices.Macro.Models;

namespace OctoglowSender.Devices.Macro.Services;

internal sealed partial class MacroSerialService
{
    internal bool SupportsBackupRestore => Volatile.Read(ref _protocolLevel) >= 12;

    internal async Task<MacroBackupDocument> ExportBackupAsync(string deviceKey)
    {
        var lines = await CommandAsync("DUMP", _stop.Token);
        MacroConfigurationParser.Parse(lines);
        return new MacroBackupDocument { DeviceKey = deviceKey, DeviceConfiguration = lines.ToList(),
            FirmwareVersion = FirmwareVersion, Host = MacroHostStore.Snapshot(), PreviewFraction = ConfigStore.Load().MacroPreviewFraction };
    }

    internal async Task<MacroConfiguration> RestoreBackupAsync(MacroBackupDocument backup, string deviceKey, string recoveryPath)
    {
        // Validate again at the write boundary, even when the caller already displayed a preview.
        backup = MacroBackup.Decode(MacroBackup.Encode(backup));
        var requested = MacroBackup.PrepareRestore(backup);
        if (!SupportsBackupRestore) throw new InvalidOperationException(AppStrings.IsEnglish ?
            "Install the new Macro 0.2 firmware with safe backup restore support first." :
            "Instalează mai întâi noul firmware Macro 0.2 cu suport pentru restaurare sigură.");
        await _commandGate.WaitAsync(_stop.Token);
        _restoring = true;
        var began = false;
        var commitAttempted = false;
        var hostChanged = false;
        MacroHostSettings? previousHost = null;
        var previousPreview = ConfigStore.Load().MacroPreviewFraction;
        try
        {
            var previousLines = await CommandCoreAsync("DUMP", _stop.Token);
            var previousConfig = MacroConfigurationParser.Parse(previousLines);
            if (previousConfig.HasEncoder != requested.HasEncoder)
                throw new InvalidDataException(AppStrings.IsEnglish ? "This backup is for a different Macro layout." : "Backup-ul aparține unui Macro cu altă configurație fizică de taste.");
            previousHost = MacroHostStore.Snapshot();
            var recovery = new MacroBackupDocument { DeviceKey = deviceKey, FirmwareVersion = FirmwareVersion,
                DeviceConfiguration = previousLines.ToList(), Host = previousHost, PreviewFraction = previousPreview };
            Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath)!);
            // Do not touch the device if the recovery copy cannot be saved.
            await File.WriteAllTextAsync(recoveryPath, MacroBackup.Encode(recovery), _stop.Token);
            await CommandCoreAsync("BEGINCFG", _stop.Token);
            began = true;
            foreach (var command in MacroBackup.RestoreCommands(requested))
                await CommandCoreAsync(command, _stop.Token);
            var verified = MacroConfigurationParser.Parse(await CommandCoreAsync("DUMP", _stop.Token));
            if (!MacroBackup.RestoreCommands(requested).SequenceEqual(MacroBackup.RestoreCommands(verified)))
                throw new InvalidDataException("The staged configuration did not match the backup.");
            // Keep legacy per-device overrides usable when restoring onto a replacement board.
            if (backup.DeviceKey.Length != 0 && deviceKey.Length != 0 && backup.DeviceKey != deviceKey &&
                backup.Host.ToneNames.TryGetValue(backup.DeviceKey, out var names))
                backup.Host.ToneNames[deviceKey] = (string?[])names.Clone();
            MacroHostStore.Restore(backup.Host);
            hostChanged = true;
            ConfigStore.Update(config => config.MacroPreviewFraction = backup.PreviewFraction);
            commitAttempted = true;
            await CommandCoreAsync("COMMITCFG", _stop.Token);
            return verified;
        }
        catch (Exception error)
        {
            if (commitAttempted)
                throw new IOException((AppStrings.IsEnglish ?
                    "The final save could not be confirmed. Reconnect Macro and check its settings before retrying. Recovery backup: " :
                    "Salvarea finală nu a putut fi confirmată. Reconectează Macro și verifică setările înainte să reîncerci. Backup de recuperare: ") + recoveryPath, error);
            if (hostChanged && previousHost is not null)
            {
                MacroHostStore.Restore(previousHost);
                ConfigStore.Update(config => config.MacroPreviewFraction = previousPreview);
            }
            if (began)
            {
                try { await CommandCoreAsync("ABORTCFG", _stop.Token); }
                catch { /* Firmware rolls back automatically after 30 seconds or a power cycle. */ }
            }
            throw;
        }
        finally { _restoring = false; _commandGate.Release(); }
    }
}
