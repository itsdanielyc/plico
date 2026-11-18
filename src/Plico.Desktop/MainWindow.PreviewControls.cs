using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Plico.Desktop;

public partial class MainWindow
{
    private static readonly Point PageCenter = new(0.5, 0.5);
    private Point _previewCenter = PageCenter;

    private void PageThumbnail_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PdfPageThumbnail thumbnail }) ViewModel?.ShowThumbnail(thumbnail.PageNumber);
    }

    private void UpdatePreviewScrollPosition()
    {
        if (ViewModel?.IsPreviewZoomed != true)
        {
            _previewCenter = PageCenter;
            PreviewScroll.Offset = default;
            return;
        }

        var image = PreviewPage.Source;
        if (image is null)
        {
            _previewCenter = CurrentPreviewCenter();
            return;
        }

        // The scroll extent changes after the new page bitmap and its layout are applied.
        var center = _previewCenter;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(PreviewPage.Source, image) || ViewModel?.IsPreviewZoomed != true) return;
            var extent = PreviewScroll.Extent;
            var viewport = PreviewScroll.Viewport;
            PreviewScroll.Offset = new Vector(
                CenteredOffset(center.X, extent.Width, viewport.Width),
                CenteredOffset(center.Y, extent.Height, viewport.Height));
        }, DispatcherPriority.Background);
    }

    private Point CurrentPreviewCenter()
    {
        var extent = PreviewScroll.Extent;
        var viewport = PreviewScroll.Viewport;
        var offset = PreviewScroll.Offset;
        return new Point(
            extent.Width > viewport.Width ? (offset.X + viewport.Width / 2) / extent.Width : PageCenter.X,
            extent.Height > viewport.Height ? (offset.Y + viewport.Height / 2) / extent.Height : PageCenter.Y);
    }

    private static double CenteredOffset(double position, double extent, double viewport) =>
        Math.Clamp(position * extent - viewport / 2, 0, Math.Max(0, extent - viewport));
}
