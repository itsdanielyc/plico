using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand] private Task MigratePaperlessFolderAsync() => MigrateAsync(LibraryMigrationFormat.Paperless, isFolder: true);
    [RelayCommand] private Task MigratePaperlessZipAsync() => MigrateAsync(LibraryMigrationFormat.Paperless, isFolder: false);
    [RelayCommand] private Task MigrateEvernoteAsync() => MigrateAsync(LibraryMigrationFormat.Evernote, isFolder: false);
    [RelayCommand] private Task MigrateMarinerAsync() => MigrateAsync(LibraryMigrationFormat.Mariner, isFolder: true);

    private Task MigrateAsync(LibraryMigrationFormat format, bool isFolder) =>
        RunOperationAsync("Choose the export to migrate…", async token =>
        {
            var library = RequireLibrary();
            string? source = await _dialogs.PickMigrationSourceAsync(format, isFolder);
            if (source is null)
            {
                Status = "Migration closed.";
                return;
            }
            token.ThrowIfCancellationRequested();
            var language = RecognitionLanguage;
            string? filingFolder = IntakeDestination(library);
            Status = "Migrating documents…";
            var report = await Task.Run(() => library.MigrateLibraryAsync(source, format, token, language, filingFolder), token);
            RefreshInboxCount();
            Tags = library.GetTags();
            Navigate(inboxOnly: false);
            await _dialogs.ShowMigrationReportAsync(report);
            Status = report.Cancelled ? "Migration paused. Completed documents are in the library." : "Migration report ready.";
        });
}
