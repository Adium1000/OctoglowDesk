namespace OctoglowSender;

public enum ConnectionStatusKind { Disconnected, Connecting, Connected, Failed }

// Found on the network and signed in to are two different things, and the pill,
// Home and the sidebar all draw the line in the same place.
public static class DeviceState
{
    public static bool IsSignedIn(DeviceDescriptor? device) =>
        device is { Kind: DeviceKind.DeskClock } &&
        ConnectionStatus.IsConnected &&
        DeskClockSession.Matches(device.Address ?? "", device.Port is > 0 and <= 65535 ? device.Port : 80);

    public static string StatusText(DeviceDescriptor device)
    {
        if (!device.IsOnline) return AppStrings.Get("device.disconnected");
        if (device.Kind == DeviceKind.Macro || IsSignedIn(device))
            return AppStrings.Get("device.connected");
        if (!DeskClockSession.Matches(device.Address ?? "", device.Port))
            return AppStrings.Get("device.disconnected");
        return ConnectionStatus.Current switch
        {
            ConnectionStatusKind.Connecting => AppStrings.Get("connection.connecting"),
            ConnectionStatusKind.Failed => AppStrings.Get("connection.failed"),
            _ => AppStrings.Get("device.disconnected"),
        };
    }
}

public static class ConnectionStatus
{
    public static ConnectionStatusKind Current { get; private set; } = ConnectionStatusKind.Disconnected;
    public static bool IsConnected => Current == ConnectionStatusKind.Connected;
    public static event EventHandler? Changed;

    public static void Set(ConnectionStatusKind status)
    {
        if (Current == status) return;
        Current = status;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void ProcessBackendMessage(string message)
    {
        if (!message.StartsWith("[AUTH]", StringComparison.OrdinalIgnoreCase)) return;
        var value = message.ToUpperInvariant();
        if (value.Contains("[AUTH] AUTENTIFICAT CU SUCCES") || value.Contains("[AUTH] AUTHENTICATED"))
        {
            Set(ConnectionStatusKind.Connected);
        }
        else if (value.Contains("[AUTH]") &&
                 (value.Contains("INCORECT") ||
                  // The clock's login closed after wrong passwords, or no account filled in.
                  value.Contains("BLOCAT") || value.Contains("DATE DE CONECTARE") ||
                  (value.Contains("LOGIN") && (value.Contains("EȘUAT") || value.Contains("EÈ˜UAT") || value.Contains("FAILED")))))
        {
            // Individual timeouts and connection errors are retryable. Do not
            // flash "Disconnected" while the backend is still retrying.
            Set(ConnectionStatusKind.Failed);
        }
    }
}
