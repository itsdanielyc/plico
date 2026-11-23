using Avalonia;
using Avalonia.Controls;

namespace Plico.Desktop;

/// <summary>
/// A column of reading width: it takes all the width it is offered, up to its <see cref="Layoutable.MaxWidth"/>, whatever
/// its content's own width. Aligned to the left, a page in it keeps one measure, so its right edge does not move with its
/// words (a path cut short by an ellipsis, a short one, another language), and it narrows with a narrow window.
/// </summary>
public sealed class MeasureColumn : Decorator
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // The offered width already has MaxWidth and the margin applied by the layout system.
        Size content = base.MeasureOverride(availableSize);
        return double.IsInfinity(availableSize.Width) ? content : content.WithWidth(Math.Max(content.Width, availableSize.Width));
    }
}
