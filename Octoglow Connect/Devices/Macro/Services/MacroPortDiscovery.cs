using Microsoft.Win32;

namespace OctoglowSender.Devices.Macro.Services;

// A composite Macro exposes HID and CDC on the same physical USB device.
// Match their Windows container IDs so we never ask the user to guess a COM port.
internal static class MacroPortDiscovery
{
    internal static IReadOnlyList<string> FindPorts(out bool macroPresent)
    {
        var devices = MacropadIdentity.FindAll();
        macroPresent = devices.Count > 0;
        if (!macroPresent) return [];

        var containers = new HashSet<Guid>();
        foreach (var device in devices)
        {
            var pathParts = device.Path.Split('#');
            if (pathParts.Length < 3) continue;
            using var hidNode = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\HID\{pathParts[1]}\{pathParts[2]}");
            if (Guid.TryParse(hidNode?.GetValue("ContainerID")?.ToString(), out var container))
                containers.Add(container);
        }

        var available = MacroSerialService.AvailablePorts().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (available.Count == 0) return [];
        using var usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usbRoot is null) return [];

        var identities = devices.Select(device => $"VID_{device.VendorId:X4}&PID_{device.ProductId:X4}")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = new List<(string Port, bool SameDevice)>();
        foreach (var interfaceName in usbRoot.GetSubKeyNames())
        {
            if (!identities.Any(id => interfaceName.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                    interfaceName.StartsWith(id + "&", StringComparison.OrdinalIgnoreCase))) continue;
            using var usbInterface = usbRoot.OpenSubKey(interfaceName);
            if (usbInterface is null) continue;
            foreach (var instanceName in usbInterface.GetSubKeyNames())
            {
                using var instance = usbInterface.OpenSubKey(instanceName);
                using var parameters = instance?.OpenSubKey("Device Parameters");
                var port = parameters?.GetValue("PortName") as string;
                if (port is null || !available.Contains(port)) continue;
                var sameDevice = Guid.TryParse(instance?.GetValue("ContainerID")?.ToString(), out var id) &&
                    containers.Contains(id);
                matches.Add((port, sameDevice));
            }
        }

        // Container IDs are the exact physical match. If an unusual driver
        // omits them, fall back only to ports with the Macro's VID/PID; HELLO
        // still verifies the firmware before any settings are sent.
        var exact = matches.Where(match => match.SameDevice).Select(match => match.Port)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(port => port, StringComparer.OrdinalIgnoreCase).ToArray();
        return exact.Length > 0 ? exact : matches.Select(match => match.Port)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(port => port, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
