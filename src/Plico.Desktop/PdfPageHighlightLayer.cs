using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Draws search hits over the preview's fitted page.</summary>
internal sealed class PdfPageHighlightLayer : Control
{
    private static readonly IBrush HighlightFill = new SolidColorBrush(Color.FromArgb(108, 255, 194, 35));
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<PdfPageHighlightLayer, IImage?>(nameof(Source));
    public static readonly StyledProperty<IReadOnlyList<PdfPageHighlight>> HighlightsProperty =
        AvaloniaProperty.Register<PdfPageHighlightLayer, IReadOnlyList<PdfPageHighlight>>(nameof(Highlights), []);

    static PdfPageHighlightLayer() => AffectsRender<PdfPageHighlightLayer>(SourceProperty, HighlightsProperty);

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IReadOnlyList<PdfPageHighlight> Highlights
    {
        get => GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not { Size.Width: > 0, Size.Height: > 0 } source || Highlights.Count == 0) return;
        double scale = Math.Min(Bounds.Width / source.Size.Width, Bounds.Height / source.Size.Height);
        double width = source.Size.Width * scale;
        double height = source.Size.Height * scale;
        double x = (Bounds.Width - width) / 2;
        double y = (Bounds.Height - height) / 2;
        foreach (var hit in Highlights)
        {
            context.DrawRectangle(HighlightFill, null,
                new Rect(x + width * hit.X, y + height * hit.Y, width * hit.Width, height * hit.Height));
        }
    }
}
