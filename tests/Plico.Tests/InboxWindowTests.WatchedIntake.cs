using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task WatchedIntakeChoiceStaysWithTheLibraryAndKeepsExistingPdfs()
    {
        const string intakePath = "Filed/Receipts";
        const string existingFilename = "Already filed.pdf";
        const string waitingFilename = "Waiting ledger.pdf";
        using var first = new TemporaryLibrary();
        using var second = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        string previousSource = Path.Combine(source.Path, existingFilename);
        string waitingSource = Path.Combine(source.Path, waitingFilename);
        File.Copy(Path.Combine(fixtures, "Lease Renewal.pdf"), previousSource);
        File.Copy(Path.Combine(fixtures, "Match Ledger.pdf"), waitingSource);

        using (var library = InboxLibrary.Open(first.Path))
        {
            Directory.CreateDirectory(Path.Combine(library.PdfDirectory, intakePath));
            await library.ImportAsync(previousSource, TestContext.Current.CancellationToken);
        }
        Directory.CreateDirectory(Path.Combine(second.Path, "PDFs", intakePath));
        var intake = new IntakeFolderPreference(settings.Path);
        intake.Save(first.Path, intakePath);
        intake.Save(second.Path, intakePath);
        var watch = new WatchFolderPreference(settings.Path);
        watch.Save(first.Path, source.Path);
        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs, watchPreference: watch, intakeFolderPreference: intake);
        model.InitializeLibrary(() => first.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();

        try
        {
            await WaitForPreviewAsync(model);
            var include = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("IncludeWatchedScansCheckBox"));
            Assert.True(include.IsEffectivelyVisible && include.IsEnabled);
            Assert.Equal(false, include.IsChecked);
            include.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            dialogs.FolderPath = second.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Equal(false, include.IsChecked);
            dialogs.FolderPath = first.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Equal(true, include.IsChecked);

            var catchUp = Assert.IsType<Button>(window.FindControl<Button>("ImportWatchFolderFilesButton"));
            var startup = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("CatchUpOnOpenCheckBox"));
            window.UpdateLayout();
            var railBottom = Assert.IsType<Point>(startup.TranslatePoint(new Point(0, startup.Bounds.Height), window));
            Assert.InRange(railBottom.Y, 0, window.Bounds.Height);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(catchUp.Command).ExecuteAsync(null);
            await WaitForAsync(() => !model.IsBusy && model.Documents.Count == 2);
            await WaitForPreviewAsync(model);
            Assert.Equal("1 document added. 1 document already in the library.", model.Status);
            Assert.Contains(model.Documents, document => document.RelativePath == $"{intakePath}/{waitingFilename}" && document.HasText);
            Assert.Contains(model.Documents, document => document.RelativePath == existingFilename);
            Assert.True(File.Exists(Path.Combine(first.Path, "PDFs", intakePath, waitingFilename)));
            Assert.True(File.Exists(waitingSource));
        }
        finally
        {
            await model.PrepareCloseAsync();
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WatchedIntakeWithAMissingDestinationCanBeRetriedWithoutFilingAtTheRoot()
    {
        const string intakePath = "Scans";
        using var library = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        string pdfs = Path.Combine(library.Path, "PDFs");
        string target = Directory.CreateDirectory(Path.Combine(pdfs, intakePath)).FullName;
        var intake = new IntakeFolderPreference(settings.Path);
        intake.Save(library.Path, intakePath);
        var watch = new WatchFolderPreference(settings.Path);
        watch.Save(library.Path, source.Path);
        var dialogs = new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Retry };
        using var model = new InboxViewModel(dialogs, watchPreference: watch, intakeFolderPreference: intake,
            importFailureStore: new ImportFailureStore(settings.Path));
        model.InitializeLibrary(() => library.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();

        try
        {
            var include = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("IncludeWatchedScansCheckBox"));
            include.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Directory.Delete(target);
            await model.ResumeFolderWatchCommand.ExecuteAsync(null);
            Assert.True(model.IsWatchingFolder);
            string scan = PutScanInFolder(source.Path);
            await WaitForAsync(() => !model.IsBusy && (model.HasImportFailures || model.InboxCount > 0));
            var failure = Assert.Single(model.ImportFailures);
            Assert.Equal(scan, failure.SourcePath);
            Assert.Contains("intake folder is unavailable", failure.Reason, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(pdfs));
            Assert.True(File.Exists(scan));

            Directory.CreateDirectory(target);
            var errors = Assert.IsType<Button>(window.FindControl<Button>("ImportErrorsButton"));
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(errors.Command).ExecuteAsync(null);
            Assert.Empty(model.ImportFailures);
            Assert.Equal($"{intakePath}/{Path.GetFileName(scan)}", Assert.Single(model.Documents).RelativePath);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            await model.PrepareCloseAsync();
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
