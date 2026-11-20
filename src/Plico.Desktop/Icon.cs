using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Plico.Desktop;

/// <summary>
/// Draws a stroke icon from the 20-unit icon grid in the inherited text colour, or fills a shape such as the
/// Plico mark. Icons are decorative; give the surrounding control its accessible name.
/// </summary>
public sealed class Icon : Control
{
    /// <summary>The icon geometry, usually an <c>Icon.*</c> or <c>Mark.*</c> resource.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    /// <summary>The stroke or fill colour. Inherits the text colour of the containing control.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    /// <summary>Fills the geometry instead of stroking it, for solid shapes such as the brand mark.</summary>
    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(IsFilled));

    /// <summary>The side of the square design grid the geometry was drawn on.</summary>
    public static readonly StyledProperty<double> GridSizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(GridSize), 20);

    /// <summary>The stroke width in grid units. 1.5 units draw a 1.2 px line at the default 16 px size.</summary>
    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), 1.5);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty, IsFilledProperty, GridSizeProperty, StrokeWidthProperty);
        WidthProperty.OverrideDefaultValue<Icon>(16);
        HeightProperty.OverrideDefaultValue<Icon>(16);
        IsHitTestVisibleProperty.OverrideDefaultValue<Icon>(false);
        FocusableProperty.OverrideDefaultValue<Icon>(false);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public bool IsFilled { get => GetValue(IsFilledProperty); set => SetValue(IsFilledProperty, value); }
    public double GridSize { get => GetValue(GridSizeProperty); set => SetValue(GridSizeProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } geometry || Foreground is not { } brush || GridSize <= 0) return;
        var bounds = Bounds;
        double scale = Math.Min(bounds.Width, bounds.Height) / GridSize;
        if (scale <= 0) return;
        double offsetX = (bounds.Width - GridSize * scale) / 2;
        double offsetY = (bounds.Height - GridSize * scale) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            if (IsFilled)
            {
                context.DrawGeometry(brush, null, geometry);
                return;
            }
            var pen = new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
