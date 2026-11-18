using NAPS2.Images;
using NAPS2.Images.Skia;
using NAPS2.Images.Transforms;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Tokens;

namespace Plico.Core;

/// <summary>Renders a bounded PNG preview in the PDF’s declared page orientation.</summary>
internal static class PdfPreviewRenderer
{
    private const int PreviewPixelLimit = 900;

    /// <summary>Returns the requested one-based page as a PNG stream owned by the caller.</summary>
    internal static Task<MemoryStream> RenderPageAsync(string path, int pageNumber, CancellationToken cancellationToken) =>
        RenderPageAsync(path, pageNumber, PreviewPixelLimit, cancellationToken);

    internal static async Task<MemoryStream> RenderPageAsync(string path, int pageNumber, int pixelLimit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int rotation = ReadRotation(path, pageNumber);
        cancellationToken.ThrowIfCancellationRequested();
        using var pageSnapshot = rotation == 0 ? null : CopyWithoutRotation(path, pageNumber - 1);
        var options = new ImportParams
        {
            Slice = Slice.Item(pageSnapshot is null ? pageNumber - 1 : 0),
            ThumbnailSize = pixelLimit
        };
        using var context = new ScanningContext(new SkiaImageContext());
        var importer = new PdfImporter(context);
        var pages = pageSnapshot is null ? importer.Import(path, options, cancellationToken)
            : importer.Import(pageSnapshot, options, cancellationToken);
        using var preview = await ReadPreviewPageAsync(pages, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (preview?.PostProcessingData.Thumbnail is not { } thumbnail)
        {
            throw new InvalidDataException($"Page {pageNumber} could not be previewed.");
        }
        if (rotation == 0) return thumbnail.SaveToMemoryStream(ImageFileFormat.Png);

        using var snapshotThumbnail = thumbnail.Clone();
        using var oriented = snapshotThumbnail.PerformTransform(new RotationTransform(rotation));
        return oriented.SaveToMemoryStream(ImageFileFormat.Png);
    }

    private static async Task<ProcessedImage?> ReadPreviewPageAsync(IAsyncEnumerable<ProcessedImage> pages,
        CancellationToken cancellationToken)
    {
        ProcessedImage? preview = null;
        try
        {
            // NAPS2 delivers the page before its producer closes the PDF. Wait for the producer to finish before moving files.
            await foreach (var page in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (preview is null && page.PostProcessingData.Thumbnail is not null) preview = page;
                else page.Dispose();
            }
            return preview;
        }
        catch
        {
            preview?.Dispose();
            throw;
        }
    }

    private static int ReadRotation(string path, int pageNumber) => DocumentProcessor.WithReadablePdf(path, pdf =>
    {
        pdf.AddPageFactory(PageRotationReader.Instance);
        int angle = (int)RotationTransform.NormalizeAngle(pdf.GetPage<int>(pageNumber));
        return Enum.IsDefined((PdfPageRotation)angle) ? angle : 0;
    });

    private static MemoryStream CopyWithoutRotation(string path, int pageIndex)
    {
        // NAPS2 can return a full-page scan’s original pixels when its dimensions match the rotated page.
        // Clearing the turn in the preview snapshot makes the thumbnail’s orientation unambiguous.
        using var original = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        using var snapshot = new PdfDocument { Version = original.Version };
        snapshot.Pages.InsertRange(0, original, pageIndex, 1, AnnotationCopyingType.DeepCopy);
        snapshot.Pages[0].Rotate = 0;
        var stream = new MemoryStream();
        try
        {
            snapshot.Save(stream, closeStream: false);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Reads the inherited rotation from the page tree without decoding the page’s content.</summary>
    private sealed class PageRotationReader : IPageFactory<int>
    {
        internal static readonly PageRotationReader Instance = new();

        public int Create(int pageNumber, DictionaryToken dictionary, PageTreeMembers pageTreeMembers,
            NamedDestinations namedDestinations) => pageTreeMembers.Rotation;
    }
}
