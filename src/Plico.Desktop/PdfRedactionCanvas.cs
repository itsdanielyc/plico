using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Shows the visible PDF page and turns pointer drags into page-relative redaction boxes.</summary>
internal sealed class PdfRedactionCanvas : Control
{
    private const double MinimumDragPixels = 4;
    private static readonly IBrush PendingFill = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0));
    private static readonly IPen PendingBorder = new Pen(Brushes.White, 1);
    private Point? _dragStart;
    private Rect? _drag;

    internal Bitmap? Page { get; private set; }
    internal IReadOnlyList<PdfRedactionRegion> Marks { get; private set; } = [];
    internal event Action<Rect>? RegionDrawn;

    internal void ShowPage(Bitmap? page, IReadOnlyList<PdfRedactionRegion> marks)
    {
        Page = page;
        Marks = marks;
        _dragStart = null;
        _drag = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Page is not { } page) return;
        Rect image = ImageBounds();
        if (image.Width <= 0 || image.Height <= 0) return;
        context.DrawImage(page, image);
        foreach (var region in Marks)
            context.DrawRectangle(Brushes.Black, PendingBorder, new Rect(
                image.X + image.Width * region.Left, image.Y + image.Height * region.Top,
                image.Width * (region.Right - region.Left), image.Height * (region.Bottom - region.Top)));
        if (_drag is { } draft) context.DrawRectangle(PendingFill, PendingBorder, draft);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (Page is null || !point.Properties.IsLeftButtonPressed || !ImageBounds().Contains(point.Position)) return;
        _dragStart = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start) return;
        _drag = DragRect(start, e.GetPosition(this), ImageBounds());
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart is not { } start) return;
        var image = ImageBounds();
        var region = DragRect(start, e.GetPosition(this), image);
        _dragStart = null;
        _drag = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
        if (region.Width < MinimumDragPixels || region.Height < MinimumDragPixels) return;
        RegionDrawn?.Invoke(new Rect(new Point((region.Left - image.Left) / image.Width, (region.Top - image.Top) / image.Height),
            new Point((region.Right - image.Left) / image.Width, (region.Bottom - image.Top) / image.Height)));
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragStart = null;
        _drag = null;
        InvalidateVisual();
    }

    private Rect ImageBounds()
    {
        if (Page is not { Size.Width: > 0, Size.Height: > 0 } page) return default;
        double scale = Math.Min(Bounds.Width / page.Size.Width, Bounds.Height / page.Size.Height);
        double width = page.Size.Width * scale;
        double height = page.Size.Height * scale;
        return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
    }

    private static Rect DragRect(Point start, Point end, Rect bounds)
    {
        var clipped = new Point(Math.Clamp(end.X, bounds.Left, bounds.Right), Math.Clamp(end.Y, bounds.Top, bounds.Bottom));
        return new Rect(new Point(Math.Min(start.X, clipped.X), Math.Min(start.Y, clipped.Y)),
            new Point(Math.Max(start.X, clipped.X), Math.Max(start.Y, clipped.Y)));
    }
}
