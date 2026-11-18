using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    private const string BatchOcrTag = "Property receipts";

    [AvaloniaFact]
    public async Task BatchOcrContinuesAfterAChangedPdfAndReusesExistingCopies()
    {
        const string changedName = "A changed.pdf";
        const string existingName = "B receipt.pdf";
        const string newName = "C receipt.pdf";
        const OcrLanguage language = OcrLanguage.French;
        var recordedDate = new DateOnly(2024, 2, 29);
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var secondLibrary = new TemporaryLibrary();
        long[] sourceIds;
        long existingCopyId;
        string newSourcePath;
        byte[] sourceBytes;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var sources = AddOcrBatchScans(library, [changedName, existingName, newName], token);
            sourceIds = sources.Select(document => document.Id).ToArray();
            library.AddTag(sources[2].Id, BatchOcrTag);
            library.SetDocumentDate(sources[2].Id, recordedDate);
            existingCopyId = (await library.CreateSearchableCopyAsync(sources[1].Id, token, language)).Document.Id;
            newSourcePath = library.ResolvePdf(sources[2]);
            sourceBytes = File.ReadAllBytes(newSourcePath);
            File.WriteAllText(library.ResolvePdf(sources[0]), "This indexed PDF has changed.");
        }

        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        model.SortOption = model.SortChoices.Single(choice => choice.Order == DocumentSort.Title);
        model.RecognitionLanguage = language;
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var action = SelectOcrBatchSources(window, model, sourceIds);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(action.Command).ExecuteAsync(null);
            await WaitForPreviewAsync(model);

            var added = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            Assert.NotEqual(existingCopyId, added.Id);
            Assert.Equal("C receipt (searchable)", added.Title);
            Assert.Equal(recordedDate, added.DocumentDate);
            Assert.Contains(BatchOcrTag, added.Tags);
            Assert.True(added.HasText);
            Assert.Equal(5, model.InboxCount);
            Assert.Contains("1 failed", model.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1 reused", model.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(sourceBytes, File.ReadAllBytes(newSourcePath));
            var report = Assert.IsType<Button>(window.FindControl<Button>("OcrCopyBatchResultsButton"));
            Assert.True(report.IsVisible && report.IsEnabled);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(report.Command).ExecuteAsync(null);
            Assert.Collection(dialogs.InspectedOcrCopyResults,
                failure =>
                {
                    Assert.Equal(changedName, failure.SourcePath);
                    Assert.Equal(OcrCopyOutcome.Failed, failure.Outcome);
                    Assert.Contains("Refresh the index", failure.Detail, StringComparison.Ordinal);
                },
                existing =>
                {
                    Assert.Equal(existingName, existing.SourcePath);
                    Assert.Equal(OcrCopyOutcome.Reused, existing.Outcome);
                    Assert.Equal("B receipt (searchable).pdf", existing.Detail);
                },
                created =>
                {
                    Assert.Equal(newName, created.SourcePath);
                    Assert.Equal(OcrCopyOutcome.Added, created.Outcome);
                    Assert.Equal(added.RelativePath, created.Detail);
                });

            action = SelectOcrBatchSources(window, model, sourceIds);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(action.Command).ExecuteAsync(null);
            Assert.Equal(5, model.InboxCount);
            Assert.Contains("2 reused", model.Status, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(model.OcrCopyBatchResults, item => item.Outcome == OcrCopyOutcome.Added);

            dialogs.FolderPath = secondLibrary.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Empty(model.OcrCopyBatchResults);
            Assert.False(report.IsVisible);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CancellingBatchOcrKeepsCompletedCopiesAndReportsUnprocessedPdfs()
    {
        const string completedName = "A scan.pdf";
        const string pendingName = "B scan.pdf";
        using var temporary = new TemporaryLibrary();
        long[] sourceIds;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            sourceIds = AddOcrBatchScans(library, [completedName, pendingName], TestContext.Current.CancellationToken)
                .Select(document => document.Id).ToArray();
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        model.SortOption = model.SortChoices.Single(choice => choice.Order == DocumentSort.Title);
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(model.Status) && model.Status.StartsWith("English OCR 2 of 2.", StringComparison.Ordinal))
                model.CancelCommand.Execute(null);
        };
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var action = SelectOcrBatchSources(window, model, sourceIds);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(action.Command).ExecuteAsync(null);
            await WaitForPreviewAsync(model);

            Assert.Equal(3, model.InboxCount);
            Assert.Equal("1 OCR copy added. Cancelled with 1 document left.", model.Status);
            Assert.Equal("A scan (searchable)", model.SelectedDocument?.Title);
            Assert.Collection(model.OcrCopyBatchResults,
                added =>
                {
                    Assert.Equal(completedName, added.SourcePath);
                    Assert.Equal(OcrCopyOutcome.Added, added.Outcome);
                },
                pending =>
                {
                    Assert.Equal(pendingName, pending.SourcePath);
                    Assert.Equal(OcrCopyOutcome.Unprocessed, pending.Outcome);
                });
            Assert.DoesNotContain(model.Documents, document => document.Title.StartsWith("B scan (", StringComparison.Ordinal));
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    private static DocumentSummary[] AddOcrBatchScans(InboxLibrary library, string[] names, CancellationToken token)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Scanned Receipt.pdf");
        foreach (string name in names) File.Copy(fixture, Path.Combine(library.PdfDirectory, name));
        Assert.Equal(names.Length, library.Refresh(token).Indexed);
        return library.Find(new DocumentFilter(Sort: DocumentSort.Title), token).Items.ToArray();
    }

    private static Button SelectOcrBatchSources(MainWindow window, InboxViewModel model, long[] sourceIds)
    {
        var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
        list.SelectedItems = model.Documents.Where(document => sourceIds.Contains(document.Id)).ToList();
        Assert.True(model.HasBatchSelection);
        var action = Assert.IsType<Button>(window.FindControl<Button>("CreateSelectedOcrCopiesButton"));
        Assert.True(action.IsEffectivelyVisible && action.IsEnabled);
        return action;
    }
}
