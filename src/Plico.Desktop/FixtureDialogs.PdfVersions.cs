using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class FixtureDialogs
{
    internal Func<DocumentSummary, int, Task<PdfPageEditRequest?>>? PageEditChoice { get; set; }
    internal Func<DocumentSummary, IReadOnlyList<PdfVersion>, Task<PdfVersionDecision?>>? VersionChoice { get; set; }
    internal string? OpenPdfChoice { get; set; }
    internal string? PdfPassword { get; set; }

    public Task<PdfPageEditRequest?> EditPdfPagesAsync(DocumentSummary document, int currentPage) =>
        PageEditChoice?.Invoke(document, currentPage) ?? Task.FromResult<PdfPageEditRequest?>(null);

    public Task<PdfVersionDecision?> ChoosePdfVersionAsync(DocumentSummary document, IReadOnlyList<PdfVersion> versions) =>
        VersionChoice?.Invoke(document, versions) ?? Task.FromResult<PdfVersionDecision?>(null);

    public Task<string?> PickPdfAsync(string title) => Task.FromResult(OpenPdfChoice);

    public Task<string?> GetPdfPasswordAsync(string sourcePath) => Task.FromResult(PdfPassword);
}
