using Avalonia.Media.Imaging;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class FixtureDialogs
{
    internal Func<int, int, Func<int, CancellationToken, Task<Bitmap>>, Task<IReadOnlyList<PdfRedactionRegion>?>>? RedactionChoice { get; set; }
    internal Func<int, int, Task<PdfStampRequest?>>? StampChoice { get; set; }
    internal PdfCompressionMode? CompressionChoice { get; set; }
    internal Func<AppearanceOptions, Task<AppearanceOptions?>>? AppearanceChoice { get; set; }

    public Task<IReadOnlyList<PdfRedactionRegion>?> MarkPdfRedactionsAsync(int pageCount, int currentPage,
        Func<int, CancellationToken, Task<Bitmap>> preview) =>
        RedactionChoice?.Invoke(pageCount, currentPage, preview) ?? Task.FromResult<IReadOnlyList<PdfRedactionRegion>?>(null);

    public Task<PdfStampRequest?> ChoosePdfStampAsync(int pageCount, int currentPage) =>
        StampChoice?.Invoke(pageCount, currentPage) ?? Task.FromResult<PdfStampRequest?>(null);

    public Task<PdfCompressionMode?> ChoosePdfCompressionAsync() => Task.FromResult(CompressionChoice);

    public Task<AppearanceOptions?> ChooseAppearanceAsync(AppearanceOptions current) =>
        AppearanceChoice?.Invoke(current) ?? Task.FromResult<AppearanceOptions?>(null);
}
