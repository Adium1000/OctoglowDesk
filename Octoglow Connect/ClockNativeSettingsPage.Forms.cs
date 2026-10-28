using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace OctoglowSender;

// The two ways a setting is acted on, each done the same everywhere: text that
// is typed and then saved, and a card that is an action in itself.
public sealed partial class ClockNativeSettingsPage
{
    // ------------------------------------------------------------------ typed and saved

    private sealed record SaveField(Border Card, TextBlock Description, Action Refresh, Action Submit);

    // The field runs across the card under its title with Save beside it, and
    // Save only shows while the field holds something new. Enter saves too.
    // `save` updates what counts as saved once it has worked; `below` goes under
    // the field, such as a preview.
    private SaveField SaveFieldCard(
        string glyph,
        string title,
        string description,
        FrameworkElement input,
        Func<bool> changed,
        Func<Task> save,
        params UIElement[] below)
    {
        var button = ActionButton(L("Salvați", "Save"), "", accent: true);
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        input.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(input);
        Grid.SetColumn(button, 1);
        row.Children.Add(button);

        var content = new StackPanel { Spacing = 10, Children = { row } };
        foreach (var element in below) content.Children.Add(element);
        var (card, text) = StackedCard(glyph, title, description, content);

        var refresh = ShowWhenChanged(changed, button);
        async void Submit()
        {
            if (!button.IsEnabled || !changed()) return;
            button.IsEnabled = false;
            try
            {
                await save();
            }
            finally
            {
                button.IsEnabled = true;
                refresh();
            }
        }
        button.Click += (_, _) => Submit();
        return new SaveField(card, text, refresh, Submit);
    }

    // A key or a password, hidden until the eye beside it is pressed. The box's own
    // reveal button only appears for something typed into it, never for a value
    // the app filled in - so a saved key could not be seen at all.
    private static (Grid View, PasswordBox Box) SecretField(string value, string placeholder)
    {
        var box = new PasswordBox
        {
            Password = value,
            PlaceholderText = placeholder,
            PasswordRevealMode = PasswordRevealMode.Hidden,
            Padding = new Thickness(12, 8, 44, 8),
            MinWidth = 180,
        };
        var icon = new FontIcon { Glyph = "", FontSize = 14 };
        var eye = new ToggleButton
        {
            Content = icon,
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
        };
        void Show(bool visible)
        {
            box.PasswordRevealMode = visible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
            icon.Glyph = visible ? "" : "";
            var label = visible ? L("Ascunde", "Hide") : L("Arată", "Show");
            ToolTipService.SetToolTip(eye, label);
            AutomationProperties.SetName(eye, label);
        }
        eye.Checked += (_, _) => Show(true);
        eye.Unchecked += (_, _) => Show(false);
        Show(false);

        var view = new Grid();
        view.Children.Add(box);
        view.Children.Add(eye);
        return (view, box);
    }

    // ------------------------------------------------------------------ actions

    private sealed record ActionCardParts(Button Card, TextBlock Description, TextBlock Label, FontIcon? Glyph);

    // A setting that is an action - power off, export, reset - is one card to
    // click, not a card with a button inside it. `action` is written on its
    // right in the accent colour when it says something the title doesn't -
    // which way a switch will go, what a running timer will do - and is left
    // empty when the title already names the action. `value` goes before it,
    // such as a running time.
    private static ActionCardParts ActionCard(
        string glyph,
        string title,
        string description,
        string action,
        string? actionGlyph,
        FrameworkElement? value = null)
    {
        var card = CardButton();
        var label = Themed<TextBlock>(
            $"<TextBlock {XamlNs} Foreground='{{ThemeResource AccentTextFillColorPrimaryBrush}}' VerticalAlignment='Center'/>");
        label.Text = action;
        FontIcon? actionIcon = null;
        if (actionGlyph is not null && action.Length > 0)
        {
            actionIcon = Themed<FontIcon>(
                $"<FontIcon {XamlNs} Foreground='{{ThemeResource AccentTextFillColorPrimaryBrush}}' FontSize='14' VerticalAlignment='Center'/>");
            actionIcon.Glyph = actionGlyph;
        }
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (value is not null)
        {
            value.Margin = new Thickness(0, 0, 8, 0);
            trailing.Children.Add(value);
        }
        if (action.Length > 0) trailing.Children.Add(label);
        if (actionIcon is not null) trailing.Children.Add(actionIcon);

        var header = HeaderGrid(glyph, title, description, trailing.Children.Count > 0 ? trailing : null);
        card.Content = header;
        AutomationProperties.SetName(card, title);
        // TitleBlock puts the description second, under the title, when there is one.
        var titleBlock = (Panel)header.Children[1];
        var text = titleBlock.Children.Count > 1 ? (TextBlock)titleBlock.Children[1] : new TextBlock();
        return new ActionCardParts(card, text, label, actionIcon);
    }

    private static void SetActionCard(ActionCardParts parts, string action, string glyph)
    {
        parts.Label.Text = action;
        if (parts.Glyph is not null) parts.Glyph.Glyph = glyph;
    }

    // A card-shaped button, shared by the cards that open something and those that do something.
    private static Button CardButton() => Themed<Button>(
        $"<Button {XamlNs} Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' " +
        "BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1' CornerRadius='6' " +
        "HorizontalAlignment='Stretch' HorizontalContentAlignment='Stretch' Padding='16,12,16,12' MinHeight='68'/>");
}
