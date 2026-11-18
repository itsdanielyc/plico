using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Shows the migrated records and offers a structured report for unresolved source fields.</summary>
internal sealed partial class MigrationReportDialog : Window
{
    private readonly LibraryMigrationReport _report;

    internal MigrationReportDialog(LibraryMigrationReport report)
    {
        _report = report;
        InitializeComponent();
        string outcome = report.Cancelled ? "Paused" : "Finished";
        MigrationSummary.Text = $"{outcome} · {report.Added} added · {report.Reused} reused · {report.Failed} failed";
        MigrationNotices.Text = report.Notices.Count == 0 ? "No migration notices." : string.Join(
            Environment.NewLine + Environment.NewLine, report.Notices.Select(notice =>
                $"{notice.Record} · {notice.Field}{Environment.NewLine}{notice.Message}" +
                (notice.OriginalValue is { } original ? $"{Environment.NewLine}{original}" : "")));
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save migration report",
                SuggestedFileName = "plico-migration-report.json",
                DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("JSON report") { Patterns = ["*.json"] }]
            });
            string? path = file?.TryGetLocalPath();
            if (path is null) return;
            string json = JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true });
            ApplicationSettings.WriteAtomically(path, json);
            MigrationSaveStatus.Text = "Report saved.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MigrationSaveStatus.Text = error.Message;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
