using System.Text;
using NAPS2.Images;
using NAPS2.Images.Skia;
using NAPS2.ImportExport;
using NAPS2.Ocr;
using NAPS2.Pdf;
using NAPS2.Scan;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using ReadablePdf = UglyToad.PdfPig.PdfDocument;
using ReadablePage = UglyToad.PdfPig.Content.Page;

namespace Plico.Core;

/// <summary>Produces a plain PDF and verifies the text that was actually embedded.</summary>
internal sealed class DocumentProcessor(string dataDirectory)
{
    internal const string PdfExtension = ".pdf";
    private const int OcrTimeoutSecondsPerPage = 90;
    private const int PreviewPixelLimit = 900;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg"
    };

    internal static bool IsPdf(string extension) => string.Equals(extension, PdfExtension, StringComparison.OrdinalIgnoreCase);

    internal static bool SupportsExtension(string extension) => IsPdf(extension) || ImageExtensions.Contains(extension);

    internal async Task<PreparedPdf> PrepareAsync(string stagedSource, string outputPath,
        OcrLanguage language, CancellationToken cancellationToken)
    {
        string stagingDirectory = Path.GetDirectoryName(outputPath)!;
        bool isPdf = IsPdf(Path.GetExtension(stagedSource));
        if (isPdf)
        {
            var existing = ReadPdf(stagedSource, cancellationToken);
            if (existing.HasTextOnEveryPage)
            {
                File.Move(stagedSource, outputPath);
                return new(existing, CopiedSource: true);
            }
        }

        using var context = CreateCaptureContext(stagingDirectory);
        var pages = isPdf
            ? new PdfImporter(context).Import(stagedSource, progress: cancellationToken)
            : new ImageImporter(context).Import(stagedSource, progress: cancellationToken);
        var text = await ExportAsync(context, pages, outputPath, language, cancellationToken).ConfigureAwait(false);
        return new(text, CopiedSource: false);
    }

    internal async Task<PdfText> ExportAsync(
        ScanningContext context,
        IAsyncEnumerable<ProcessedImage> pages,
        string outputPath,
        OcrLanguage language,
        CancellationToken cancellationToken)
    {
        var captured = new List<ProcessedImage>();
        await foreach (var image in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            captured.Add(image);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (captured.Count == 0)
        {
            throw new InvalidDataException("No pages could be read. Check that the file or scanner has a page.");
        }

        string languageCode = language switch
        {
            OcrLanguage.English => "eng",
            OcrLanguage.French => "fra",
            OcrLanguage.German => "deu",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown OCR language.")
        };
        string modelFile = Path.Combine(dataDirectory, $"{languageCode}.traineddata");
        if (!File.Exists(modelFile))
        {
            throw new FileNotFoundException($"The bundled {language} OCR data is missing. Extract a fresh copy of Plico.", modelFile);
        }
        context.OcrEngine = TesseractOcrEngine.Bundled(dataDirectory);
        if (!await new PdfExporter(context).Export(
            outputPath, captured, new PdfExportParams { Metadata = new PdfMetadata { Creator = "Plico" } },
            new OcrParams(languageCode, TimeoutInSeconds: OcrTimeoutSecondsPerPage),
            cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Plico could not write the processed PDF.");
        }
        return ReadPdf(outputPath, cancellationToken);
    }

    internal static ScanningContext CreateCaptureContext(string stagingDirectory) => new(new SkiaImageContext())
    {
        FileStorageManager = new FileStorageManager(stagingDirectory),
        TempFolderPath = stagingDirectory
    };

    // O(pages + text length) time and O(text length) retained text, in addition to PDF parser state.
    internal static PdfText ReadPdf(string path, CancellationToken cancellationToken) => WithReadablePdf(path, pdf =>
    {
        bool everyPageHasText = true;
        var text = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string pageText = ReadPageText(page);
            if (pageText.Length == 0)
            {
                everyPageHasText = false;
                continue;
            }
            if (text.Length > 0) text.AppendLine();
            text.Append(pageText);
        }
        return new PdfText(text.ToString(), pdf.NumberOfPages, everyPageHasText);
    });

    internal static string ReadPageText(ReadablePage page) => ContentOrderTextExtractor.GetText(page).Trim();

    internal static TResult WithReadablePdf<TResult>(string path, Func<ReadablePdf, TResult> read)
    {
        try
        {
            using var pdf = ReadablePdf.Open(path);
            if (pdf.NumberOfPages == 0) throw new InvalidDataException("This PDF has no pages.");
            return read(pdf);
        }
        catch (PdfDocumentEncryptedException error)
        {
            throw new InvalidDataException("This PDF is password-protected. Import an unlocked copy.", error);
        }
        catch (Exception error) when (error is PdfDocumentFormatException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Plico could not read this PDF. Check that it opens in a PDF viewer.", error);
        }
    }

    internal static async Task<MemoryStream> RenderPageAsync(string path, int pageNumber, CancellationToken cancellationToken)
    {
        var options = new ImportParams { Slice = Slice.Item(pageNumber - 1), ThumbnailSize = PreviewPixelLimit };
        using var context = new ScanningContext(new SkiaImageContext());
        var pages = new PdfImporter(context).Import(path, options, cancellationToken);
        await foreach (var page in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            using (page)
            {
                if (page.PostProcessingData.Thumbnail is { } thumbnail)
                {
                    return thumbnail.SaveToMemoryStream(ImageFileFormat.Png);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidDataException($"Page {pageNumber} could not be previewed.");
    }
}
