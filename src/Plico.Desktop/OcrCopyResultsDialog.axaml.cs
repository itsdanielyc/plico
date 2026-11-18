using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

internal enum OcrCopyOutcome { Added, Reused, Failed, Unprocessed }

/// <summary>The outcome and detail associated with one source PDF in an OCR run.</summary>
internal sealed record OcrCopyBatchItem(string SourceTitle, string SourcePath, OcrCopyOutcome Outcome, string Detail)
{
    public string OutcomeLabel => Outcome switch
    {
        OcrCopyOutcome.Added => "Added to Inbox",
        OcrCopyOutcome.Reused => "Existing copy reused",
        OcrCopyOutcome.Failed => "Failed",
        _ => "Not processed"
    };
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>Shows the completed and remaining work for a selection of PDFs.</summary>
internal sealed partial class OcrCopyResultsDialog : Window
{
    internal OcrCopyResultsDialog(IReadOnlyList<OcrCopyBatchItem> results)
    {
        InitializeComponent();
        OcrResultsList.ItemsSource = results;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
