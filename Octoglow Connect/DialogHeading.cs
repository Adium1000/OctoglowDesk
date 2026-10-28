using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

internal static class DialogHeading
{
    public static UIElement Create(string glyph, string title)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
        });
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return heading;
    }
}
