using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Plico.Desktop;

/// <summary>
/// The keyboard focus ring: a 2 px ink ring outside a 2 px gap, following the focused control's corners, as in
/// CSS <c>0 0 0 2px panel, 0 0 0 4px fg</c>. Used as the adorner layer's default focus adorner with a margin of
/// minus <see cref="Gap"/> plus <see cref="RingThickness"/>, so it draws entirely outside the control.
/// </summary>
public sealed class FocusRing : Control
{
    /// <summary>The ring colour, normally the <c>Fg</c> ink.</summary>
    public static readonly StyledProperty<IBrush?> RingProperty =
        AvaloniaProperty.Register<FocusRing, IBrush?>(nameof(Ring));

    /// <summary>The colour of the gap between the control and the ring, normally <c>Panel</c>.</summary>
    public static readonly StyledProperty<IBrush?> GapBrushProperty =
        AvaloniaProperty.Register<FocusRing, IBrush?>(nameof(GapBrush));

    /// <summary>The width of the ring in device-independent pixels.</summary>
    public static readonly StyledProperty<double> RingThicknessProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(RingThickness), 2);

    /// <summary>The width of the gap between the control's edge and the ring.</summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(Gap), 2);

    static FocusRing()
    {
        AffectsRender<FocusRing>(RingProperty, GapBrushProperty, RingThicknessProperty, GapProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<FocusRing>(false);
        FocusableProperty.OverrideDefaultValue<FocusRing>(false);
    }

    public IBrush? Ring { get => GetValue(RingProperty); set => SetValue(RingProperty, value); }
    public IBrush? GapBrush { get => GetValue(GapBrushProperty); set => SetValue(GapBrushProperty, value); }
    public double RingThickness { get => GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    public override void Render(DrawingContext context)
    {
        double ring = RingThickness, gap = Gap;
        var outer = new Rect(Bounds.Size);
        if (outer.Width <= 2 * (ring + gap) || outer.Height <= 2 * (ring + gap)) return;
        var corners = AdornerLayer.GetAdornedElement(this) switch
        {
            TemplatedControl control => control.CornerRadius,
            Border border => border.CornerRadius,
            _ => default
        };
        if (GapBrush is { } gapBrush && gap > 0)
            Stroke(context, gapBrush, gap, outer.Deflate(ring + gap / 2), corners, gap / 2);
        if (Ring is { } ringBrush && ring > 0)
            Stroke(context, ringBrush, ring, outer.Deflate(ring / 2), corners, gap + ring / 2);
    }

    /// <summary>Strokes a rectangle whose corners grow with the distance from the control's edge, as a CSS spread does.</summary>
    private static void Stroke(DrawingContext context, IBrush brush, double thickness, Rect rect, CornerRadius corners, double spread)
    {
        static double Grow(double radius, double by) => radius > 0 ? radius + by : 0;
        var rounded = new RoundedRect(rect, Grow(corners.TopLeft, spread), Grow(corners.TopRight, spread),
            Grow(corners.BottomRight, spread), Grow(corners.BottomLeft, spread));
        context.DrawRectangle(null, new Pen(brush, thickness), rounded);
    }
}
