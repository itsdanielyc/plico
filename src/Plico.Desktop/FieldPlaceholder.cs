using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Plico.Desktop;

/// <summary>
/// A field's placeholder is Fg3 at full strength (5.7:1), as the family's Input has it. Fluent's text box template draws
/// its placeholder at half opacity, set inside the template where a style cannot reach it, which made "Add a date" read
/// as disabled beside the values around it. Every text field, the tag fields' inner one included, gets full opacity
/// when its template is applied.
/// </summary>
internal static class FieldPlaceholder
{
    internal static void Register() => TemplatedControl.TemplateAppliedEvent.AddClassHandler<TextBox>((field, applied) =>
    {
        if (applied.NameScope.Find<TextBlock>("PART_Placeholder") is { } placeholder) placeholder.Opacity = 1;
    });
}
