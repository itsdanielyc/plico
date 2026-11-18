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
    public async Task ASelectedPdfOffersAnEditableDocumentDate()
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
            var edit = Assert.IsType<Button>(window.FindControl<Button>("EditDocumentDateButton"));
            Assert.True(edit.IsEffectivelyVisible);
            Assert.True(edit.IsEnabled);
            Assert.Equal("Set document date…", model.DocumentDateAction);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(edit.Command);
            var leapDay = new DateOnly(2024, 2, 29);
            dialogs.DocumentDateChoice = date =>
            {
                Assert.Null(date);
                return Task.FromResult<DocumentDateEdit?>(new(leapDay));
            };
            await command.ExecuteAsync(null);
            Assert.Equal(leapDay, model.SelectedDocument?.DocumentDate);
            Assert.Equal("Document date saved.", model.Status);
            model.Query = "dated:2024-02";
            await WaitForAsync(() => model.Heading == "Search results" && model.Documents.Count == 1);
            dialogs.DocumentDateChoice = date =>
            {
                Assert.Equal(leapDay, date);
                return Task.FromResult<DocumentDateEdit?>(new(null));
            };
            await command.ExecuteAsync(null);
            Assert.Empty(model.Documents);
            Assert.Equal("Document date cleared.", model.Status);
            model.ShowAllCommand.Execute(null);
            Assert.Null(Assert.Single(model.Documents).DocumentDate);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DocumentDateEditorKeepsInvalidDatesOpenAndAcceptsLeapDay()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new DocumentDateDialog(new DateOnly(2024, 2, 1));
        var result = dialog.ShowDialog<DocumentDateEdit?>(owner);
        try
        {
            var box = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("DocumentDateBox"));
            Assert.Equal("2024-02-01", box.Text);
            var save = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmDocumentDateButton"));
            box.Text = "2023-02-29";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(result.IsCompleted);
            Assert.True(dialog.FindControl<TextBlock>("DateValidation")?.IsVisible);
            box.Text = "2024-02-29";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new DateOnly(2024, 2, 29), Assert.IsType<DocumentDateEdit>(await result).Date);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await result;
            owner.Close();
        }
    }
}
