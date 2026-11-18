using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed record IndexReportLine(string Label, string Detail);

/// <summary>Displays the affected paths and notices from one completed refresh.</summary>
internal sealed partial class IndexReportDialog : Window
{
    internal IndexReportDialog(RefreshResult result)
    {
        InitializeComponent();
        var entries = new List<IndexReportLine>(result.UnavailablePaths.Count + result.Warnings.Count);
        foreach (string path in result.UnavailablePaths) entries.Add(new("Unavailable", path));
        foreach (string notice in result.Warnings) entries.Add(new("Notice", notice));
        IndexReportList.ItemsSource = entries;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
