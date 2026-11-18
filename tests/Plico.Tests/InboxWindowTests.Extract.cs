using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ExtractPagesActionOpensTheExcerptInTheInbox()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf");
            await library.ImportAsync(fixture, TestContext.Current.CancellationToken);
        }
        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var extract = Assert.IsType<Button>(window.FindControl<Button>("ExtractPdfPagesButton"));
            Assert.True(extract.IsEffectivelyVisible);
            Assert.True(extract.IsEnabled);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(extract.Command);
            var source = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            model.NextPageCommand.Execute(null);
            await WaitForPreviewAsync(model);
            await command.ExecuteAsync(extract.CommandParameter);
            Assert.Equal("Page extraction closed.", model.Status);
            Assert.Equal("2 / 5", model.PageLabel);

            dialogs.PageCopyChoice = (document, page, action) =>
            {
                Assert.Equal(source.Id, document.Id);
                Assert.Equal(2, page);
                Assert.Equal(PdfPageCopyAction.Extract, action);
                return Task.FromResult<PdfPageCopyRequest?>(new("January entry", PdfPageSelection.Parse("2", document.PageCount)));
            };
            await command.ExecuteAsync(extract.CommandParameter);
            await WaitForPreviewAsync(model);
            var excerpt = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            Assert.Equal("January entry", excerpt.Title);
            Assert.Equal(1, excerpt.PageCount);
            Assert.False(extract.IsEffectivelyVisible);
            Assert.True(model.InboxSelected);
            Assert.Contains(model.Documents, document => document.Id == source.Id && document.PageCount == 5);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExtractDialogValidatesPagesAndTitleBeforeReturningASelection()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new PdfPageCopyDialog(documentPageCount: 5, currentPage: 3, action: PdfPageCopyAction.Extract);
        var completion = dialog.ShowDialog<PdfPageCopyRequest?>(owner);
        try
        {
            var pages = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("PagesBox"));
            var title = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("TitleBox"));
            var confirm = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmPageCopyButton"));
            var validation = Assert.IsType<TextBlock>(dialog.FindControl<TextBlock>("ValidationMessage"));
            Assert.Equal("3", pages.Text);
            Assert.False(Assert.IsType<ComboBox>(dialog.FindControl<ComboBox>("RotationBox")).IsEffectivelyVisible);
            pages.Text = "2-6";
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("1 to 5", validation.Text);
            Assert.True(dialog.IsVisible);

            pages.Text = "4,1-2,2";
            title.Text = " ";
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("title", validation.Text, StringComparison.OrdinalIgnoreCase);
            Assert.True(dialog.IsVisible);

            title.Text = "  Lease excerpt  ";
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<PdfPageCopyRequest>(await completion.WaitAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Lease excerpt", request.Title);
            Assert.Equal(new[] { new PdfPageRange(1, 2), new PdfPageRange(4, 4) }, request.Selection.Ranges);
            Assert.Null(request.Rotation);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await completion;
            owner.Close();
        }
    }
}
