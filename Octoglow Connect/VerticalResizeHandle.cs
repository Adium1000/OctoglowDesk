using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OctoglowSender;

// A transparent hit target can keep the old divider's appearance while
// advertising its drag action with the standard vertical-resize cursor.
public sealed class VerticalResizeHandle : UserControl
{
    public VerticalResizeHandle()
    {
        Content = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    }
}
