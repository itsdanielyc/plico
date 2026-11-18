using NAPS2.Images;
using NAPS2.Scan;

namespace Plico.Core;

/// <summary>Splits at PATCH-T or an optional additional barcode value, omitting each separator sheet.</summary>
public sealed record ScanBatchOptions(bool SplitOnSeparators, string? SeparatorBarcode = null);

/// <summary>A pending first pass is ready for its back sides.</summary>
public sealed record ManualDuplexPrompt(int FrontPageCount);

/// <summary>Determines whether to capture the backs, keep the fronts for later, or discard them.</summary>
public enum ManualDuplexDecision { ScanBacks, SaveForLater, Discard }

/// <summary>The cataloged PDFs, an optional capture error, and whether a front pass is saved for later.</summary>
public sealed record ScanBatchOutcome(IReadOnlyList<DocumentSummary> Documents, ScanInterruption? Interruption = null,
    bool HasPendingDuplex = false);

internal static class ScanBatchPages
{
    internal const int MaximumSeparatorLength = 128;

    internal static BarcodeDetectionOptions BarcodeOptions(ScanBatchOptions? options) => new()
    {
        DetectBarcodes = options?.SplitOnSeparators == true,
        PatchTOnly = string.IsNullOrEmpty(options?.SeparatorBarcode)
    };

    // O(n) time and space for n page references.
    internal static List<ProcessedImage> Interleave(IReadOnlyList<ProcessedImage> fronts, IReadOnlyList<ProcessedImage> backs)
    {
        RequireMatchingPasses(fronts.Count, backs.Count);
        var ordered = new List<ProcessedImage>(checked(fronts.Count * 2));
        for (int index = 0; index < fronts.Count; index++)
        {
            ordered.Add(fronts[index]);
            ordered.Add(backs[backs.Count - 1 - index]);
        }
        return ordered;
    }

    internal static void RequireMatchingPasses(int frontCount, int backCount)
    {
        if (frontCount == backCount && frontCount != 0) return;
        throw new InvalidDataException($"The scanner returned {backCount} back sides for {frontCount} fronts. " +
            "The fronts are saved. Reload the whole stack and scan the backs again.");
    }

    // O(n) time and space for n page references. Each separator ends the current document.
    internal static List<ICollection<ProcessedImage>> Split(List<ProcessedImage> pages, ScanBatchOptions? options)
    {
        if (options?.SplitOnSeparators != true) return pages.Count == 0 ? [] : [pages];

        var documents = new List<ICollection<ProcessedImage>>();
        var current = new List<ProcessedImage>();
        foreach (var page in pages)
        {
            Barcode barcode = page.PostProcessingData.Barcode;
            if (!barcode.IsPatchT && !(barcode.IsDetected && options.SeparatorBarcode is { Length: > 0 } separator &&
                string.Equals(barcode.DetectedText, separator, StringComparison.Ordinal)))
            {
                current.Add(page);
                continue;
            }
            if (current.Count == 0) continue;
            documents.Add(current);
            current = [];
        }
        if (current.Count != 0) documents.Add(current);
        return documents;
    }
}
