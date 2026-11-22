using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Plico.Desktop;

/// <summary>
/// Lays out a dialog built in code like the XAML dialogs: a raised window with a 20 px heading, the body, and a
/// footer under a hairline with the actions on the right, the primary last. Styles live in Theme/Controls.axaml.
/// </summary>
internal static class DialogLayout
{
    /// <summary>Marks <paramref name="dialog"/> as a dialog and sets its content.</summary>
    /// <param name="dialog">The dialog window.</param>
    /// <param name="heading">The heading text, or null when the body carries its own.</param>
    /// <param name="body">The dialog's content between the heading and the footer.</param>
    /// <param name="footerStart">A status line or a lone action on the left of the footer, or null.</param>
    /// <param name="actions">The footer actions, secondary first and the primary last.</param>
    internal static void Compose(Window dialog, string? heading, Control body, Control? footerStart, params Button[] actions) =>
        Compose(dialog, heading, null, body, footerStart, actions);

    /// <summary>
    /// Marks <paramref name="dialog"/> as a dialog with a head (a 20 px title that names the task and a 14 px Fg2
    /// description), the body, and the footer.
    /// </summary>
    /// <param name="description">One sentence under the title in Fg2, or null.</param>
    internal static void Compose(Window dialog, string? heading, string? description, Control body, Control? footerStart, params Button[] actions)
    {
        dialog.Classes.Add("dialog");
        var buttons = new StackPanel { Classes = { "dialog-actions" } };
        foreach (var action in actions) buttons.Children.Add(action);
        Grid.SetColumn(buttons, 1);
        var footerRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        if (footerStart is not null)
        {
            footerStart.VerticalAlignment = VerticalAlignment.Center;
            footerRow.Children.Add(footerStart);
        }
        footerRow.Children.Add(buttons);
        var footer = new Border { Classes = { "dialog-footer" }, Child = footerRow };
        DockPanel.SetDock(footer, Dock.Bottom);

        var content = new Grid { Classes = { "dialog-body" }, RowDefinitions = new("Auto,*"), RowSpacing = 14 };
        if (heading is not null || description is not null)
        {
            var head = new StackPanel { Spacing = 4 };
            if (heading is not null) head.Children.Add(new TextBlock { Text = heading, Classes = { "heading" }, TextWrapping = TextWrapping.Wrap });
            if (description is not null) head.Children.Add(new TextBlock { Text = description, Classes = { "description" } });
            content.Children.Add(head);
        }
        Grid.SetRow(body, 1);
        content.Children.Add(body);
        dialog.Content = new DockPanel { Children = { footer, content } };
    }

    /// <summary>The dialog's main action: an ink button.</summary>
    internal static Button Primary(string label, bool isDefault = false) =>
        new() { Content = label, Classes = { "primary" }, IsDefault = isDefault };

    /// <summary>Any other action: an outlined button.</summary>
    internal static Button Secondary(string label, bool isCancel = false) =>
        new() { Content = label, Classes = { "secondary" }, IsCancel = isCancel };

    /// <summary>A line of status or validation text for the footer or the body.</summary>
    internal static TextBlock Message(string style = "validation") => new() { Classes = { style }, TextWrapping = TextWrapping.Wrap };

    /// <summary>
    /// An empty state in a dashed frame: a 44 px graphite tile with a white glyph, a 16 px title and one sentence.
    /// </summary>
    /// <param name="icon">The resource key of a 20-unit stroke glyph, for example "Icon.Upcoming".</param>
    internal static Control EmptyState(string icon, string title, string text)
    {
        var glyph = new Icon { Width = 20, Height = 20, StrokeWidth = 2, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        if (Avalonia.Application.Current?.TryFindResource(icon, out var data) == true) glyph.Data = data as Geometry;
        var content = new StackPanel
        {
            Classes = { "empty" },
            Margin = new Avalonia.Thickness(24, 32),
            Children =
            {
                new Border { Classes = { "tile", "empty-tile" }, Child = glyph },
                new TextBlock { Text = title, Classes = { "empty-title" } },
                new TextBlock { Text = text, Classes = { "empty-text" } }
            }
        };
        return new Panel { Children = { new Avalonia.Controls.Shapes.Rectangle { Classes = { "dashed-frame" } }, content } };
    }
}
