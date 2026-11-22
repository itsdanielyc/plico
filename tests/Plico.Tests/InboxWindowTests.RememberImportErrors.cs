using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ReopeningRestoresImportErrorsAndRetryAndClearPersist()
    {
        const string invalidPdf = "Invalid test PDF";
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var inputs = new TemporaryLibrary();
        string repaired = Path.Combine(inputs.Path, "Damaged.pdf");
        string stillDamaged = Path.Combine(inputs.Path, "Unreadable.pdf");
        File.WriteAllText(repaired, invalidPdf);
        File.WriteAllText(stillDamaged, invalidPdf);
        var store = new ImportFailureStore(settings.Path);
        using (var first = new InboxViewModel(new FixtureDialogs(repaired, stillDamaged), importFailureStore: store))
        {
            first.InitializeLibrary(() => library.Path);
            await first.AddFilesCommand.ExecuteAsync(null);
            Assert.Equal(2, first.ImportFailures.Count);
        }

        var retry = new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Retry };
        using (var reopened = new InboxViewModel(retry, importFailureStore: new ImportFailureStore(settings.Path)))
        {
            reopened.InitializeLibrary(() => Path.Combine(library.Path, "."));
            var window = new MainWindow { DataContext = reopened };
            window.Show();
            try
            {
                var button = Assert.IsType<Button>(window.FindControl<Button>("ImportErrorsButton"));
                Assert.True(button.IsVisible);
                Assert.Equal(new[] { repaired, stillDamaged }, reopened.ImportFailures.Select(error => error.SourcePath));

                File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf"), repaired, overwrite: true);
                await reopened.InspectImportErrorsCommand.ExecuteAsync(null);
                await WaitForPreviewAsync(reopened);
                Assert.Single(reopened.Documents);
                Assert.Equal(stillDamaged, Assert.Single(reopened.ImportFailures).SourcePath);
                Assert.Equal(new[] { repaired, stillDamaged }, retry.InspectedImportFailures.Select(error => error.SourcePath));
            }
            finally
            {
                window.Close();
            }
        }

        using (var afterRetry = new InboxViewModel(new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Clear },
                   importFailureStore: store))
        {
            afterRetry.InitializeLibrary(() => library.Path);
            await WaitForPreviewAsync(afterRetry);
            Assert.Equal(stillDamaged, Assert.Single(afterRetry.ImportFailures).SourcePath);
            await afterRetry.InspectImportErrorsCommand.ExecuteAsync(null);
            Assert.Empty(afterRetry.ImportFailures);
        }

        using var afterClear = new InboxViewModel(new FixtureDialogs(), importFailureStore: store);
        afterClear.InitializeLibrary(() => library.Path);
        await WaitForPreviewAsync(afterClear);
        Assert.Empty(afterClear.ImportFailures);
        Assert.Equal(invalidPdf, File.ReadAllText(stillDamaged));
        Assert.True(File.Exists(repaired));
    }

    [AvaloniaFact]
    public async Task SwitchingLibrariesShowsEachLibrarysOwnImportErrors()
    {
        using var settings = new TemporaryLibrary();
        using var firstLibrary = new TemporaryLibrary();
        using var secondLibrary = new TemporaryLibrary();
        using var inputs = new TemporaryLibrary();
        string firstInput = Path.Combine(inputs.Path, "First damage.pdf");
        string secondInput = Path.Combine(inputs.Path, "Second damage.pdf");
        File.WriteAllText(firstInput, "Invalid PDF A");
        File.WriteAllText(secondInput, "Invalid PDF B");
        var dialogs = new FixtureDialogs(firstInput);
        using var model = new InboxViewModel(dialogs, importFailureStore: new ImportFailureStore(settings.Path));
        model.InitializeLibrary(() => firstLibrary.Path);
        await model.AddFilesCommand.ExecuteAsync(null);
        Assert.Equal(firstInput, Assert.Single(model.ImportFailures).SourcePath);

        dialogs.FolderPath = secondLibrary.Path;
        await model.ChooseLibraryCommand.ExecuteAsync(null);
        Assert.Empty(model.ImportFailures);
        dialogs.SelectedFiles = [secondInput];
        await model.AddFilesCommand.ExecuteAsync(null);
        Assert.Equal(secondInput, Assert.Single(model.ImportFailures).SourcePath);

        dialogs.FolderPath = firstLibrary.Path;
        await model.ChooseLibraryCommand.ExecuteAsync(null);
        Assert.Equal(firstInput, Assert.Single(model.ImportFailures).SourcePath);
        dialogs.FolderPath = secondLibrary.Path;
        await model.ChooseLibraryCommand.ExecuteAsync(null);
        Assert.Equal(secondInput, Assert.Single(model.ImportFailures).SourcePath);
    }

    [AvaloniaFact]
    public void AnInvalidSavedSourceDoesNotPreventOpeningTheLibrary()
    {
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        var store = new ImportFailureStore(settings.Path);
        store.Save(library.Path, []);
        string saved = Assert.Single(Directory.GetFiles(settings.Path, "import-errors-*.json"));
        const string invalid = """[{"SourcePath":"relative.pdf","Reason":"Unreadable"}]""";
        File.WriteAllText(saved, invalid);

        using var model = new InboxViewModel(new FixtureDialogs(), importFailureStore: store);
        model.InitializeLibrary(() => library.Path);

        Assert.True(model.HasLibrary);
        Assert.Empty(model.ImportFailures);
        Assert.Contains("Couldn't load the saved import errors", model.Status, StringComparison.Ordinal);
        Assert.Equal(invalid, File.ReadAllText(saved));
    }

    [AvaloniaFact]
    public async Task ImportErrorsStayVisibleWhenClearingCannotBeSaved()
    {
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = new ImportFailureStore(settingsDirectory);
        var failure = new ImportFailure(Path.Combine(library.Path, "Damaged.pdf"), "Unreadable");
        store.Save(library.Path, [failure]);
        using var model = new InboxViewModel(new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Clear },
            importFailureStore: store);
        model.InitializeLibrary(() => library.Path);
        Assert.Equal(failure, Assert.Single(model.ImportFailures));

        string retainedDirectory = Path.Combine(settings.Path, "Saved before write failure");
        Directory.Move(settingsDirectory, retainedDirectory);
        File.WriteAllText(settingsDirectory, "The settings path is a file.");
        await model.InspectImportErrorsCommand.ExecuteAsync(null);

        Assert.Equal(failure, Assert.Single(model.ImportFailures));
        Assert.Contains("Couldn't save the import errors", model.Status, StringComparison.Ordinal);
        Assert.Equal(failure, Assert.Single(new ImportFailureStore(retainedDirectory).Load(library.Path)));
    }

    [AvaloniaFact]
    public async Task AnImportWhoseErrorsCannotBeSavedLeadsWithTheProblem()
    {
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var inputs = new TemporaryLibrary();
        using (var opened = InboxLibrary.Open(library.Path)) BatchFixtures.AddTwoDocuments(opened, TestContext.Current.CancellationToken);
        // A file that failed to import before is saved as an import error.
        string repaired = Path.Combine(inputs.Path, "Damaged.pdf");
        var store = new ImportFailureStore(settings.Path);
        store.Save(library.Path, [new ImportFailure(repaired, "Unreadable")]);
        using var model = new InboxViewModel(new FixtureDialogs(repaired), importFailureStore: store);
        model.InitializeLibrary(() => library.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            Assert.Equal(repaired, Assert.Single(model.ImportFailures).SourcePath);
            // The file is whole now, but the saved import errors can no longer be written: a folder stands in their place.
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf"), repaired);
            string saved = Assert.Single(Directory.GetFiles(settings.Path, "import-errors-*.json"));
            File.Delete(saved);
            Directory.CreateDirectory(saved);

            ClickAt(window, Named<Button>(window, "AddFilesButton"));
            await WaitForAsync(() => !model.IsBusy && model.InboxCount == 3);
            await WaitForPreviewAsync(model);

            // The document was added, but the list of import errors was not saved: the toast leads with that, with the
            // alert, and the document's arrival follows.
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            Assert.Equal(("Couldn't save the import errors", true),
                (model.ToastTitle, ToastWearsTheAlert(toast)));
            Assert.EndsWith("1 document added.", model.ToastStatus, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WatchedImportErrorsSurviveReopeningWhenTheSourceIsGone()
    {
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        string scan = Path.Combine(source.Path, "Interrupted scan.pdf");
        var store = new ImportFailureStore(settings.Path);
        using (var first = new InboxViewModel(new FixtureDialogs { FolderPath = source.Path }, importFailureStore: store))
        {
            first.InitializeLibrary(() => library.Path);
            await first.StartFolderWatchCommand.ExecuteAsync(null);
            Assert.True(first.IsWatchingFolder);
            try
            {
                File.WriteAllText(scan, "Incomplete PDF");
                await WaitForAsync(() => first.HasImportFailures && !first.IsBusy);
                Assert.Equal(scan, Assert.Single(first.ImportFailures).SourcePath);
            }
            finally
            {
                await first.StopFolderWatchCommand.ExecuteAsync(null);
            }
        }

        File.Delete(scan);
        using var reopened = new InboxViewModel(new FixtureDialogs(), importFailureStore: store);
        reopened.InitializeLibrary(() => library.Path);
        Assert.Equal(scan, Assert.Single(reopened.ImportFailures).SourcePath);
        Assert.Empty(reopened.Documents);
    }
}
