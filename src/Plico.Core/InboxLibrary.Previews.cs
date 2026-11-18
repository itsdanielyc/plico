namespace Plico.Core;

/// <summary>A rendered PDF page and its visible width and height in device-independent pixels.</summary>
public sealed record ScaledPdfPreview(MemoryStream Image, double DisplayWidth, double DisplayHeight);

public sealed partial class InboxLibrary
{
    private const int ThumbnailPixels = 158;
    private const int MaximumDisplayPixels = 4096;
    private const int MaximumPreviewZoomPercent = 300;
    private const int MinimumPreviewZoomPercent = 50;
    private const double DeviceIndependentPixelsPerPoint = 96d / 72;
    private const double QualityScale = 1.5;

    /// <summary>Renders a small page PNG suitable for navigation thumbnails. Callers own the stream.</summary>
    public Task<MemoryStream> RenderPageThumbnailAsync(DocumentSummary document, int pageNumber, CancellationToken cancellationToken = default)
    {
        ValidatePreviewPage(document, pageNumber);
        return RenderPageAsync(document, pageNumber, (path, page, token) =>
            PdfPreviewRenderer.RenderPageAsync(path, page, ThumbnailPixels, token), cancellationToken);
    }

    /// <summary>Renders a page for manual zoom and returns its physical display dimensions. Callers own the stream.</summary>
    public async Task<ScaledPdfPreview> RenderScaledPdfPageAsync(DocumentSummary document, int pageNumber, int zoomPercent,
        CancellationToken cancellationToken = default)
    {
        ValidatePreviewPage(document, pageNumber);
        ArgumentOutOfRangeException.ThrowIfLessThan(zoomPercent, MinimumPreviewZoomPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zoomPercent, MaximumPreviewZoomPercent);
        await _pdfFileAccess.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = ResolvePdf(document);
            var dimensions = DocumentProcessor.WithReadablePdf(path, pdf =>
            {
                var page = pdf.GetPage(pageNumber);
                double scale = DeviceIndependentPixelsPerPoint * zoomPercent / 100;
                return (Width: page.Width * scale, Height: page.Height * scale);
            });
            double longest = Math.Max(dimensions.Width, dimensions.Height);
            if (!double.IsFinite(longest) || longest <= 0 || dimensions.Width <= 0 || dimensions.Height <= 0)
                throw new InvalidDataException("The PDF page has invalid dimensions.");
            int pixelLimit = (int)Math.Clamp(Math.Ceiling(longest * QualityScale), 1, MaximumDisplayPixels);
            var image = await PdfPreviewRenderer.RenderPageAsync(path, pageNumber, pixelLimit, cancellationToken).ConfigureAwait(false);
            return new ScaledPdfPreview(image, dimensions.Width, dimensions.Height);
        }
        finally
        {
            _pdfFileAccess.Release();
        }
    }

    private static void ValidatePreviewPage(DocumentSummary document, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, document.PageCount);
    }
}
