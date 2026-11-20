using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// A small paper thumbnail of a document's first page for list rows. Renders lazily through the window's
/// view model, so scrolling a long list only renders the rows that appear.
/// </summary>
public sealed class DocumentThumbnail : Border
{
    /// <summary>The document whose first page to show.</summary>
    public static readonly StyledProperty<DocumentSummary?> DocumentProperty =
        AvaloniaProperty.Register<DocumentThumbnail, DocumentSummary?>(nameof(Document));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private CancellationTokenSource? _request;

    static DocumentThumbnail()
    {
        ClipToBoundsProperty.OverrideDefaultValue<DocumentThumbnail>(true);
    }

    public DocumentThumbnail()
    {
        Child = _image;
    }

    public DocumentSummary? Document { get => GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty) Load();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Load();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Cancel();
    }

    private void Load()
    {
        Cancel();
        _image.Source = null;
        if (Document is not { PageCount: > 0 } document || TopLevel.GetTopLevel(this)?.DataContext is not InboxViewModel model) return;
        if (model.TryGetCachedThumbnail(document) is { } cached)
        {
            _image.Source = cached;
            return;
        }
        var request = new CancellationTokenSource();
        _request = request;
        _ = ShowAsync(model, document, request);
    }

    private async Task ShowAsync(InboxViewModel model, DocumentSummary document, CancellationTokenSource request)
    {
        try
        {
            var image = await model.GetDocumentThumbnailAsync(document, request.Token);
            if (!request.IsCancellationRequested && ReferenceEquals(Document, document)) _image.Source = image;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_request == request) _request = null;
            request.Dispose();
        }
    }

    private void Cancel()
    {
        _request?.Cancel();
        _request = null;
    }
}
