using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// Pages of their own inside a section - the Tile Manager's appearance pages,
// the hardware monitor - opened in place with a back button, the way the web
// interface goes one screen deeper. The section is set aside meanwhile and
// comes back as it was left, scrolled where it was. The page and a panel over
// it each keep their own.
public sealed partial class ClockNativeSettingsPage
{
    // Build fills the page from fresh state and may hand back what stops it
    // again, such as a poll that should only run while the page is open.
    private sealed record SubPage(
        string Title,
        string Description,
        Func<ClockDeviceState?, Action?> Build,
        bool RequiresDeviceState,
        string? Glyph = null);

    private void OpenSubPage(string title, string description, Func<ClockDeviceState, Action?> build, string? glyph = null)
        => OpenSubPage(new SubPage(title, description, state => build(state!), true, glyph));

    private void OpenLocalSubPage(string title, string description, Func<Action?> build, string? glyph = null)
        => OpenSubPage(new SubPage(title, description, _ => build(), false, glyph));

    private void OpenSubPage(SubPage page)
    {
        PauseTileLineReads();
        var surface = Surface;
        if (surface.SubPage is null)
        {
            surface.SectionChildren = surface.Content.Children.ToList();
            surface.SectionOffset = surface.Scroller.VerticalOffset;
        }
        else
        {
            // One page opening another: back goes to the one it came from.
            surface.SubPageStack.Add(surface.SubPage);
        }
        surface.SubPage = page;
        _ = LoadSubPageAsync(surface);
    }

    private async Task LoadSubPageAsync(SettingsSurface surface, bool forward = true)
    {
        DismissTileDialog(surface);
        var page = surface.SubPage;
        if (page is null) return;
        StopSubPage(surface);
        surface.Content.Children.Clear();
        surface.Status.IsOpen = false;
        ApplyHeader(surface);
        surface.Scroller.ChangeView(null, 0, null, true);

        surface.Cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        surface.Cancellation = cancellation;
        var token = cancellation.Token;
        surface.Loading.Visibility = Visibility.Visible;
        surface.Refresh.IsEnabled = false;
        try
        {
            ClockDeviceState? state = null;
            // Device-backed pages always read fresh; local pages can open even
            // while the clock is temporarily unavailable.
            if (page.RequiresDeviceState)
                state = await ClockDeviceApi.GetStateAsync(_device, token);
            if (token.IsCancellationRequested || surface.SubPage != page) return;
            surface.Content.Children.Clear();
            BuildInto(surface, () => surface.SubPageCleanup = page.Build(state));
            SlideIn(surface, forward);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ShowError(surface.Status, FriendlyError(error));
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                surface.Loading.Visibility = Visibility.Collapsed;
                surface.Refresh.IsEnabled = true;
            }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => CloseSubPage(PageSurface);

    private void CloseSubPage(SettingsSurface surface)
    {
        if (surface.SubPage is null) return;
        DismissTileDialog(surface);
        surface.Cancellation?.Cancel();
        StopSubPage(surface);
        if (surface.SubPageStack.Count > 0)
        {
            var parent = surface.SubPageStack[^1];
            surface.SubPageStack.RemoveAt(surface.SubPageStack.Count - 1);
            surface.SubPage = parent;
            // Read again rather than kept: what it shows may have been changed
            // on the page being left.
            _ = LoadSubPageAsync(surface, forward: false);
            return;
        }
        surface.SubPage = null;
        surface.Loading.Visibility = Visibility.Collapsed;
        surface.Refresh.IsEnabled = true;
        surface.Status.IsOpen = false;
        surface.Content.Children.Clear();
        ApplyHeader(surface);
        if (surface.SectionChildren is null)
        {
            _ = LoadSurfaceAsync(surface);
            return;
        }
        foreach (var child in surface.SectionChildren) surface.Content.Children.Add(child);
        surface.SectionChildren = null;
        surface.Scroller.UpdateLayout();
        surface.Scroller.ChangeView(null, surface.SectionOffset, null, true);
        SlideIn(surface, forward: false);
    }

    // For when the section itself is rebuilt: there is nothing left to go back to.
    private void DiscardSubPage(SettingsSurface surface)
    {
        StopSubPage(surface);
        surface.SubPage = null;
        surface.SubPageStack.Clear();
        surface.SectionChildren = null;
    }

    private static void StopSubPage(SettingsSurface surface)
    {
        ResetNavigationMotion(surface);
        surface.SubPageCleanup?.Invoke();
        surface.SubPageCleanup = null;
    }

    // Going a level deeper slides in from the right and coming back from the
    // left, the way WinUI's own navigation moves between pages - on the page and
    // inside a panel alike.
    internal static readonly TimeSpan NavigationAnimation = TimeSpan.FromMilliseconds(220);

    private static void ResetNavigationMotion(SettingsSurface surface)
    {
        ++surface.NavigationVersion;
        surface.Content.TranslationTransition = null;
        surface.Content.OpacityTransition = null;
        surface.Content.Translation = Vector3.Zero;
        surface.Content.Opacity = 1;
    }

    private static void SlideIn(SettingsSurface surface, bool forward)
    {
        ResetNavigationMotion(surface);
        if (!AnimationsEnabled) return;
        var version = surface.NavigationVersion;
        var content = surface.Content;
        content.Translation = new Vector3(forward ? 44 : -44, 0, 0);
        content.Opacity = 0;
        if (!content.DispatcherQueue.TryEnqueue(() =>
        {
            // Rapid back/forward navigation must not animate a newer page.
            if (surface.NavigationVersion != version) return;
            content.TranslationTransition = new Vector3Transition { Duration = NavigationAnimation };
            content.OpacityTransition = new ScalarTransition { Duration = NavigationAnimation };
            content.Translation = Vector3.Zero;
            content.Opacity = 1;
        })) ResetNavigationMotion(surface);
    }

    // A card that opens a page of its own, with the chevron that says so.
    internal static Button NavCard(string glyph, string title, string? description, Action open) =>
        NavCard(glyph, title, description, null, open);

    // The same card carrying the setting the page it opens is about - the one
    // that applies to every tile - so the common change takes no trip inside.
    // The control keeps the press to itself; the rest of the card opens the page.
    private static Button NavCard(string glyph, string title, string? description, FrameworkElement? control, Action open)
    {
        var button = CardButton();
        var chevron = new FontIcon { Glyph = "", FontSize = 14, Opacity = 0.72 };
        button.Content = HeaderGrid(glyph, title, description, control is null ? chevron : Inline(control, chevron));
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => open();
        return button;
    }

    // One question before something that can't be taken back. No answer - a
    // dialog already open, say - counts as no.
    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        // A panel opened from another page - About from the clock page - has no
        // window of its own, and borrows the one that opened it.
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = DialogHeading.Create("\uE7BA", title),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = action,
            CloseButtonText = L("Anulați", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeSource.ActualTheme,
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    // Changes what an ActionButton says, laid out the way ActionButton lays it out.
    private static void SetActionButton(Button button, string text, string glyph)
    {
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
        };
    }
}
