using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    private static readonly TimeSpan SearchTestDeadline = TimeSpan.FromSeconds(6);

    [AvaloniaFact]
    public async Task GoToMatchOpensTheMatchingOcrPageFromTheCurrentResults()
    {
        const string receiptQuery = "WRENCH470";
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Mixed Pages.pdf");
            var document = await library.ImportAsync(fixture, TestContext.Current.CancellationToken);
            Assert.Equal(2, document.PageCount);
        }

        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var goToMatch = Assert.IsType<Button>(window.FindControl<Button>("GoToMatchButton"));
            Assert.False(goToMatch.IsVisible);
            Assert.Equal("1 / 2", model.PageLabel);
            var searchBox = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            searchBox.SetCurrentValue(TextBox.TextProperty, receiptQuery);

            await WaitForAsync(() => goToMatch.IsVisible && goToMatch.IsEnabled);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(goToMatch.Command).ExecuteAsync(goToMatch.CommandParameter);
            await WaitForPreviewAsync(model);

            Assert.Equal("2 / 2", model.PageLabel);
            Assert.NotNull(model.Preview);
            Assert.Equal("Match on page 2.", model.Status);
            searchBox.SetCurrentValue(TextBox.TextProperty, "NORTHSTAR WRENCH470");

            Assert.False(goToMatch.IsVisible);
            await WaitForAsync(() => goToMatch.IsVisible && goToMatch.IsEnabled);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(goToMatch.Command).ExecuteAsync(goToMatch.CommandParameter);

            Assert.Equal("0 matching pages.", model.Status);
            Assert.Equal("2 / 2", model.PageLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NewQueryCancelsTheBackgroundSearchAndOnlyPublishesItsOwnResults()
    {
        const string firstTitle = "Plumbing invoice";
        const string secondTitle = "Insurance notice";
        const string firstQuery = "Plumbing";
        const string secondQuery = "Insurance";
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            var documents = library.Find(new DocumentFilter(), TestContext.Current.CancellationToken).Items;
            library.SetTitle(documents[0].Id, firstTitle);
            library.SetTitle(documents[1].Id, secondTitle);
        }

        var firstStartsOnDesktopThread = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWasCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        using var model = new InboxViewModel(new FixtureDialogs(), searchDocuments: ReadSearch);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var searchBox = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            var shownTitles = new List<string>();
            model.PropertyChanged += RecordSingleResult;
            searchBox.SetCurrentValue(TextBox.TextProperty, firstQuery);

            Assert.False(await firstStartsOnDesktopThread.Task.WaitAsync(SearchTestDeadline, TestContext.Current.CancellationToken));
            searchBox.SetCurrentValue(TextBox.TextProperty, secondQuery);
            releaseFirst.Set();

            Assert.True(await firstWasCancelled.Task.WaitAsync(SearchTestDeadline, TestContext.Current.CancellationToken));
            await WaitForAsync(() => model.SelectedDocument?.Title == secondTitle);
            await WaitForPreviewAsync(model);
            Assert.Equal(new[] { secondTitle }, shownTitles);

            void RecordSingleResult(object? _, PropertyChangedEventArgs change)
            {
                if (change.PropertyName == nameof(model.Documents) && model.Documents.Count == 1)
                {
                    shownTitles.Add(model.Documents[0].Title);
                }
            }
        }
        finally
        {
            releaseFirst.Set();
            window.Close();
        }

        DocumentPage ReadSearch(InboxLibrary library, DocumentFilter filter, CancellationToken token)
        {
            if (filter.Query == firstQuery)
            {
                bool onDesktopThread = Dispatcher.UIThread.CheckAccess();
                firstStartsOnDesktopThread.SetResult(onDesktopThread);
                if (!onDesktopThread) releaseFirst.Wait(SearchTestDeadline);
                firstWasCancelled.SetResult(token.IsCancellationRequested);
            }
            // Let the older search return its result to exercise the desktop's publication guard.
            return library.Find(filter, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task TypingDisablesPaginationUntilTheNewResultPageArrives()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
            for (int copy = 0; copy <= LibraryCatalog.PageSize; copy++)
            {
                File.Copy(fixture, Path.Combine(library.PdfDirectory, $"Lease {copy:D2}.pdf"));
            }
            Assert.Equal(LibraryCatalog.PageSize + 1, library.Refresh(TestContext.Current.CancellationToken).Indexed);
        }

        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            Assert.True(model.HasMore);
            var searchBox = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            searchBox.SetCurrentValue(TextBox.TextProperty, "NORTHSTAR");

            Assert.False(model.HasMore);
            model.LoadMoreCommand.Execute(null);
            Assert.Equal(LibraryCatalog.PageSize, model.Documents.Count);
            await WaitForAsync(() => model.HasMore && model.Heading == "Search results");
        }
        finally
        {
            window.Close();
        }
    }
}
