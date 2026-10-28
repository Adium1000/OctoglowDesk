using System.Collections.Specialized;
using Microsoft.UI.Xaml.Controls;
namespace OctoglowSender;
public sealed partial class LogsPage : Page
{
    private bool _subscribed;

    public LogsPage()
    {
        InitializeComponent();
        CenteredColumn.Attach(LogsScroller, LogsColumn, maxWidth: 976, gutter: 32);
        LogsList.ItemsSource = AppLog.DeskClockEntries;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ShowNewest();
        if (_subscribed) return;
        _subscribed = true;
        AppLog.DeskClockEntries.CollectionChanged += Entries_CollectionChanged;
        AppStrings.LanguageChanged += LanguageChanged;
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_subscribed) return;
        _subscribed = false;
        AppLog.DeskClockEntries.CollectionChanged -= Entries_CollectionChanged;
        AppStrings.LanguageChanged -= LanguageChanged;
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ShowNewest();

    // The newest entry is the one worth seeing, as it was with the list's own scroller.
    private void ShowNewest() => DispatcherQueue.TryEnqueue(() =>
    {
        LogsScroller.UpdateLayout();
        LogsScroller.ChangeView(null, LogsScroller.ScrollableHeight, null, true);
    });

    private void LanguageChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        LogsList.ItemsSource = null;
        LogsList.ItemsSource = AppLog.DeskClockEntries;
        ShowNewest();
    });
}
