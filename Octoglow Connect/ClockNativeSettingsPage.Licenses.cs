using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// The firmware already exposes the same license catalogue used by its web UI.
// Showing it natively closes the last substantial feature gap in About.
public sealed partial class ClockNativeSettingsPage
{
    private async Task ShowClockLicensesDialogAsync()
    {
        var root = XamlRoot ?? _panelOwner?.XamlRoot;
        if (root is null) return;

        var width = Math.Clamp(root.Size.Width - 120, 300, 620);
        var content = new StackPanel { Spacing = 8 };
        var loading = new Grid
        {
            MinHeight = 180,
            Children =
            {
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 12,
                    Children =
                    {
                        new ProgressRing { IsActive = true, Width = 28, Height = 28 },
                        Secondary(L("Se încarcă licențele…", "Loading licenses…")),
                    },
                },
            },
        };
        content.Children.Add(loading);

        var scroller = new ScrollViewer
        {
            Width = width,
            MaxHeight = DialogBodyHeight(root),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = DialogHeading.Create("\uE8A5", L("Licențe", "Licenses")),
            Content = scroller,
            CloseButtonText = L("Închideți", "Close"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeSource.ActualTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = width + 64;

        using var cancellation = new CancellationTokenSource();
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args) => scroller.MaxHeight = DialogBodyHeight(sender);
        root.Changed += Resized;
        dialog.Closed += (_, _) => cancellation.Cancel();
        dialog.Opened += async (_, _) =>
        {
            try
            {
                var json = await ClockDeviceApi.GetTextAsync(_device, "/licenses", cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                BuildClockLicenses(content, json);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (cancellation.IsCancellationRequested) return;
                content.Children.Clear();
                content.Children.Add(SettingsCard("\uE783", L("Licențele nu au putut fi încărcate", "Couldn't load the licenses"),
                    FriendlyError(error)));
            }
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // WinUI permits one ContentDialog per XamlRoot at a time.
        }
        finally
        {
            root.Changed -= Resized;
        }
    }

    private static void BuildClockLicenses(StackPanel host, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new JsonException("The license catalogue has no items.");

        var names = root.TryGetProperty("names", out var nameMap) && nameMap.ValueKind == JsonValueKind.Object
            ? nameMap
            : default;
        var texts = root.TryGetProperty("texts", out var textMap) && textMap.ValueKind == JsonValueKind.Object
            ? textMap
            : default;
        host.Children.Clear();

        var groups = new (string Key, string Title)[]
        {
            ("own", "Octoglow"),
            ("lib", L("Biblioteci", "Libraries")),
            ("sys", L("Sistem", "System")),
            ("ui", L("Fonturi și iconițe", "Fonts and icons")),
        };
        foreach (var group in groups)
        {
            var matching = items.EnumerateArray()
                .Where(item => item.TryGetProperty("g", out var value) && value.GetString() == group.Key)
                .ToList();
            if (matching.Count == 0) continue;

            var heading = new TextBlock
            {
                Text = group.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(2, host.Children.Count == 0 ? 0 : 12, 0, 2),
            };
            host.Children.Add(heading);
            foreach (var item in matching)
                host.Children.Add(ClockLicenseRow(item, names, texts));
        }
    }

    private static Expander ClockLicenseRow(JsonElement item, JsonElement names, JsonElement texts)
    {
        var title = item.TryGetProperty("name", out var name) ? name.GetString() ?? "-" : "-";
        var version = item.TryGetProperty("ver", out var ver) ? ver.GetString() : null;
        var licenseIds = item.TryGetProperty("lic", out var licenses) && licenses.ValueKind == JsonValueKind.Array
            ? licenses.EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrEmpty(value)).Cast<string>().ToList()
            : [];
        var subtitle = (version is { Length: > 0 } ? version + " · " : "") + string.Join(", ", licenseIds);

        var body = new StackPanel { Spacing = 10, Padding = new Thickness(16, 2, 16, 16) };
        if (item.TryGetProperty("by", out var authors) && authors.ValueKind == JsonValueKind.Array)
        {
            foreach (var author in authors.EnumerateArray())
                if (author.GetString() is { Length: > 0 } line) body.Children.Add(Secondary(line));
        }
        if (item.TryGetProperty("url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri))
            body.Children.Add(new HyperlinkButton { Content = uri.Host, NavigateUri = uri, Padding = new Thickness(0) });

        foreach (var id in licenseIds)
        {
            var licenseName = names.ValueKind == JsonValueKind.Object && names.TryGetProperty(id, out var named)
                ? named.GetString() ?? id
                : id;
            body.Children.Add(new TextBlock
            {
                Text = licenseName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0),
            });
            if (texts.ValueKind == JsonValueKind.Object && texts.TryGetProperty(id, out var licenseText) && licenseText.GetString() is { } text)
                body.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Opacity = 0.82,
                });
        }

        return new Expander
        {
            Header = TitleBlock(title, subtitle),
            Content = body,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }
}
