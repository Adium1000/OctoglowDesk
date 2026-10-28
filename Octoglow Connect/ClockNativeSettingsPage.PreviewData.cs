using System.Globalization;
using System.Text.Json;

namespace OctoglowSender;

// The wire format is kept separate from WinUI so malformed/older firmware
// responses can be regression-tested without a window or a connected clock.
public sealed partial class ClockNativeSettingsPage
{
    private sealed record TilePreviewAnswer(
        bool Live, bool Sample, byte[] Still,
        byte[]? Text, int TextX, int TextWidth, bool Wraps, int StepMs, int PauseMs,
        List<byte[]>? Frames, int FrameMs);

    private static TilePreviewAnswer ParseTilePreview(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a preview object.");
        static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        static int Number(JsonElement e, string name, int fallback) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
        static string Hex(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        byte[]? text = null;
        int textX = 0, textWidth = 32, stepMs = 40, pauseMs = 1000;
        var wraps = false;
        if (root.TryGetProperty("scroll", out var scroll) && scroll.ValueKind == JsonValueKind.Object)
        {
            text = Columns(Hex(scroll, "buf"));
            textX = Math.Clamp(Number(scroll, "x", 0), 0, 31);
            textWidth = Math.Clamp(Number(scroll, "w", 32), 1, 32 - textX);
            wraps = Flag(scroll, "wrap");
            stepMs = Math.Clamp(Number(scroll, "ms", 40), 16, 5000);
            pauseMs = Math.Clamp(Number(scroll, "pause", 1000), 0, 60000);
        }
        List<byte[]>? frames = null;
        var frameMs = 80;
        if (root.TryGetProperty("anim", out var anim) && anim.ValueKind == JsonValueKind.Object &&
            anim.TryGetProperty("frames", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            frames = list.EnumerateArray()
                .Where(frame => frame.ValueKind == JsonValueKind.String)
                .Select(frame => Panel32(Columns(frame.GetString() ?? "")))
                .ToList();
            if (frames.Count < 2) frames = null;
            frameMs = Math.Clamp(Number(anim, "ms", 80), 16, 5000);
        }
        return new TilePreviewAnswer(Flag(root, "live"), Flag(root, "sample"), Panel32(Columns(Hex(root, "px"))),
            text is { Length: > 0 } && text.Length > textWidth ? text : null, textX, textWidth, wraps, stepMs, pauseMs, frames, frameMs);
    }

    // Two hex digits a column, as /getscreen gives them: bit 0 the top row.
    private static byte[] Columns(string hex)
    {
        var columns = new byte[hex.Length / 2];
        for (var i = 0; i < columns.Length; i++)
            columns[i] = byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : (byte)0;
        return columns;
    }

    private static byte[] Panel32(byte[] columns)
    {
        if (columns.Length == 32) return columns;
        var panel = new byte[32];
        Array.Copy(columns, panel, Math.Min(32, columns.Length));
        return panel;
    }
}
