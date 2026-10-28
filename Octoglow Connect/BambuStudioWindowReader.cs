using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OctoglowSender;

// Bambu Studio has no external print-status API. Its Device page renders the
// current print percentage in two adjacent native wxStaticText controls: a
// number and "%". Read only those controls; never scrape credentials or logs.
internal static class BambuStudioWindowReader
{
    internal readonly record struct Reading(bool StudioRunning, bool HasProgress, int Progress);

    private delegate bool WindowCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, WindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint word,
        StringBuilder text, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);

    private static nint _numberWindow;
    private static nint _percentWindow;

    internal static Reading Read()
    {
        using var processes = new ProcessList(Process.GetProcessesByName("bambu-studio"));
        if (processes.Items.Length == 0)
        {
            _numberWindow = _percentWindow = 0;
            return new Reading(false, false, 0);
        }

        if (IsWindow(_numberWindow) && IsWindow(_percentWindow) &&
            IsWindowVisible(_numberWindow) && IsWindowVisible(_percentWindow) &&
            WindowText(_percentWindow) == "%" &&
            int.TryParse(WindowText(_numberWindow), out var cachedNumber) && cachedNumber is >= 0 and <= 100)
        {
            GetWindowThreadProcessId(_numberWindow, out var owner);
            if (processes.Items.Any(process => (uint)process.Id == owner))
                return new Reading(true, true, cachedNumber);
        }
        _numberWindow = _percentWindow = 0;

        foreach (var process in processes.Items)
        {
            var processId = (uint)process.Id;
            Reading? found = null;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner != processId || !IsWindowVisible(window) || IsHungAppWindow(window)) return true;

                var controls = new List<(nint Handle, nint Parent, string Text, Rect Bounds)>();
                var budget = Stopwatch.StartNew();
                EnumChildWindows(window, (child, ignored) =>
                {
                    if (budget.ElapsedMilliseconds > 750) return false;
                    if (!IsWindowVisible(child) || ClassName(child) != "Static") return true;
                    var text = WindowText(child);
                    if (text != "%" && !int.TryParse(text, out var numericText)) return true;
                    if (GetWindowRect(child, out var bounds))
                        controls.Add((child, GetParent(child), text, bounds));
                    return true;
                }, 0);

                foreach (var percentSign in controls.Where(control => control.Text == "%"))
                {
                    var value = controls.FirstOrDefault(control =>
                        control.Parent == percentSign.Parent &&
                        int.TryParse(control.Text, out var number) && number is >= 0 and <= 100 &&
                        control.Bounds.Right <= percentSign.Bounds.Left + 8 &&
                        percentSign.Bounds.Left - control.Bounds.Right <= 64 &&
                        Math.Abs((control.Bounds.Top + control.Bounds.Bottom) / 2 -
                                 (percentSign.Bounds.Top + percentSign.Bounds.Bottom) / 2) <= 32);
                    if (value.Handle == 0) continue;
                    _numberWindow = value.Handle;
                    _percentWindow = percentSign.Handle;
                    found = new Reading(true, true, int.Parse(value.Text));
                    return false;
                }
                return true;
            }, 0);
            if (found is not null) return found.Value;
        }

        // The application may be open on another page; an old percentage is
        // never reused when the Device status controls are not visible.
        return new Reading(true, false, 0);
    }

    private static string WindowText(nint window)
    {
        var text = new StringBuilder(64);
        // WM_GETTEXT is system-marshalled across processes for standard STATIC
        // controls; GetWindowText does not read foreign child controls.
        SendMessageTimeout(window, 0x000D, text.Capacity, text, 0x0002, 50, out _);
        return text.ToString();
    }

    private static string ClassName(nint window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private sealed class ProcessList(Process[] items) : IDisposable
    {
        internal Process[] Items { get; } = items;
        public void Dispose()
        {
            foreach (var item in Items) item.Dispose();
        }
    }
}
