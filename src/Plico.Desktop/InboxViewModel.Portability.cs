using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task CheckIntegrityAsync() => RunOperationAsync(DesktopText.Translate("Checking the library…"), async token =>
    {
        var library = RequireLibrary();
        var report = await Task.Run(() => library.CheckIntegrity(token), token);
        await _dialogs.ShowIntegrityReportAsync(report);
        Status = report.Issues.Count == 0 ? DesktopText.Translate("Integrity check found no issues.") : DesktopText.Translate("Integrity check complete.");
    });

    [RelayCommand]
    private Task ExportMetadataAsync() => ExportReportAsync(csv: false);

    [RelayCommand]
    private Task ExportSearchCsvAsync() => ExportReportAsync(csv: true);

    private Task ExportReportAsync(bool csv)
    {
        var filter = CurrentFilter();
        return RunOperationAsync(DesktopText.Translate("Choose an export destination…"), async token =>
        {
            var library = RequireLibrary();
            string title = csv ? "Choose where to save search results" : "Choose where to save library metadata";
            string? directory = await _dialogs.PickFolderAsync(title);
            if (directory is null)
            {
                Status = DesktopText.Translate("Export cancelled.");
                return;
            }
            Status = csv ? DesktopText.Translate("Exporting search results…") : DesktopText.Translate("Exporting metadata…");
            string path = await Task.Run(() => csv
                ? library.ExportSearchCsvAsync(filter, directory, token)
                : library.ExportMetadataAsync(directory, token), token);
            LatestExportLocation = Path.GetDirectoryName(path);
            Status = DesktopText.Format($"Exported {Path.GetFileName(path)}.");
        });
    }
}
