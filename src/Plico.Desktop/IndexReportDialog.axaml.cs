using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

internal sealed record IndexReportLine(string Label, string Detail);

/// <summary>A snapshot of unresolved paths and the latest refresh notices for an open library.</summary>
internal sealed record IndexReport(IReadOnlyList<string> UnavailablePaths, IReadOnlyList<string> Notices);

/// <summary>Displays unavailable paths and the latest refresh notices.</summary>
internal sealed partial class IndexReportDialog : Window
{
    internal IndexReportDialog(IndexReport report)
    {
        InitializeComponent();
        var entries = new List<IndexReportLine>(report.UnavailablePaths.Count + report.Notices.Count);
        foreach (string path in report.UnavailablePaths) entries.Add(new("Unavailable", path));
        foreach (string notice in report.Notices) entries.Add(new("Notice", notice));
        IndexReportList.ItemsSource = entries;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
