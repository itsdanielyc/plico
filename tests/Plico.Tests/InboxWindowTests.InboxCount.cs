using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task InboxCountIsIndependentOfTheVisibleSearch()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var count = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("InboxReviewCount"));
            Assert.True(count.IsEffectivelyVisible);
            Assert.Equal("2", count.Text);
            model.Query = "notfound098765";
            await WaitForAsync(() => model.Heading == "Search results" && model.Documents.Count == 0);
            Assert.Equal("2", count.Text);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WatchedArrivalUpdatesCountAndPreservesTheOpenView(bool loadSecondPage)
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var incoming = new TemporaryLibrary();
        const int initialCount = LibraryCatalog.PageSize + 1;
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string receipt = Path.Combine(fixtures, "Lease Renewal.pdf");
            for (int index = 0; index < initialCount; index++)
            {
                File.Copy(receipt, Path.Combine(library.PdfDirectory, $"Lease {index:D2}.pdf"));
            }
            Assert.Equal(initialCount, library.Refresh(token).Indexed);
        }

        var dialogs = new FixtureDialogs { FolderPath = incoming.Path };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            Assert.True(model.HasMore);
            if (loadSecondPage) model.LoadMoreCommand.Execute(null);
            var open = loadSecondPage ? model.Documents[0] : model.Documents[^1];
            model.SelectedDocument = open;
            await WaitForPreviewAsync(model);
            var preview = model.Preview;
            Assert.NotNull(preview);
            var shownIds = model.Documents.Select(document => document.Id).ToArray();
            Assert.Equal(loadSecondPage ? initialCount : LibraryCatalog.PageSize, shownIds.Length);

            await model.StartFolderWatchCommand.ExecuteAsync(null);
            Assert.True(model.IsWatchingFolder);
            File.Copy(Path.Combine(fixtures, "Hardware Store Receipt.png"), Path.Combine(incoming.Path, "New receipt.png"));
            await WaitForAsync(() => !model.IsBusy && model.Status == "1 document added.");

            Assert.Equal(initialCount + 1, model.InboxCount);
            // First-page deferral is decided after the background query reads the updated catalog.
            await WaitForAsync(() => model.HasDeferredWatchResults || model.Documents.Any(document => document.Title == "New receipt"));
            Assert.Equal(shownIds, model.Documents.Select(document => document.Id));
            Assert.Equal(open.Id, model.SelectedDocument?.Id);
            Assert.Same(preview, model.Preview);
            var refresh = Assert.IsType<Button>(window.FindControl<Button>("RefreshWatchedResultsButton"));
            Assert.True(refresh.IsEffectivelyVisible);
            Assert.True(refresh.IsEnabled);
            refresh.Command!.Execute(null);
            Assert.Equal("New receipt", model.Documents[0].Title);
            Assert.Equal(LibraryCatalog.PageSize, model.Documents.Count);
            Assert.True(model.HasMore);
            Assert.False(refresh.IsEffectivelyVisible);
            Assert.Equal(initialCount + 1, model.InboxCount);
        }
        finally
        {
            if (model.IsWatchingFolder) await model.StopFolderWatchCommand.ExecuteAsync(null);
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WatchedArrivalRestartsAnActiveQueryAgainstTheCommittedScan()
    {
        const string query = "WRENCH470";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var incoming = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, token);
        using var releaseRead = new ManualResetEventSlim();
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReadCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        var dialogs = new FixtureDialogs { FolderPath = incoming.Path };
        using var model = new InboxViewModel(dialogs, searchDocuments: ReadSearch);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            await model.StartFolderWatchCommand.ExecuteAsync(null);
            Assert.True(model.IsWatchingFolder);
            model.Query = query;
            await firstReadStarted.Task.WaitAsync(SearchTestDeadline, token);

            string receipt = Path.Combine(AppContext.BaseDirectory, "fixtures", "Hardware Store Receipt.png");
            File.Copy(receipt, Path.Combine(incoming.Path, "New receipt.png"));
            await WaitForAsync(() => !model.IsBusy && model.InboxCount == 3);
            releaseRead.Set();
            Assert.True(await firstReadCancelled.Task.WaitAsync(SearchTestDeadline, token));
            await WaitForAsync(() => model.Documents.Count == 1 && model.Documents[0].Title == "New receipt");
            Assert.Equal(query, model.Query);
            Assert.Equal(2, Volatile.Read(ref reads));
            Assert.False(model.HasDeferredWatchResults);
        }
        finally
        {
            releaseRead.Set();
            if (model.IsWatchingFolder) await model.StopFolderWatchCommand.ExecuteAsync(null);
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }

        DocumentPage ReadSearch(InboxLibrary library, DocumentFilter filter, CancellationToken cancellationToken)
        {
            var result = library.Find(filter, cancellationToken);
            if (filter.Query != query || Interlocked.Increment(ref reads) != 1) return result;

            firstReadStarted.SetResult();
            // The test releases this read after the watched import commits, which can include slow OCR.
            releaseRead.Wait(token);
            firstReadCancelled.SetResult(cancellationToken.IsCancellationRequested);
            return result;
        }
    }
}
