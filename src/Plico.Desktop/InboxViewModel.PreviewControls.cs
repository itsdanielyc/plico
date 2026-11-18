using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed record PdfPageThumbnail(int PageNumber, Bitmap Image);

internal sealed partial class InboxViewModel
{
    private const int VisibleThumbnailCount = 5;
    private const int DefaultZoomIn = 100;
    private const int DefaultZoomOut = 75;
    private static readonly int[] ZoomSteps = [50, 75, 100, 125, 150, 200, 300];

    private CancellationTokenSource? _thumbnailRequest;
    private Task? _thumbnailLoad;
    private long _thumbnailDocument;
    private int _thumbnailWindowStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewZoomed))]
    [NotifyPropertyChangedFor(nameof(IsPreviewScrollable))]
    [NotifyPropertyChangedFor(nameof(PreviewZoomLabel))]
    [NotifyPropertyChangedFor(nameof(PreviewFitAction))]
    [NotifyPropertyChangedFor(nameof(CanZoomIn))]
    [NotifyPropertyChangedFor(nameof(CanZoomOut))]
    private int _previewZoomPercent;

    [ObservableProperty] private double _previewDisplayWidth = double.NaN;
    [ObservableProperty] private double _previewDisplayHeight = double.NaN;
    [ObservableProperty] private bool _showPageThumbnails;
    [ObservableProperty] private IReadOnlyList<PdfPageThumbnail> _pageThumbnails = [];

    public bool IsPreviewZoomed => PreviewZoomPercent != 0;
    public bool IsPreviewScrollable => IsPreviewZoomed || FitPreviewWidth;
    public bool CanZoomIn => PreviewZoomPercent < ZoomSteps[^1];
    public bool CanZoomOut => PreviewZoomPercent != ZoomSteps[0];
    public string PreviewZoomLabel => IsPreviewZoomed ? $"{PreviewZoomPercent}%" : DesktopText.Translate("Fit");

    [RelayCommand] private void ZoomIn() => StepZoom(1, DefaultZoomIn);
    [RelayCommand] private void ZoomOut() => StepZoom(-1, DefaultZoomOut);

    private void StepZoom(int direction, int initial)
    {
        if (!HasPreview || IsPageTextView) return;
        int index = Array.IndexOf(ZoomSteps, PreviewZoomPercent);
        int next = index < 0 ? initial : ZoomSteps[Math.Clamp(index + direction, 0, ZoomSteps.Length - 1)];
        if (next == PreviewZoomPercent) return;
        PreviewZoomPercent = next;
        FitPreviewWidth = false;
        RequestPreview();
    }

    private void ResetManualZoom()
    {
        PreviewZoomPercent = 0;
        PreviewDisplayWidth = double.NaN;
        PreviewDisplayHeight = double.NaN;
    }

    internal void ShowThumbnail(int pageNumber) => ShowPreviewPage(pageNumber);

    [RelayCommand]
    private void TogglePageThumbnails()
    {
        ShowPageThumbnails = !ShowPageThumbnails;
        RefreshPageThumbnails();
    }

    private void RefreshPageThumbnails()
    {
        if (!ShowPageThumbnails || IsPageTextView || _library is not { } library ||
            SelectedDocument is not { PageCount: > 1 } document)
        {
            ClearPageThumbnails();
            return;
        }
        int count = Math.Min(document.PageCount, VisibleThumbnailCount);
        int firstPage = Math.Clamp(_pageNumber - count / 2, 1, document.PageCount - count + 1);
        if (_thumbnailDocument == document.Id && _thumbnailWindowStart == firstPage) return;
        ClearPageThumbnails();
        var request = new CancellationTokenSource();
        _thumbnailRequest = request;
        _thumbnailDocument = document.Id;
        _thumbnailWindowStart = firstPage;
        _thumbnailLoad = LoadThumbnailsAsync(library, document, firstPage, count, request);
    }

    // O(five preview renders) with at most five small bitmaps per request.
    private async Task LoadThumbnailsAsync(InboxLibrary library, DocumentSummary document, int firstPage, int count,
        CancellationTokenSource request)
    {
        var images = new List<PdfPageThumbnail>(count);
        try
        {
            for (int number = firstPage; number < firstPage + count; number++)
            {
                int page = number;
                using var png = await Task.Run(() => library.RenderPageThumbnailAsync(document, page, request.Token), request.Token);
                if (request.IsCancellationRequested) return;
                images.Add(new PdfPageThumbnail(page, new Bitmap(png)));
            }
            if (!request.IsCancellationRequested) PageThumbnails = images.ToArray();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (_thumbnailRequest == request) _thumbnailWindowStart = 0;
        }
        finally
        {
            if (request.IsCancellationRequested || images.Count != count)
                foreach (var image in images) image.Image.Dispose();
            if (_thumbnailRequest == request) _thumbnailRequest = null;
            request.Dispose();
        }
    }

    private void ClearPageThumbnails()
    {
        _thumbnailRequest?.Cancel();
        _thumbnailRequest = null;
        _thumbnailDocument = 0;
        _thumbnailWindowStart = 0;
        var previous = PageThumbnails;
        PageThumbnails = [];
        foreach (var thumbnail in previous) thumbnail.Image.Dispose();
    }
}
