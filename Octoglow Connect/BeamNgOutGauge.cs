using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OctoglowSender;

// BeamNG.drive's OutGauge UDP packet (documentation.beamng.com/modding/protocols,
// "OutGauge UDP protocol"): the Live for Speed layout, 92 bytes, or 96 when the
// game is given an OutGauge ID. BeamNG always sends time as 0 - nothing here
// reads it - and speed in metres per second.
//
//   0  unsigned time          24 float engTemp       56 float clutch
//   4  char     car[4]        28 float fuel          60 char  display1[16]
//   8  ushort   flags         32 float oilPressure   76 char  display2[16]
//  10  char     gear          36 float oilTemp       92 int   id (optional)
//  11  char     plid          40 unsigned dashLights
//  12  float    speed         44 unsigned showLights
//  16  float    rpm           48 float throttle
//  20  float    turbo         52 float brake
internal readonly record struct OutGaugePacket(float SpeedMs, int Gear, int? Id)
{
    public const int Size = 92;
    public const int SizeWithId = 96;
    private const int GearOffset = 10;
    private const int SpeedOffset = 12;
    private const int RpmOffset = 16;
    private const int IdOffset = 92;
    // Faster than anything in the game drives (1800 km/h): past it, or not a
    // number at all, the packet is not a speed worth showing.
    private const float MaxSpeedMs = 500f;

    public static bool TryParse(ReadOnlySpan<byte> data, out OutGaugePacket packet)
    {
        packet = default;
        if (data.Length != Size && data.Length != SizeWithId) return false;
        var speed = BinaryPrimitives.ReadSingleLittleEndian(data[SpeedOffset..]);
        var rpm = BinaryPrimitives.ReadSingleLittleEndian(data[RpmOffset..]);
        if (!float.IsFinite(speed) || !float.IsFinite(rpm) || Math.Abs(speed) > MaxSpeedMs) return false;
        int? id = data.Length == SizeWithId ? BinaryPrimitives.ReadInt32LittleEndian(data[IdOffset..]) : null;
        packet = new OutGaugePacket(Math.Abs(speed), data[GearOffset], id);
        return true;
    }

    // Whole km/h, halves away from zero: what the clock is sent. The clock
    // works out mph from it when it is set to show mph.
    public int SpeedKmh => (int)Math.Round(SpeedMs * 3.6, MidpointRounding.AwayFromZero);
}

// What the clock is sent from the packets is SpeedTilePlanner's to decide.

// Where OutGauge is listened for: 127.0.0.1 when the game runs on this
// computer; this computer's network address, or 0.0.0.0 for every one of them,
// when it runs on another. Only an address this computer has can be listened
// on, so anything else is refused when it is typed rather than failing later.
internal static class BeamNgListenAddress
{
    public const string Default = "127.0.0.1";

    // IPv4 written out in full: "127.1" or "1" would parse, but aren't what
    // anyone means.
    public static bool TryParse(string? text, out IPAddress address)
    {
        address = IPAddress.Loopback;
        var trimmed = (text ?? "").Trim();
        if (!IPAddress.TryParse(trimmed, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork ||
            parsed.ToString() != trimmed)
            return false;
        address = parsed;
        return true;
    }

    public static bool IsLocal(IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || IPAddress.IsLoopback(address)) return true;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Any(unicast => unicast.Address.Equals(address));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }
}

// What the Integrations tab shows: whether packets are arriving, on which port.
internal static class BeamNgStatus
{
    private static volatile bool _receiving;
    private static volatile int _port;
    private static volatile string? _problem;

    public static bool Receiving => _receiving;
    public static int Port => _port;
    public static string? Problem => _problem;

    public static void Set(bool receiving, int port, string? problem)
    {
        _receiving = receiving;
        _port = port;
        _problem = problem;
    }
}
