using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ChosenIntakeFolderReceivesAddedDroppedAndRetriedPdfs()
    {
        using var temporary = new TemporaryLibrary();
        using var otherLibrary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var input = new TemporaryLibrary();
        string pdfs;
        string target;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            pdfs = library.PdfDirectory;
            target = Directory.CreateDirectory(Path.Combine(pdfs, "Filed", "Receipts")).FullName;
        }
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var dialogs = new FixtureDialogs(Path.Combine(fixtures, "Lease Renewal.pdf")) { FolderPath = target };
        using var model = new InboxViewModel(dialogs, intakeFolderPreference: new IntakeFolderPreference(settings.Path));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            var choose = Assert.IsType<Button>(OpenSettingsAt(window, "IntakeGroup").FindControl<Button>("IntakeFolderButton"));
            Assert.True(choose.IsEffectivelyVisible);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(choose.Command).ExecuteAsync(null);
            Assert.Equal(pdfs, dialogs.LastFolderStart);
            Assert.Equal("PDFs/Filed/Receipts", model.IntakeFolderLabel);

            await model.AddFilesCommand.ExecuteAsync(null);
            var added = Assert.Single(model.Documents);
            Assert.Equal("Filed/Receipts/Lease Renewal.pdf", added.RelativePath);
            await model.ImportDroppedFilesAsync([Path.Combine(fixtures, "Match Ledger.pdf")], hasSupportedFile: true);
            Assert.Equal(2, model.Documents.Count);
            Assert.All(model.Documents, document => Assert.StartsWith("Filed/Receipts/", document.RelativePath, StringComparison.Ordinal));
            Assert.Equal(2, model.InboxCount);

            string recoveredSource = Path.Combine(input.Path, "Recovered receipt.pdf");
            dialogs.SelectedFiles = [recoveredSource];
            await model.AddFilesCommand.ExecuteAsync(null);
            Assert.Equal(recoveredSource, Assert.Single(model.ImportFailures).SourcePath);
            File.Copy(Path.Combine(fixtures, "Scanned Receipt.pdf"), recoveredSource);
            dialogs.ImportErrorsChoice = ImportErrorsAction.Retry;
            var errors = Assert.IsType<Button>(window.FindControl<Button>("ImportErrorsButton"));
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(errors.Command).ExecuteAsync(null);
            Assert.Empty(model.ImportFailures);
            Assert.Equal("Filed/Receipts/Recovered receipt.pdf", model.SelectedDocument?.RelativePath);
            Assert.Equal(3, model.InboxCount);
            await WaitForPreviewAsync(model);

            dialogs.FolderPath = otherLibrary.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Equal("PDFs", model.IntakeFolderLabel);
            dialogs.FolderPath = temporary.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Equal("PDFs/Filed/Receipts", model.IntakeFolderLabel);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WatchingAScannerFolderUsesTheLibraryRootWithACustomManualDestination()
    {
        using var temporary = new TemporaryLibrary();
        using var incoming = new TemporaryLibrary();
        string target = Directory.CreateDirectory(Path.Combine(temporary.Path, "PDFs", "Manual")).FullName;
        var dialogs = new FixtureDialogs { FolderPath = target };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        await model.ChooseIntakeFolderCommand.ExecuteAsync(null);
        Assert.Equal("PDFs/Manual", model.IntakeFolderLabel);

        dialogs.FolderPath = incoming.Path;
        await model.StartFolderWatchCommand.ExecuteAsync(null);
        Assert.True(model.IsWatchingFolder);
        try
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
            File.Copy(fixture, Path.Combine(incoming.Path, "Watched lease.pdf"));
            await WaitForAsync(() => model.Documents.Count == 1 && !model.IsBusy);
            Assert.Equal("Watched lease.pdf", Assert.Single(model.Documents).RelativePath);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            await model.StopFolderWatchCommand.ExecuteAsync(null);
        }
    }
}
