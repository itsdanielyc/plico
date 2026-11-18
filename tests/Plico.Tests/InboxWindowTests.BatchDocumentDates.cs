using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task BatchDocumentDatesSetAndClearTheSelectedPdfs()
    {
        const string firstTitle = "April lease";
        const string secondTitle = "May lease";
        const string outsideTitle = "June lease";
        const string dateInput = "2024-02-29";
        const string query = "dated:2024-02-29";
        DateOnly firstDate = new(2024, 1, 5);
        DateOnly secondDate = new(2024, 1, 6);
        DateOnly outsideDate = new(2024, 1, 7);
        DateOnly newDate = new(2024, 2, 29);
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
            File.Copy(Path.Combine(library.PdfDirectory, $"{firstTitle}.pdf"), Path.Combine(library.PdfDirectory, $"{outsideTitle}.pdf"));
            library.Refresh(token);
            var ids = library.Find(new DocumentFilter(), token).Items.ToDictionary(document => document.Title, document => document.Id);
            library.SetDocumentDate(ids[firstTitle], firstDate);
            library.SetDocumentDate(ids[secondTitle], secondDate);
            library.SetDocumentDate(ids[outsideTitle], outsideDate);
        }

        var window = new MainWindow { Width = 900, Height = 590 };
        using var model = new InboxViewModel(new WorkspaceDialogs(window));
        model.InitializeLibrary(() => temporary.Path);
        window.DataContext = model;
        window.Show();
        try
        {
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            list.SelectedItems = model.Documents.Where(document => document.Title != outsideTitle).ToList();
            Assert.True(model.HasBatchSelection);
            var edit = Assert.IsType<Button>(window.FindControl<Button>("EditBatchDocumentDateButton"));
            Assert.True(edit.IsEffectivelyVisible);

            var dialog = await OpenEditorAsync();
            var box = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("DocumentDateBox"));
            var save = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmDocumentDateButton"));
            Assert.True(string.IsNullOrEmpty(box.Text));
            Assert.False(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.IsVisible);
            box.Text = dateInput;
            Assert.IsType<Button>(dialog.FindControl<Button>("CancelDocumentDateButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !model.IsBusy);
            Assert.Equal(firstDate, DateOf(firstTitle));
            Assert.Equal(secondDate, DateOf(secondTitle));

            dialog = await OpenEditorAsync();
            box = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("DocumentDateBox"));
            box.Text = dateInput;
            save = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmDocumentDateButton"));
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Status == "Document date saved for 2 documents." && model.HasBatchSelection);
            Assert.Equal(newDate, DateOf(firstTitle));
            Assert.Equal(newDate, DateOf(secondTitle));
            Assert.Equal(outsideDate, DateOf(outsideTitle));

            model.Query = query;
            await WaitForAsync(() => model.Heading == "Search results" && model.Documents.Count == 2);
            list.SelectedItems = model.Documents.ToList();
            dialog = await OpenEditorAsync();
            Assert.Equal(dateInput, Assert.IsType<TextBox>(dialog.FindControl<TextBox>("DocumentDateBox")).Text);
            var clear = Assert.IsType<Button>(dialog.FindControl<Button>("ClearDocumentDatesButton"));
            Assert.True(clear.IsEffectivelyVisible);
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Status == "Document dates cleared for 2 documents." && !model.IsBusy);
            Assert.Empty(model.Documents);
            Assert.Equal(query, model.Query);
            model.ShowAllCommand.Execute(null);
            Assert.Null(DateOf(firstTitle));
            Assert.Null(DateOf(secondTitle));
            Assert.Equal(outsideDate, DateOf(outsideTitle));

            async Task<Window> OpenEditorAsync()
            {
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => window.OwnedWindows.Count > 0);
                return Assert.Single(window.OwnedWindows);
            }
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            await WaitForAsync(() => !model.IsBusy);
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }

        DateOnly? DateOf(string title) => model.Documents.Single(document => document.Title == title).DocumentDate;
    }
}
