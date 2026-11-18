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
    public async Task RotatePagesActionOpensARotatedCopyOfASinglePagePdf()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
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
            var rotate = Assert.IsType<Button>(window.FindControl<Button>("RotatePdfPagesButton"));
            Assert.True(rotate.IsEffectivelyVisible);
            Assert.True(rotate.IsEnabled);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(rotate.Command);
            var source = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            Assert.NotNull(model.Preview);
            await command.ExecuteAsync(rotate.CommandParameter);
            Assert.Equal("Page rotation closed.", model.Status);

            dialogs.PageCopyChoice = (document, page, action) =>
            {
                Assert.Equal(source.Id, document.Id);
                Assert.Equal(1, page);
                Assert.Equal(PdfPageCopyAction.Rotate, action);
                return Task.FromResult<PdfPageCopyRequest?>(new("Landscape lease", PdfPageSelection.Parse("1", document.PageCount),
                    PdfPageRotation.Clockwise));
            };
            await command.ExecuteAsync(rotate.CommandParameter);
            await WaitForPreviewAsync(model);
            Assert.Equal("Landscape lease", model.SelectedDocument?.Title);
            Assert.NotNull(model.Preview);
            Assert.True(model.InboxSelected);
            Assert.Contains(model.Documents, document => document.Id == source.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RotationDialogReturnsTheChosenDirection()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new PdfPageCopyDialog(documentPageCount: 5, currentPage: 3, action: PdfPageCopyAction.Rotate);
        var completion = dialog.ShowDialog<PdfPageCopyRequest?>(owner);
        try
        {
            var rotation = Assert.IsType<ComboBox>(dialog.FindControl<ComboBox>("RotationBox"));
            Assert.True(rotation.IsEffectivelyVisible);
            Assert.Equal(3, rotation.ItemCount);
            Assert.Equal("90° clockwise", rotation.SelectedItem?.ToString());
            rotation.SelectedIndex = 1;
            var pages = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("PagesBox"));
            Assert.Equal("3", pages.Text);
            var confirm = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmPageCopyButton"));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var request = Assert.IsType<PdfPageCopyRequest>(await completion.WaitAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PdfPageRotation.Counterclockwise, request.Rotation);
            Assert.Equal(new PdfPageRange(3, 3), Assert.Single(request.Selection.Ranges));
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await completion;
            owner.Close();
        }
    }
}
