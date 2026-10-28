namespace OctoglowSender;

// The clock's transitions and scroll types as frames, for the preview behind
// every transition and scroll type picker: playTileTransition() and the tiles'
// scroll states of Octoglow.ino in C#. The web interface has the same in
// JavaScript (portal.html, "EFFECT PREVIEW"); an effect changed in the firmware
// has to change in all three.
internal static class ClockEffects
{
    // A frame of the panel (32 columns, bit y of each lit in row y, the
    // leftmost first), how long it stays, and how bright the panel is (0..1).
    internal readonly record struct Frame(byte[] Columns, double Milliseconds, double Level = 1);

    private static readonly byte[,] Bayer =
    {
        { 0, 32, 8, 40, 2, 34, 10, 42 },
        { 48, 16, 56, 24, 50, 18, 58, 26 },
        { 12, 44, 4, 36, 14, 46, 6, 38 },
        { 60, 28, 52, 20, 62, 30, 54, 22 },
        { 3, 35, 11, 43, 1, 33, 9, 41 },
        { 51, 19, 59, 27, 49, 17, 57, 25 },
        { 15, 47, 7, 39, 13, 45, 5, 37 },
        { 63, 31, 55, 23, 61, 29, 53, 21 },
    };

    // An 8x8 icon (rows, MSB the left pixel, as ICON_CATALOG has them) as panel columns.
    internal static byte[] IconColumns(byte[] rows)
    {
        var columns = new byte[8];
        for (var x = 0; x < 8; x++)
            for (var y = 0; y < 8; y++)
                if ((rows[y] & (0x80 >> x)) != 0) columns[x] |= (byte)(1 << y);
        return columns;
    }

    // A tile as the clock lays it out: the icon, a dark column, the text in the
    // middle of what is left.
    internal static byte[] Tile(byte[] icon, byte[] text)
    {
        var columns = new byte[32];
        Array.Copy(icon, columns, Math.Min(8, icon.Length));
        var lead = Math.Max(0, (23 - text.Length) / 2);
        for (var x = 0; x < 23; x++)
            if (x - lead >= 0 && x - lead < text.Length) columns[9 + x] = text[x - lead];
        return columns;
    }

    // easedStepDelay(): slower near the two ends of a move.
    private static int Eased(int step, int steps, int baseDelay, int extraDelay, int zone)
    {
        var fromEdge = Math.Min(step, steps - step);
        return fromEdge >= zone ? baseDelay : baseDelay + extraDelay * (zone - fromEdge) / zone;
    }

