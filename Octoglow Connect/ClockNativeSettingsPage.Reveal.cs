using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace OctoglowSender;

public sealed partial class ClockNativeSettingsPage
{
    private static readonly UISettings MotionSettings = new();
    private static bool AnimationsEnabled => MotionSettings.AnimationsEnabled;
    private static string UiLanguage => AppStrings.IsEnglish ? "en-US" : "ro-RO";

    private sealed class RevealState
    {
        public Storyboard? Animation;
        public bool Open;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, RevealState> Reveals = new();

    private static void SetExpanded(FrameworkElement element, bool open, bool animate = true)
    {
        var state = Reveals.GetValue(element, _ => new RevealState());
        if (state.Animation is not null && state.Open == open) return;
        if (state.Animation is null && element.Visibility == (open ? Visibility.Visible : Visibility.Collapsed))
            return;
        var from = element.Visibility == Visibility.Visible ? element.ActualHeight : 0;
        state.Animation?.Stop();
        state.Animation = null;
        state.Open = open;
        element.Height = double.NaN;
        if (!animate || !AnimationsEnabled || !element.IsLoaded)
        {
            element.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        element.Visibility = Visibility.Visible;
        var width = element.ActualWidth > 0 ? element.ActualWidth
            : (element.Parent as FrameworkElement)?.ActualWidth ?? 400;
        element.Measure(new Size(Math.Max(1, width), double.PositiveInfinity));
        var target = open ? element.DesiredSize.Height : 0;
        if (element.Clip is null)
        {
            var clip = new RectangleGeometry { Rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight) };
            element.Clip = clip;
            element.SizeChanged += (_, args) => clip.Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
        }
        element.Height = from;
        var animation = new DoubleAnimation
        {
            From = from, To = target, Duration = new Duration(GroupAnimation),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Height");
        var board = new Storyboard();
        board.Children.Add(animation);
        state.Animation = board;
        board.Completed += (_, _) =>
        {
            if (state.Animation != board) return;
            board.Stop();
            state.Animation = null;
            element.Height = double.NaN;
            element.Visibility = state.Open ? Visibility.Visible : Visibility.Collapsed;
        };
        board.Begin();
    }
}
