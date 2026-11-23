using Avalonia;
using Avalonia.Controls;

namespace Plico.Desktop;

/// <summary>
/// Lays out the queue's count line: the count first, as wide as it needs (it wraps only when it would leave the
/// second child less than <see cref="TrailingMinWidth"/>), then the second child, the sort choice, at the right in
/// whatever room is left, where it trims its own label. So the count sentence is always whole. O(1) per layout.
/// </summary>
internal sealed class CountLinePanel : Panel
{
    public static readonly StyledProperty<double> TrailingMinWidthProperty =
        AvaloniaProperty.Register<CountLinePanel, double>(nameof(TrailingMinWidth), 72);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CountLinePanel, double>(nameof(Spacing), 8);

    static CountLinePanel()
    {
        AffectsMeasure<CountLinePanel>(TrailingMinWidthProperty, SpacingProperty);
    }

    /// <summary>The narrowest the sort choice gets before the count starts to wrap.</summary>
    public double TrailingMinWidth { get => GetValue(TrailingMinWidthProperty); set => SetValue(TrailingMinWidthProperty, value); }

    /// <summary>The gap between the count and the sort choice.</summary>
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children is not [var lead, var trail, ..]) return Children is [var only] ? Measured(only, availableSize) : default;
        double width = availableSize.Width;
        lead.Measure(new Size(Math.Max(0, width - TrailingMinWidth - Spacing), availableSize.Height));
        trail.Measure(new Size(Math.Max(0, width - lead.DesiredSize.Width - Spacing), availableSize.Height));
        double used = lead.DesiredSize.Width + Spacing + trail.DesiredSize.Width;
        return new Size(double.IsFinite(width) ? Math.Min(width, used) : used, Math.Max(lead.DesiredSize.Height, trail.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children is not [var lead, var trail, ..])
        {
            if (Children is [var only]) only.Arrange(new Rect(finalSize));
            return finalSize;
        }
        double leadWidth = Math.Min(lead.DesiredSize.Width, finalSize.Width);
        lead.Arrange(new Rect(0, 0, leadWidth, finalSize.Height));
        double trailWidth = Math.Min(trail.DesiredSize.Width, Math.Max(0, finalSize.Width - leadWidth - Spacing));
        trail.Arrange(new Rect(finalSize.Width - trailWidth, 0, trailWidth, finalSize.Height));
        return finalSize;
    }

    private static Size Measured(Control child, Size availableSize)
    {
        child.Measure(availableSize);
        return child.DesiredSize;
    }
}
