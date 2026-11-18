using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ReviewingAPdfUpdatesTheOpenSavedInboxSearch()
    {
        const string name = "Leases to review";
        const string inboxQuery = "northstar status:inbox";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
            library.SaveSearch(name, inboxQuery, DocumentSort.Title);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var shortcuts = Assert.IsType<ItemsControl>(window.FindControl<ItemsControl>("SavedSearchList"));
            await WaitForAsync(() => shortcuts.GetVisualDescendants().OfType<Button>().Any());
            var saved = Assert.Single(shortcuts.GetVisualDescendants().OfType<Button>());
            saved.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Heading == name && model.Documents.Count == 2);
            var eventuallyReviewed = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            var review = Assert.IsType<Button>(window.FindControl<Button>("ReviewButton"));
            Assert.True(review.IsEnabled);
            review.Command!.Execute(null);

            Assert.Equal(name, model.Heading);
            Assert.Equal(inboxQuery, model.Query);
            Assert.Equal(1, model.InboxCount);
            Assert.NotEqual(eventuallyReviewed.Id, Assert.Single(model.Documents).Id);

            model.Query = "northstar status:reviewed";
            await WaitForAsync(() => model.Heading == "Search results" && model.Documents.Count == 1 &&
                model.Documents[0].Id == eventuallyReviewed.Id);
            Assert.True(model.Documents[0].Reviewed);
            review.Command.Execute(null);
            Assert.Empty(model.Documents);
            Assert.Equal(2, model.InboxCount);

            saved.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Heading == name && model.Documents.Count == 2);
            Assert.Contains(model.Documents, document => document.Id == eventuallyReviewed.Id && !document.Reviewed);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
