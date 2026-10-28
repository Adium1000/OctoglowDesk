using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// Centres a column of content inside a ScrollViewer. A MaxWidth with centred
// or stretched alignment isn't enough there: on a full-screen window the
// scroller placed the column against the left edge, or past the centre with
// its right side cut off. So the column is given exactly the scroller's width
// every time it changes, split into equal side margins and the content between
// them - nothing is left over for the scroller to align.
internal static class CenteredColumn
{
    public static void Attach(ScrollViewer scroller, FrameworkElement column, double maxWidth, double gutter)
    {
        column.HorizontalAlignment = HorizontalAlignment.Left;
        void Fit()
        {
            var available = scroller.ActualWidth;
            if (available <= 0) return;
            var width = Math.Max(0, Math.Min(maxWidth, available - 2 * gutter));
            var side = (available - width) / 2;
            column.Width = width;
            column.Margin = new Thickness(side, column.Margin.Top, side, column.Margin.Bottom);
        }
        scroller.SizeChanged += (_, _) => Fit();
        Fit();
    }
}
