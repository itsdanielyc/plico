using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var field = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            Assert.True(field.IsEffectivelyVisible);
            Assert.True(field.IsEnabled);
            Assert.True(string.IsNullOrEmpty(field.Text));
            Assert.Equal("Add a date", field.PlaceholderText);
            field.SetCurrentValue(TextBox.TextProperty, "29 Feb 2024");
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            var leapDay = new DateOnly(2024, 2, 29);
            Assert.Equal(leapDay, model.SelectedDocument?.DocumentDate);
            Assert.Equal("Date set.", model.Status);
            model.Query = "dated:2024-02";
            await WaitForAsync(() => model.Heading == "Search results" && model.Documents.Count == 1);
            Assert.Equal("29 Feb 2024", field.Text);
            field.SetCurrentValue(TextBox.TextProperty, "");
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Empty(model.Documents);
            Assert.Equal("Date cleared.", model.Status);
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
            Assert.Equal("1 Feb 2024", box.Text);
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