    // From the old tile to the new one with the effect (0 none, 1-2 scroll
    // left/right, 3-4 up/down, 5 morph, 6 fade, 7-9 expand left/right/centre),
    // at `pace` percent of the pace it was drawn at. The clock counts columns
    // from the right, so the tiles are turned round going in and the frames
    // coming out.
    internal static List<Frame> Transition(int effect, byte[] oldColumns, byte[] newColumns, int pace)
    {
        var before = Reversed(oldColumns);
        var after = Reversed(newColumns);
        var frames = new List<Frame>();
        pace = pace is >= 50 and <= 250 ? pace : 100;
        void Push(byte[] frame, double ms, double level = 1) =>
            // transDelay(): whole milliseconds, never under one.
            frames.Add(new Frame(Reversed(frame), Math.Max(1, Math.Floor(ms * 100 / pace)), level));

        switch (effect)
        {
            case 1 or 2:
            {
                var strip = new byte[68];
                Array.Copy(effect == 1 ? before : after, 0, strip, 0, 32);
                Array.Copy(effect == 1 ? after : before, 0, strip, 36, 32);
                for (var step = 0; step <= 36; step++)
                    Push(strip.AsSpan(effect == 1 ? step : 36 - step, 32).ToArray(), Eased(step, 36, 4, 10, 8));
                break;
            }
            case 3 or 4:
            {
                for (var step = 0; step <= 10; step++)
                {
                    var frame = new byte[32];
                    for (var c = 0; c < 32; c++)
                    {
                        var both = effect == 3 ? (after[c] << 10) | before[c] : (before[c] << 10) | after[c];
                        frame[c] = (byte)((both >> (effect == 3 ? step : 10 - step)) & 0xFF);
                    }
                    Push(frame, Eased(step, 10, 14, 24, 3));
                }
                break;
            }
            case 5:
            {
                for (var step = 1; step <= 24; step++)
                {
                    var frame = new byte[32];
                    for (var c = 0; c < 32; c++)
                        for (var r = 0; r < 8; r++)
                        {
                            var threshold = Bayer[r, c % 8];
                            if (((before[c] >> r & 1) != 0 && threshold >= step * 4 - 32) ||
                                ((after[c] >> r & 1) != 0 && threshold < step * 4))
                                frame[c] |= (byte)(1 << r);
                        }
                    Push(frame, 14);
                }
                Push(after, 1);
                break;
            }
            case 6:
            {
                // The clock steps the panel's own brightness down and up again.
                for (var step = 12; step >= 0; step--) Push(before, 1000.0 / 13, 0.1 + 0.9 * step / 12);
                Push(after, 100, 0.1);
                for (var step = 0; step <= 12; step++) Push(after, 450.0 / 13, 0.1 + 0.9 * step / 12);
                break;
            }
            case 7 or 8 or 9:
            {
                // The old tile closes from the side opposite to the one the new opens from.
                for (var step = 1; step <= 32; step++)
                {
                    var frame = (byte[])before.Clone();
                    if (effect == 7)
                        for (var c = 31; c > 31 - step && c >= 0; c--) frame[c] = 0;
                    else if (effect == 8)
                        for (var c = 0; c < step && c < 32; c++) frame[c] = 0;
                    else
                    {
                        int low = step / 2, high = 31 - step / 2 - step % 2;
                        for (var c = 0; c < 32; c++)
                            if (low > high || c < low || c > high) frame[c] = 0;
                    }
                    Push(frame, 6);
                }
                Push(new byte[32], 100);
                for (var step = 1; step <= 32; step++)
                {
                    var frame = new byte[32];
                    if (effect == 7)
                        for (var c = 0; c < step && c < 32; c++) frame[c] = after[c];
                    else if (effect == 8)
                        for (var c = 31; c > 31 - step && c >= 0; c--) frame[c] = after[c];
                    else
                    {
                        int low = Math.Max(0, 16 - step / 2), high = Math.Min(31, 15 + step / 2 + step % 2);
                        for (var c = low; c <= high; c++) frame[c] = after[c];
                    }
                    Push(frame, 6);
                }
                Push(after, 1);
                break;
            }
            default:
                Push(after, 1);
                break;
        }
        return frames;
    }

    // A line of text wider than its window, moved as the tiles move theirs for
    // one whole round: there and back (Bounce) or round again (Wrap), with the
    // icon standing still beside it or travelling in front of it (+ Icon).
    // `type` is the clock's scroll type: 0 Bounce, 1 Wrap, 2 Bounce + Icon,
    // 3 Wrap + Icon.
    internal static List<Frame> Scroll(int type, byte[] icon, byte[] text, int stepMs)
    {
        var iconTravels = type is 2 or 3;
        var wraps = type is 1 or 3;
        byte[] line;
        if (iconTravels)
        {
            line = new byte[icon.Length + 1 + text.Length];
            Array.Copy(icon, line, icon.Length);
            Array.Copy(text, 0, line, icon.Length + 1, text.Length);
        }
        else line = text;
        int x = iconTravels ? 0 : 9, width = 32 - x, count = line.Length, end = Math.Max(0, count - width);
        stepMs = Math.Max(10, stepMs);

        byte[] At(int position)
        {
            var frame = new byte[32];
            if (!iconTravels) Array.Copy(icon, frame, Math.Min(8, icon.Length));
            for (var k = 0; k < width; k++)
                if (position + k >= 0 && position + k < count) frame[x + k] = line[position + k];
            return frame;
        }

        var frames = new List<Frame> { new(At(0), 1000) };
        if (wraps)
        {
            for (var position = 1; position < count; position++) frames.Add(new Frame(At(position), stepMs));
            for (var position = -width; position < 0; position++) frames.Add(new Frame(At(position), stepMs));
        }
        else if (end > 0)
        {
            for (var position = 1; position < end; position++) frames.Add(new Frame(At(position), stepMs));
            frames.Add(new Frame(At(end), 1000));
            for (var position = end - 1; position > 0; position--) frames.Add(new Frame(At(position), stepMs));
        }
        return frames;
    }

    private static byte[] Reversed(byte[] columns)
    {
        var copy = new byte[32];
        for (var c = 0; c < 32 && c < columns.Length; c++) copy[31 - c] = columns[c];
        return copy;
    }
}
