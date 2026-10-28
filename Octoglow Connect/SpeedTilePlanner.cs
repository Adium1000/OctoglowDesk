namespace OctoglowSender;

internal enum SpeedSendKind { Speed, Stop }

// Mph is -1 unless the game gives it itself (NFS Underground 2): the clock
// works mph out of the km/h otherwise.
internal readonly record struct SpeedSend(SpeedSendKind Kind, int Kmh, int Mph = -1)
{
    public static SpeedSend Speed(int kmh, int mph = -1) => new(SpeedSendKind.Speed, kmh, mph);
    public static readonly SpeedSend Stop = new(SpeedSendKind.Stop, 0);
}

// What goes to the clock for a game's speed tile (BeamNG.drive, Assetto
// Corsa, NFS Underground 2, Most Wanted and Carbon), from the speeds the game gave: whole km/h at most
// four times a second while the speed changes, again every two seconds while it doesn't
// (the clock drops the tile after five seconds of silence), and a stop once the
// game has said nothing for two seconds - or at once when it is known to have
// stopped, or while the vehicle stands still when the integration is set so.
// However fast the game goes (BeamNG can send 60 packets a second, Assetto
// Corsa steps its physics hundreds of times), that is all the clock hears.
internal sealed class SpeedTilePlanner
{
    public static readonly TimeSpan PacketsStaleAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan KeepaliveEvery = TimeSpan.FromSeconds(2);

    private DateTime _lastPacketAt = DateTime.MinValue;
    private int _latestKmh;
    private int _latestMph = -1;
    private int? _sentKmh;   // null: the tile is not up on the clock
    private int _sentMph = -1;
    private DateTime _sentAt = DateTime.MinValue;

    public int LatestKmh => _latestKmh;

    public void OnSpeed(int kmh, DateTime now, int mph = -1)
    {
        _latestKmh = Math.Clamp(kmh, 0, 999);
        _latestMph = mph < 0 ? -1 : Math.Min(mph, 999);
        _lastPacketAt = now;
    }

    // The game has stopped - paused, left the session, closed: no waiting for
    // its silence to go stale.
    public void Silence() => _lastPacketAt = DateTime.MinValue;

    public bool Receiving(DateTime now) => now - _lastPacketAt <= PacketsStaleAfter;

    // Null when there is nothing to send now.
    public SpeedSend? Next(DateTime now, bool stopWhenStationary)
    {
        var showing = Receiving(now) && !(stopWhenStationary && _latestKmh == 0);
        if (!showing) return _sentKmh is null ? null : SpeedSend.Stop;
        var due = _sentKmh == _latestKmh && _sentMph == _latestMph ? KeepaliveEvery : MinInterval;
        return _sentKmh is null || now - _sentAt >= due ? SpeedSend.Speed(_latestKmh, _latestMph) : null;
    }

    public void Sent(SpeedSend send, DateTime now)
    {
        _sentKmh = send.Kind == SpeedSendKind.Stop ? null : send.Kmh;
        _sentMph = send.Kind == SpeedSendKind.Stop ? -1 : send.Mph;
        _sentAt = now;
    }

    // Whether the tile is up on the clock, as far as what was sent goes.
    public bool Showing => _sentKmh is not null;

    // The clock has the tile switched off, or no tile at all: nothing is up there.
    public void Forget() => _sentKmh = null;
}
