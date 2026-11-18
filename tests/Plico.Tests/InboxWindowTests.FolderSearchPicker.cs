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
    public async Task ChooseFolderReplacesTheScopeAndKeepsTheActiveSearch()
    {
        const string eligible = "Lease selected";
        const string outside = "Lease outside";
        const string tag = "Property";
        var date = new DateOnly(2024, 2, 29);
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        string target;
        string pdfDirectory;
        long eligibleId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            pdfDirectory = library.PdfDirectory;
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
            target = Directory.CreateDirectory(Path.Combine(pdfDirectory, "Filed", "Home insurance")).FullName;
            string nested = Directory.CreateDirectory(Path.Combine(target, "2024")).FullName;
            string other = Directory.CreateDirectory(Path.Combine(pdfDirectory, "Other")).FullName;
            File.Copy(fixture, Path.Combine(nested, $"{eligible}.pdf"));
            File.Copy(fixture, Path.Combine(target, "Lease untagged.pdf"));
            File.Copy(fixture, Path.Combine(other, $"{outside}.pdf"));
            Assert.Equal(3, library.Refresh(token).Indexed);
            var documents = library.Find(new DocumentFilter(), token).Items;
            eligibleId = Assert.Single(documents, document => document.Title == eligible).Id;
            foreach (var document in documents)
            {
                library.SetDocumentDate(document.Id, date);
                if (document.Title is eligible or outside) library.AddTag(document.Id, tag);
            }
        }

        var dialogs = new FixtureDialogs { FolderPath = target };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var sort = model.SortChoices.Single(choice => choice.Order == DocumentSort.DocumentDate);
        model.SortOption = sort;
        model.FilterByTag(tag);
        model.Query = "lease folder:Other dated:2024-02";
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            await WaitForAsync(() => model.Documents is [{ Title: outside }]);
            var folder = Assert.IsType<Button>(window.FindControl<Button>("ChooseSearchFolderButton"));
            Assert.True(folder.IsVisible && folder.IsEnabled);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(folder.Command);
            await command.ExecuteAsync(null);

            const string expectedQuery = "lease dated:2024-02 folder:\"Filed/Home insurance\"";
            Assert.Equal(expectedQuery, model.Query);
            Assert.Equal(expectedQuery, Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox")).Text);
            Assert.Equal(pdfDirectory, dialogs.LastFolderStart);
            Assert.Equal(eligibleId, Assert.Single(model.Documents).Id);
            Assert.Equal(sort, model.SortOption);
            Assert.Contains("in Property", model.ResultCount, StringComparison.Ordinal);

            dialogs.FolderPath = temporary.Path;
            await command.ExecuteAsync(null);
            Assert.Contains("Choose a folder inside", model.Status, StringComparison.Ordinal);
            Assert.Equal(expectedQuery, model.Query);
            Assert.Equal(eligibleId, Assert.Single(model.Documents).Id);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
