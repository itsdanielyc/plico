using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// The filing card is edited where it is: the title in its heading, the date in its field, a tag with one Enter. Each
/// edit saves as it happens and is undone from its toast.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task TheTitleIsEditedInPlaceAndSavesOnEnterOrLeavingAndRestoresOnEscape()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var start = Assert.IsType<Button>(window.FindControl<Button>("EditTitleButton"));
            var editor = Assert.IsType<TextBox>(window.FindControl<TextBox>("TitleEditBox"));
            long id = model.SelectedDocument!.Id;
            string original = model.SelectedDocument.Title;
            Assert.False(editor.IsVisible);
            Assert.True(start.IsEffectivelyVisible);
            Assert.Contains(start.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("key") && border.Child is TextBlock { Text: "E" });

            // E turns the heading into a field with the title selected; Escape puts it back unchanged.
            PressWindowKey(window, Key.E, "e");
            Assert.True(editor.IsEffectivelyVisible);
            Assert.True(editor.IsFocused);
            Assert.Equal(original, editor.Text);
            Assert.Equal(original, editor.SelectedText);
            editor.SetCurrentValue(TextBox.TextProperty, "Abandoned title");
            PressFieldKey(editor, Key.Escape);
            Assert.False(editor.IsVisible);
            Assert.True(start.IsEffectivelyVisible);
            Assert.Equal(original, model.SelectedDocument!.Title);

            // A click on the heading opens it too, and Enter saves with a toast that offers Undo.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(editor.IsFocused);
            editor.SetCurrentValue(TextBox.TextProperty, "Lease renewal letter");
            PressFieldKey(editor, Key.Enter);
            Assert.False(editor.IsVisible);
            Assert.Equal("Lease renewal letter", model.SelectedDocument!.Title);
            Assert.Equal(("Renamed", "“Lease renewal letter”"), (model.ToastTitle, model.ToastBody));
            Assert.Equal("Renamed", model.UndoLabel);

            // Leaving the field saves as well, and focus stays where it went.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.SetCurrentValue(TextBox.TextProperty, "Lease renewal");
            var review = Assert.IsType<Button>(window.FindControl<Button>("ReviewButton"));
            Assert.True(review.Focus());
            Assert.Equal("Lease renewal", model.SelectedDocument!.Title);
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.IsVisible);
            Assert.True(review.IsFocused);

            // An empty title is rolled back, and the message says why.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.SetCurrentValue(TextBox.TextProperty, "   ");
            PressFieldKey(editor, Key.Enter);
            Assert.False(editor.IsVisible);
            Assert.Equal("Lease renewal", model.SelectedDocument!.Title);
            Assert.Equal("Couldn't rename", model.ToastTitle);
            Assert.Contains("Enter a title", model.ToastBody, StringComparison.Ordinal);

            // Nothing changed, so Undo still reverses the latest rename.
            await model.UndoCommand.ExecuteAsync(null);
            Assert.Equal("Lease renewal letter", model.Documents.Single(document => document.Id == id).Title);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A key pressed in a focused field, as a person typing there would.</summary>
    private static void PressFieldKey(Control field, Key key) => field.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key
    });
}
