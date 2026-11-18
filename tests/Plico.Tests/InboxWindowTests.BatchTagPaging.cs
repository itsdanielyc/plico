using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchTagEditsKeepLoadedResultsAndContinuePaging(bool removeFilterTag)
    {
        const string filterTag = "Queue";
        const string addedTag = "Checked";
        const int loadedCount = 2 * LibraryCatalog.PageSize;
        const int additionalMatches = 5;
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
            for (int index = 1; index <= loadedCount + additionalMatches; index++)
            {
                File.Copy(fixture, Path.Combine(library.PdfDirectory, $"Lease {index:D3}.pdf"));
            }
            Assert.Equal(loadedCount + additionalMatches, library.Refresh(token).Indexed);
            if (removeFilterTag)
            {
                var ids = new List<long>();
                for (int offset = 0; offset < loadedCount + additionalMatches; offset += LibraryCatalog.PageSize)
                {
                    ids.AddRange(library.Find(new DocumentFilter(Offset: offset), token).Items.Select(document => document.Id));
                }
                library.AddTagToDocuments(ids, filterTag);
            }
        }

        var dialogs = new FixtureDialogs { TagRemovalChoice = _ => Task.FromResult<string?>(filterTag) };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        if (removeFilterTag) model.FilterByTag(filterTag);
        model.SortOption = model.SortChoices.Single(choice => choice.Order == DocumentSort.Title);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            model.LoadMoreCommand.Execute(null);
            Assert.Equal(loadedCount, model.Documents.Count);
            var before = model.Documents.Select(document => document.Id).ToArray();
            DocumentSummary[] selection = [model.Documents[^1], model.Documents[LibraryCatalog.PageSize], model.Documents[0]];
            var selectedIds = selection.Select(document => document.Id).ToHashSet();
            list.SelectedItems = selection.ToList();
            Assert.Equal(3, list.Selection.Count);

            string buttonName = removeFilterTag ? "RemoveBatchTagButton" : "ApplyBatchTagButton";
            string finished = removeFilterTag ? "Tag removed from 3 documents." : "Tag applied to 3 documents.";
            if (!removeFilterTag)
            {
                Assert.IsType<AutoCompleteBox>(window.FindControl<AutoCompleteBox>("BatchTagBox"))
                    .SetCurrentValue(AutoCompleteBox.TextProperty, addedTag);
            }
            var action = Assert.IsType<Button>(window.FindControl<Button>(buttonName));
            Assert.True(action.IsEnabled);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Status == finished && !model.IsBusy);

            Assert.Equal(loadedCount, model.Documents.Count);
            Assert.True(model.HasMore);
            Assert.Equal(DocumentSort.Title, model.SortOption?.Order);
            if (removeFilterTag)
            {
                Assert.Equal(filterTag, model.Heading);
                Assert.DoesNotContain(model.Documents, document => selectedIds.Contains(document.Id));
                Assert.False(model.HasBatchSelection);
            }
            else
            {
                Assert.Equal(before, model.Documents.Select(document => document.Id));
                Assert.Equal(selectedIds.Order(), list.Selection.SelectedItems.OfType<DocumentSummary>().Select(document => document.Id).Order());
                Assert.All(model.Documents.Where(document => selectedIds.Contains(document.Id)), document => Assert.Contains(addedTag, document.Tags));
            }

            model.LoadMoreCommand.Execute(null);
            int expectedTotal = loadedCount + additionalMatches - (removeFilterTag ? selection.Length : 0);
            Assert.Equal(expectedTotal, model.Documents.Count);
            Assert.Equal(expectedTotal, model.Documents.Select(document => document.Id).Distinct().Count());
            Assert.False(model.HasMore);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
