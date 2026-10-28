using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OctoglowSender;

// The native counterparts of the contextual "Did you know?" and
// "Looking for something else?" groups from the clock's web interface.
public sealed partial class ClockNativeSettingsPage
{
    private void AddDidYouKnow(params (string Glyph, string Title, string Description)[] facts) =>
        Add(SuggestionBox(L("Știați că?", "Did you know?"), "\uE946",
            facts.Select(fact => SuggestionInfoRow(fact.Glyph, fact.Title, fact.Description))));

    private void AddLookingElse(params (ClockSettingsSection Section, string? Description)[] suggestions)
    {
        var rows = suggestions.Select(suggestion =>
        {
            var (title, description) = SectionHeader(suggestion.Section);
            return SuggestionLinkRow(SectionGlyph(suggestion.Section), CardTitle(suggestion.Section, title),
                suggestion.Description ?? description, () => OpenSuggestedSection(suggestion.Section));
        });
        Add(SuggestionBox(L("Căutați altceva?", "Looking for something else?"), "\uE721", rows));
    }

    private void AddLookingElseLinks(params (ClockSettingsSection Section, string Title)[] links) =>
        Add(SuggestionBox(L("Căutați altceva?", "Looking for something else?"), "\uE721",
            links.Select(link => SuggestionLinkRow(SectionGlyph(link.Section), link.Title, "",
                () => OpenSuggestedSection(link.Section)))));

    private static Border SuggestionBox(string title, string glyph, IEnumerable<UIElement> rows)
    {
        var card = CardBorder();
        card.Margin = new Thickness(0, 12, 0, 0);
        card.CornerRadius = new CornerRadius(8);
        card.Background = Resource<Brush>("SuggestionBackgroundBrush");
        card.BorderBrush = Resource<Brush>("SuggestionBorderBrush");

        var content = new StackPanel();
        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(18, 15, 18, 10),
            Children =
            {
                new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 16,
                    Foreground = Resource<Brush>("SuggestionAccentBrush"),
                },
                new TextBlock
                {
                    Text = title,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Resource<Brush>("SuggestionForegroundBrush"),
                },
            },
        };
        content.Children.Add(heading);
        foreach (var row in rows)
        {
            content.Children.Add(row);
        }
        card.Child = content;
        return card;
    }

    private static Grid SuggestionInfoRow(string glyph, string title, string description)
    {
        var row = SuggestionRow(glyph, title, description, false);
        row.Margin = new Thickness(8, 0, 8, 8);
        AutomationProperties.SetName(row, title);
        return row;
    }

    private static Button SuggestionLinkRow(string glyph, string title, string description, Action open)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(8, 0, 8, 8),
            Content = SuggestionRow(glyph, title, description, true),
        };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => open();
        return button;
    }

    private static Grid SuggestionRow(string glyph, string title, string description, bool chevron)
    {
        var row = new Grid { Padding = new Thickness(12, 10, 12, 10), ColumnSpacing = 12, MinHeight = 48 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (chevron) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            Foreground = Resource<Brush>("SuggestionAccentBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 0, 0),
        });
        var copy = TitleBlock(title, description);
        foreach (var text in copy.Children.OfType<TextBlock>())
            text.Foreground = Resource<Brush>("SuggestionForegroundBrush");
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        if (chevron)
        {
            var arrow = new FontIcon { Glyph = "\uE76C", FontSize = 13, Opacity = 0.72,
                Foreground = Resource<Brush>("SuggestionAccentBrush") };
            Grid.SetColumn(arrow, 2);
            row.Children.Add(arrow);
        }
        return row;
    }

    private void OpenSuggestedSection(ClockSettingsSection section)
    {
        // A panel cannot open another panel. Keep the same navigation pattern,
        // but go one level deeper inside the panel when necessary.
        if (_panel is not null)
        {
            var (title, description) = SectionHeader(section);
            if (IsLocalSection(section))
                OpenLocalSubPage(title, description, () => BuildSectionContent(section, null));
            else
                OpenSubPage(title, description, state => BuildSectionContent(section, state));
            return;
        }
        OpenConfigurationSection(section);
    }

    private void AddTouchInsight()
    {
        AddDidYouKnow(
            ("\uE701", L("Comutați Wi-Fi / Access Point", "Switch Wi-Fi / Access Point"),
                L("Țineți apăsat senzorul 2 secunde cât timp ceasul funcționează.",
                  "Hold the sensor for 2 seconds while the clock is running.")),
            ("\uE777", L("Resetare din fabrică", "Factory Reset"),
                L("La pornire, începeți apăsarea în primele 3 secunde și țineți senzorul 5 secunde. Setările, rețeaua Wi-Fi și contul de acces vor fi șterse.",
                  "At startup, begin pressing within the first 3 seconds and hold the sensor for 5 seconds. Settings, the Wi-Fi network and the sign-in account will be erased.")));
    }
}
