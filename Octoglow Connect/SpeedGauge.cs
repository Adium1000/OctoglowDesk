namespace OctoglowSender;

// The range the clock takes for where a speed gauge's scale ends
// (SPEED_GAUGE_FULL_MIN / _MAX in Octoglow.ino). How a gauge looks is drawn by
// the clock only; a speed tile's settings show it through their Preview.
internal static class SpeedGauge
{
    public const int FullMinimum = 40;
    public const int FullMaximum = 500;
}
