using System.Text.Json;
using Avalonia.Headless.XUnit;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task RetryFilingUsesCurrentSettingsForEachSourceAfterARepeatedFailureAndReopening()
    {
        const string intakeFolder = "Filed";
        const string addedFilename = "Added receipt.pdf";
        const string watchedFilename = "Watched ledger.pdf";
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var inputs = new TemporaryLibrary();
        using var scannerOutput = new TemporaryLibrary();
        string pdfs = Path.Combine(library.Path, "PDFs");
        Directory.CreateDirectory(Path.Combine(pdfs, intakeFolder));
        string addedSource = Path.Combine(inputs.Path, addedFilename);
        string watchedSource = Path.Combine(scannerOutput.Path, watchedFilename);
        File.WriteAllText(addedSource, "Incomplete receipt PDF");
        File.WriteAllText(watchedSource, "Incomplete watched PDF");
        var store = new ImportFailureStore(settings.Path);
        var intake = new IntakeFolderPreference(settings.Path);
        intake.Save(library.Path, intakeFolder);
        var watch = new WatchFolderPreference(settings.Path);
        watch.Save(library.Path, scannerOutput.Path);

        using (var initial = OpenLibrary(new FixtureDialogs(addedSource)))
        {
            initial.IncludeWatchedScans = true;
            await initial.AddFilesCommand.ExecuteAsync(null);
            await initial.ImportWatchFolderFilesCommand.ExecuteAsync(null);
            try
            {
                await WaitForAsync(() => !initial.IsBusy && initial.ImportFailures.Count == 2);
            }
            finally { await initial.PrepareCloseAsync(); }
        }

        using (var failedRetry = OpenLibrary(new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Retry }))
        {
            Assert.True(failedRetry.IncludeWatchedScans);
            await failedRetry.InspectImportErrorsCommand.ExecuteAsync(null);
            Assert.Equal(2, failedRetry.ImportFailures.Count);
            Assert.Empty(failedRetry.Documents);
            failedRetry.IncludeWatchedScans = false;
        }

        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        File.Copy(Path.Combine(fixtures, "Lease Renewal.pdf"), addedSource, overwrite: true);
        File.Copy(Path.Combine(fixtures, "Match Ledger.pdf"), watchedSource, overwrite: true);
        using var reopened = OpenLibrary(new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Retry });
        Assert.False(reopened.IncludeWatchedScans);
        Assert.Equal(2, reopened.ImportFailures.Count);
        await reopened.InspectImportErrorsCommand.ExecuteAsync(null);
        await WaitForPreviewAsync(reopened);

        Assert.Equal("2 documents added.", reopened.Status);
        Assert.Equal(2, reopened.Documents.Count);
        Assert.Contains(reopened.Documents, document => document.RelativePath == $"{intakeFolder}/{addedFilename}" && document.HasText);
        Assert.Contains(reopened.Documents, document => document.RelativePath == watchedFilename && document.HasText);
        Assert.Empty(reopened.ImportFailures);
        Assert.Empty(store.Load(library.Path));
        Assert.True(File.Exists(addedSource) && File.Exists(watchedSource));

        InboxViewModel OpenLibrary(FixtureDialogs dialogs)
        {
            var model = new InboxViewModel(dialogs, watchPreference: watch, importFailureStore: store,
                intakeFolderPreference: intake);
            model.InitializeLibrary(() => library.Path);
            return model;
        }
    }

    [AvaloniaFact]
    public async Task RetryFilingUsesTheIntakeFolderForAnOlderErrorReport()
    {
        const string intakeFolder = "Filed";
        const string filename = "Older error.pdf";
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var inputs = new TemporaryLibrary();
        string source = Path.Combine(inputs.Path, filename);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf"), source);
        Directory.CreateDirectory(Path.Combine(library.Path, "PDFs", intakeFolder));
        var intake = new IntakeFolderPreference(settings.Path);
        intake.Save(library.Path, intakeFolder);
        var store = new ImportFailureStore(settings.Path);
        store.Save(library.Path, []);
        string reportPath = Assert.Single(Directory.GetFiles(settings.Path, "import-errors-*.json"));
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new[] { new { SourcePath = source, Reason = "Unfinished PDF" } }));

        var dialogs = new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Retry };
        using var model = new InboxViewModel(dialogs, importFailureStore: store, intakeFolderPreference: intake);
        model.InitializeLibrary(() => library.Path);
        Assert.Equal(source, Assert.Single(model.ImportFailures).SourcePath);
        await model.InspectImportErrorsCommand.ExecuteAsync(null);
        await WaitForPreviewAsync(model);

        Assert.Equal($"{intakeFolder}/{filename}", Assert.Single(model.Documents).RelativePath);
        Assert.Empty(store.Load(library.Path));
    }
}
