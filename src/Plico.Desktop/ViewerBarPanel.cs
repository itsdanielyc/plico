using Avalonia;
using Avalonia.Controls;

namespace Plico.Desktop;

/// <summary>
/// Lays out the viewer bar: the match chevrons at the left and the fit, thumbnail, zoom and open buttons at the right,
/// on one row when both fit beside each other with <see cref="Spacing"/> between them. When they do not (a long fit
/// label in a narrow page column), the right group moves to a second row, still at the right, so no two buttons ever
/// overlap. A hidden group takes no room. O(1) per layout.
/// </summary>
internal sealed class ViewerBarPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ViewerBarPanel, double>(nameof(Spacing), 8);

    static ViewerBarPanel()
    {
        AffectsMeasure<ViewerBarPanel>(SpacingProperty);
    }

    /// <summary>The least gap between the two groups on one row.</summary>
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children is not [var lead, var trail, ..]) return Children is [var only] ? Measured(only, availableSize) : default;
        lead.Measure(availableSize);
        trail.Measure(availableSize);
        double width = availableSize.Width;
        if (FitsOneRow(lead, trail, width))
        {
            return new Size(lead.DesiredSize.Width + Gap(lead, trail) + trail.DesiredSize.Width,
                Math.Max(lead.DesiredSize.Height, trail.DesiredSize.Height));
        }
        return new Size(Math.Max(lead.DesiredSize.Width, trail.DesiredSize.Width), lead.DesiredSize.Height + trail.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children is not [var lead, var trail, ..])
        {
            if (Children is [var only]) only.Arrange(new Rect(finalSize));
            return finalSize;
        }
        double trailWidth = Math.Min(trail.DesiredSize.Width, finalSize.Width);
        if (FitsOneRow(lead, trail, finalSize.Width))
        {
            double row = finalSize.Height;
            lead.Arrange(new Rect(0, 0, Math.Min(lead.DesiredSize.Width, finalSize.Width), row));
            trail.Arrange(new Rect(finalSize.Width - trailWidth, 0, trailWidth, row));
            return finalSize;
        }
        double leadHeight = lead.DesiredSize.Height;
        lead.Arrange(new Rect(0, 0, Math.Min(lead.DesiredSize.Width, finalSize.Width), leadHeight));
        trail.Arrange(new Rect(finalSize.Width - trailWidth, leadHeight, trailWidth, Math.Max(0, finalSize.Height - leadHeight)));
        return finalSize;
    }

    private bool FitsOneRow(Control lead, Control trail, double width) =>
        !double.IsFinite(width) || lead.DesiredSize.Width + Gap(lead, trail) + trail.DesiredSize.Width <= width + 0.5;

    private double Gap(Control lead, Control trail) => lead.IsVisible && trail.IsVisible ? Spacing : 0;

    private static Size Measured(Control child, Size availableSize)
    {
        child.Measure(availableSize);
        return child.DesiredSize;
    }
}
