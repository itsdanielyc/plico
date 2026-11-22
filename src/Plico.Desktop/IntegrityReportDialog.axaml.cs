using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed record IntegrityFinding(string Path, string Issue);

internal sealed partial class IntegrityReportDialog : Window
{
    internal IntegrityReportDialog(IntegrityReport report)
    {
        InitializeComponent();
        int checkedFiles = report.FilesChecked;
        int findings = report.Issues.Count;
        IntegritySummary.Text = $"Checked {checkedFiles} {(checkedFiles == 1 ? "PDF" : "PDFs")}. " +
            (findings == 0 ? "No issues found." : $"{findings} {(findings == 1 ? "issue" : "issues")}.");
        IntegrityIssues.IsVisible = findings > 0;
        IntegrityIssues.ItemsSource = report.Issues.Select(item =>
            new IntegrityFinding(item.InTrash ? $"Trash / {item.RelativePath}" : item.RelativePath, Label(item.Kind))).ToArray();
    }

    private static string Label(IntegrityIssueKind kind) => kind switch
    {
        IntegrityIssueKind.Missing => DesktopText.Translate("PDF missing"),
        IntegrityIssueKind.Unreadable => DesktopText.Translate("Couldn't read the PDF"),
        IntegrityIssueKind.ChecksumMismatch => DesktopText.Translate("PDF changed since indexing"),
        IntegrityIssueKind.NoChecksum => DesktopText.Translate("Checksum unavailable"),
        IntegrityIssueKind.Unindexed => DesktopText.Translate("PDF is outside the index"),
        IntegrityIssueKind.NoSearchableText => DesktopText.Translate("No searchable text"),
        IntegrityIssueKind.IndexPending => DesktopText.Translate("PDF returned since indexing"),
        _ => kind.ToString()
    };

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
