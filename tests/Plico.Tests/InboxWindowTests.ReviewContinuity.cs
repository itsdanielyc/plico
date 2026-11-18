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
    public async Task ReviewingAPdfRefillsTheCurrentPageAndKeepsTheReviewPosition()
    {
        const int pageSize = InboxLibrary.DefaultResultPageSize;
        const int documentCount = pageSize + 3;
        using var temporary = new TemporaryLibrary();
        SeedReviewQueue(temporary.Path, documentCount, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        model.SortOption = model.SortChoices.Single(choice => choice.Order == DocumentSort.Title);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            var review = Assert.IsType<Button>(window.FindControl<Button>("ReviewButton"));
            model.LoadMoreCommand.Execute(null);
            var original = model.Documents.ToArray();
            Assert.Equal(documentCount, original.Length);
            model.ShowInboxCommand.Execute(null);

            list.SetCurrentValue(ListBox.SelectedItemProperty, model.Documents[^1]);
            review.Command!.Execute(null);
            Assert.Equal(original[pageSize].Id, model.SelectedDocument?.Id);
            Assert.Equal(pageSize, model.Documents.Count);
            Assert.True(model.HasMore);

            model.LoadMoreCommand.Execute(null);
            var secondLast = model.Documents[^2];
            var last = model.Documents[^1];
            list.SetCurrentValue(ListBox.SelectedItemProperty, secondLast);
            review.Command.Execute(null);
            Assert.Equal(last.Id, model.SelectedDocument?.Id);
            Assert.Equal(documentCount - 2, model.Documents.Count);

            var preceding = model.Documents[^2];
            review.Command.Execute(null);
            Assert.Equal(preceding.Id, model.SelectedDocument?.Id);
            Assert.Equal(documentCount - 3, model.InboxCount);

            model.ShowAllCommand.Execute(null);
            model.LoadMoreCommand.Execute(null);
            var reviewed = model.Documents.Single(document => document.Id == original[pageSize - 1].Id);
            Assert.True(reviewed.Reviewed);
            list.SetCurrentValue(ListBox.SelectedItemProperty, reviewed);
            review.Command.Execute(null);
            Assert.Equal(documentCount, model.Documents.Count);
            Assert.Equal(reviewed.Id, model.SelectedDocument?.Id);
            Assert.False(Assert.IsType<DocumentSummary>(model.SelectedDocument).Reviewed);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ReviewingASelectionKeepsLoadedMatchesAndSkipsTheReviewedRows()
    {
        const int pageSize = InboxLibrary.DefaultResultPageSize;
        const int documentCount = pageSize + 4;
        const string query = "northstar status:inbox";
        using var temporary = new TemporaryLibrary();
        SeedReviewQueue(temporary.Path, documentCount, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        model.SortOption = model.SortChoices.Single(choice => choice.Order == DocumentSort.Title);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            model.Query = query;
            await WaitForAsync(() => model.Heading == "Search results" && model.HasMore);
            model.LoadMoreCommand.Execute(null);
            Assert.Equal(documentCount, model.Documents.Count);
            var first = model.Documents[pageSize];
            var second = model.Documents[pageSize + 1];
            var next = model.Documents[pageSize + 2];
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            list.SetCurrentValue(ListBox.SelectedItemProperty, first);
            list.SelectedItems = new List<DocumentSummary> { first, second };
            Assert.Equal(first.Id, model.SelectedDocument?.Id);
            Assert.Equal(2, list.Selection.Count);

            var review = Assert.IsType<Button>(window.FindControl<Button>("BatchReviewButton"));
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(review.Command).ExecuteAsync(null);
            Assert.Equal(documentCount - 2, model.Documents.Count);
            Assert.Equal(next.Id, model.SelectedDocument?.Id);
            Assert.Equal(query, model.Query);
            Assert.Equal(DocumentSort.Title, model.SortOption?.Order);
            Assert.DoesNotContain(model.Documents, document => document.Id == first.Id || document.Id == second.Id);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    private static void SeedReviewQueue(string libraryRoot, int documentCount, CancellationToken cancellationToken)
    {
        using var library = InboxLibrary.Open(libraryRoot);
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        for (int index = 1; index <= documentCount; index++)
        {
            File.Copy(fixture, Path.Combine(library.PdfDirectory, $"Lease {index:D3}.pdf"));
        }
        Assert.Equal(documentCount, library.Refresh(cancellationToken).Indexed);
    }
}
