using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task InvalidSearchShowsARepairHintAndCorrectionRestoresResults()
    {
        const string invalidQuery = "northstar status:pending";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
        }

        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var searchBox = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            var message = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("EmptyResultsMessage"));
            searchBox.SetCurrentValue(TextBox.TextProperty, invalidQuery);
            await WaitForAsync(() => model.ResultCount != "Searching…");

            Assert.Equal("Check search", model.ResultCount);
            Assert.True(message.IsVisible);
            Assert.Equal("Use one review status. Try status:inbox or status:reviewed.", message.Text);
            Assert.Equal(invalidQuery, searchBox.Text);
            Assert.Empty(model.Documents);
            Assert.False(model.HasMore);

            searchBox.SetCurrentValue(TextBox.TextProperty, "northstar status:reviewed");
            Assert.Equal("", model.EmptyMessage);
            await WaitForAsync(() => model.ResultCount != "Searching…");
            Assert.Equal("0 documents", model.ResultCount);
            // The empty queue says what is true in its title and offers a way forward in its one sentence.
            Assert.Equal("No documents match", model.EmptyTitle);
            Assert.Equal("Try other words or a different filter.", message.Text);
            Assert.True(model.ClearSearchCommand.CanExecute(null));

            searchBox.SetCurrentValue(TextBox.TextProperty, "northstar status:inbox");
            await WaitForAsync(() => model.Documents.Count == 2);
            Assert.Equal("2 documents", model.ResultCount);
            Assert.False(message.IsVisible);
            Assert.All(model.Documents, document => Assert.False(document.Reviewed));
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
