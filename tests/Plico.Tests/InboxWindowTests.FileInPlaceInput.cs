using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// The filing card's in-place fields with the keyboard and the pointer a person uses: keys typed into the focused field,
/// Ctrl+Z pressed where the keyboard is, and clicks made of a press and a release. Each edit is saved once, as typed,
/// whatever the next press is, and Undo reverses what its toast names.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task CtrlZInASettledFieldReversesWhatTheToastNames()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            foreach (var document in library.Find(new DocumentFilter("", null, false, 0, DocumentSort.Automatic)).Items)
                library.SetDocumentDate(document.Id, new DateOnly(2026, 4, 3));
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var dateField = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));

            // D, a date typed key by key, Enter: the date is saved and the toast offers its Undo.
            PressKey(window, Key.D, PhysicalKey.D, "d");
            Assert.True(dateField.IsFocused);
            TypeText(window, "28 Feb 2025");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(new DateOnly(2025, 2, 28), model.SelectedDocument!.DocumentDate);
            Assert.Equal(("Date set", "28 Feb 2025"), (model.ToastTitle, model.ToastBody));

            // With nothing new typed, Ctrl+Z is Plico's Undo, not the field's: the date goes back, whole.
            PressUndoChord(window);
            await WaitForAsync(() => !model.IsBusy);
            Assert.Equal(new DateOnly(2026, 4, 3), model.SelectedDocument!.DocumentDate);
            Assert.Equal("3 Apr 2026", dateField.Text);
            Assert.False(model.HasUndo);

            // Leaving the field afterwards saves nothing more.
            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Assert.False(dateField.IsFocused);
            Assert.Equal(new DateOnly(2026, 4, 3), model.SelectedDocument!.DocumentDate);
            Assert.False(model.HasUndo);

            // The tag field does the same once its tag is added.
            var tagField = Assert.IsType<AutoCompleteBox>(window.FindControl<AutoCompleteBox>("TagBox"));
            ClickAt(window, tagField);
            TypeText(window, "Tax");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Contains("Tax", model.SelectedDocument!.Tags);
            Assert.Equal("Tag added", model.UndoLabel);
            PressUndoChord(window);
            await WaitForAsync(() => !model.IsBusy);
            Assert.DoesNotContain("Tax", model.SelectedDocument!.Tags);
            Assert.True(string.IsNullOrEmpty(tagField.Text));
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClickingAnotherDocumentSavesTheEditAndOpensTheClickedDocument()
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
            var first = model.SelectedDocument!;
            var second = model.Documents.Single(document => document.Id != first.Id);

            // A title being typed is saved, and the click still opens the row it landed on.
            PressKey(window, Key.E, PhysicalKey.E, "e");
            TypeText(window, "Renamed by click");
            ClickAt(window, QueueRow(window, second.Id));
            await WaitForPreviewAsync(model);
            Assert.Equal(second.Id, model.SelectedDocument?.Id);
            Assert.Equal("Renamed by click", model.Documents.Single(document => document.Id == first.Id).Title);
            Assert.Equal("Renamed", model.UndoLabel);

            // So is a date being typed: it goes to the document it was typed for, and the clicked one opens.
            PressKey(window, Key.D, PhysicalKey.D, "d");
            TypeText(window, "3 Mar 2025");
            ClickAt(window, QueueRow(window, first.Id));
            await WaitForPreviewAsync(model);
            Assert.Equal(first.Id, model.SelectedDocument?.Id);
            Assert.Equal(new DateOnly(2025, 3, 3), model.Documents.Single(document => document.Id == second.Id).DocumentDate);
            Assert.Null(model.SelectedDocument!.DocumentDate);
            Assert.True(string.IsNullOrEmpty(Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField")).Text));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheToastsUndoReversesTheActionItNamesWhileATitleIsBeingTyped()
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
            var reviewed = model.SelectedDocument!;
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("ReviewButton")));
            await WaitForPreviewAsync(model);
            var next = model.SelectedDocument!;
            Assert.NotEqual(reviewed.Id, next.Id);
            Assert.Equal(("Marked reviewed", reviewed.Title), (model.ToastTitle, model.ToastBody));

            // Halfway through typing a new title for the next document, the toast's Undo is clicked.
            PressKey(window, Key.E, PhysicalKey.E, "e");
            TypeText(window, "Half typed");
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("UndoButton")));
            await WaitForAsync(() => !model.IsBusy);
            await WaitForPreviewAsync(model);

            // The review the toast named is reversed, and the half-typed title was never saved.
            model.ShowAllCommand.Execute(null);
            Assert.False(model.Documents.Single(document => document.Id == reviewed.Id).Reviewed);
            Assert.Equal(next.Title, model.Documents.Single(document => document.Id == next.Id).Title);
            Assert.Equal(2, model.InboxCount);
            Assert.False(model.HasUndo);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EditsMadeWhilePlicoWorksAreSaved()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        // A saved-search dialog left open holds Plico busy, as a watched folder's import does while the person types.
        var gate = new TaskCompletionSource<SavedSearchDecision?>();
        var dialogs = new FixtureDialogs { SavedSearchChoice = (_, _, _) => gate.Task };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        Task? work = null;
        try
        {
            model.Query = "lease";
            await WaitForAsync(() => model.Heading == "Search results" && model.Documents.Count == 2);
            await WaitForPreviewAsync(model);

            PressKey(window, Key.E, PhysicalKey.E, "e");
            TypeText(window, "Typed while busy");
            work = model.SaveSearchCommand.ExecuteAsync(null);
            Assert.True(model.IsBusy);
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal("Typed while busy", model.SelectedDocument!.Title);

            // The date field keeps the keyboard while Plico works, and Enter saves what was typed.
            var dateField = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            PressKey(window, Key.D, PhysicalKey.D, "d");
            Assert.True(dateField.IsFocused);
            TypeText(window, "4 Jul 2025");
            Assert.True(dateField.IsEffectivelyEnabled && dateField.IsFocused);
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(new DateOnly(2025, 7, 4), model.SelectedDocument!.DocumentDate);
            Assert.True(model.IsBusy);
        }
        finally
        {
            gate.TrySetResult(null);
            if (work is not null) await work;
            await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ASavedDateReadsAsAValueOnceTheKeyboardLeaves()
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
            var dateField = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            PressKey(window, Key.D, PhysicalKey.D, "d");
            TypeText(window, "29 Feb 2024");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            // The keyboard stays with the date selected, ready for another try.
            Assert.Equal("29 Feb 2024", dateField.SelectedText);

            // Once the keyboard leaves, the date is a value again: no selection is left drawn behind it.
            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Assert.False(dateField.IsFocused);
            Assert.Equal("29 Feb 2024", dateField.Text);
            Assert.Equal(dateField.SelectionStart, dateField.SelectionEnd);

            // A date entered without the keyboard in the field, as an automation client or the native journey does,
            // saves without leaving a selection behind it either.
            dateField.SetCurrentValue(TextBox.TextProperty, "1 Mar 2024");
            PressFieldKey(dateField, Key.Enter);
            Assert.Equal(new DateOnly(2024, 3, 1), model.SelectedDocument!.DocumentDate);
            Assert.False(dateField.IsFocused);
            Assert.Equal(dateField.SelectionStart, dateField.SelectionEnd);
            // And a field without the keyboard never paints a selection, whatever selected its text.
            dateField.SelectAll();
            Assert.Equal(Avalonia.Media.Colors.Transparent,
                Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(dateField.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single().SelectionBrush).Color);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The queue's row for a document.</summary>
    private static ListBoxItem QueueRow(Window window, long documentId)
    {
        var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
        window.UpdateLayout();
        return list.GetVisualDescendants().OfType<ListBoxItem>().First(item => item.DataContext is DocumentSummary document && document.Id == documentId);
    }
}
