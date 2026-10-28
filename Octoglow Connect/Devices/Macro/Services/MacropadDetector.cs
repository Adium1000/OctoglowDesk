namespace OctoglowSender.Devices.Macro.Services;

/// <summary>
/// Monitorizează dacă macropad-ul RP2040 Zero este conectat
/// la USB, prin polling periodic (fallback simplu, independent de Raw Input).
/// Identificarea device-ului (Product String = "RP2040 Macropad") e centralizată
/// în <see cref="MacropadIdentity"/>.
/// Detecția prezenței folosește HID. Firmware-ul nou expune separat un port
/// USB CDC pentru configurare; lipsa canalului CDC nu ascunde dispozitivul.
/// </summary>
public sealed class MacropadDetector : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;

    /// <summary>Se declanșează pe thread-ul de timer (nu pe UI thread) când starea se schimbă.</summary>
    public event EventHandler<bool>? ConnectionChanged;

    public MacropadDetector(int pollIntervalMs = 1500)
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, pollIntervalMs);
    }

    private void Poll()
    {
        bool found;
        try
        {
            found = MacropadIdentity.FindAll().Count > 0;
        }
        catch
        {
            // Dacă enumerarea eșuează dintr-un motiv oarecare, nu dărâmăm aplicația.
            found = false;
        }

        if (found != _isConnected)
        {
            _isConnected = found;
            ConnectionChanged?.Invoke(this, found);
        }
    }

    /// <summary>Forțează o verificare imediată (util la pornirea aplicației, fără să aștepți primul tick).</summary>
    public bool CheckNow()
    {
        Poll();
        return _isConnected;
    }

    public void Dispose() => _timer.Dispose();
}
