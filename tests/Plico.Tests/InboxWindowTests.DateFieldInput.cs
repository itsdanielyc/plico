using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// The date field with the pointer and keyboard a person uses. Its clear button, its suggested date and its calendar
/// glyph leave the keyboard in the field, so a date typed next lands there and never runs a single-key shortcut; the
/// open calendar is the field's own layer, closed by Escape, and a date typed while it is open saves like any other.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task AfterTheClearButtonTheNextDateIsTypedIntoTheField()
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
            var open = model.SelectedDocument!;
            var field = Named<TextBox>(window, "DocumentDateField");

            // The × clears the date and the keyboard stays in the field, as in any field with a clear button.
            ClickAt(window, Named<Button>(window, "ClearDocumentDateButton"));
            Assert.Null(model.SelectedDocument!.DocumentDate);
            Assert.Equal("Date cleared", model.UndoLabel);
            Assert.True(field.IsFocused, $"After clearing the date the keyboard is on {KeyboardOwner(window)}.");

            // So the new date typed next goes into the field: none of its letters reviews, accepts or renames anything.
            TypeKeys(window, "5 Apr 2026");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(open.Id, model.SelectedDocument!.Id);
            Assert.Equal(new DateOnly(2026, 4, 5), model.SelectedDocument!.DocumentDate);
            Assert.False(model.SelectedDocument!.Reviewed);
            Assert.Equal(open.Title, model.SelectedDocument!.Title);
            Assert.Equal(2, model.InboxCount);
            Assert.Equal("Date set", model.UndoLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheSuggestedDateFilesTheDateAndLeavesItReadyToBeTypedOver()
    {
        using var temporary = TemporaryLibraryWithASuggestedDate(out long dated);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            ClickAt(window, QueueRow(window, dated));
            await WaitForPreviewAsync(model);
            await WaitForAsync(() => model.HasSuggestedDate);
            var open = model.SelectedDocument!;
            var field = Named<TextBox>(window, "DocumentDateField");
            var suggestion = Named<Button>(window, "SuggestedDateButton");
            Assert.True(suggestion.IsEffectivelyVisible);

            // Selecting the dashed date files it. The headless renderer finds a dashed chip by its outline, so the click
            // lands just inside the chip's left edge.
            ClickAt(window, suggestion, new Point(2, suggestion.Bounds.Height / 2));
            Assert.Equal(new DateOnly(2026, 4, 3), model.SelectedDocument!.DocumentDate);
            Assert.Equal("Suggestion accepted", model.UndoLabel);
            Assert.True(field.IsFocused, $"After the suggested date was filed the keyboard is on {KeyboardOwner(window)}.");
            Assert.Equal("3 Apr 2026", field.SelectedText);

            // A date typed next replaces it, and none of its letters runs a shortcut.
            TypeKeys(window, "5 Apr 2026");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(open.Id, model.SelectedDocument!.Id);
            Assert.Equal(new DateOnly(2026, 4, 5), model.SelectedDocument!.DocumentDate);
            Assert.False(model.SelectedDocument!.Reviewed);
            Assert.Equal(2, model.InboxCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnEmptyDateFieldSaysItHoldsASuggestionAndEnterFilesIt()
    {
        using var temporary = TemporaryLibraryWithASuggestedDate(out long dated);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            ClickAt(window, QueueRow(window, dated));
            await WaitForPreviewAsync(model);
            await WaitForAsync(() => model.HasSuggestedDate);
            var field = Named<TextBox>(window, "DocumentDateField");
            var peer = ControlAutomationPeer.CreatePeerForElement(field);

            // D puts the keyboard in the empty field, and what a screen reader hears there names the suggestion and the key.
            PressKey(window, Key.D, PhysicalKey.D, "d");
            Assert.True(field.IsFocused);
            Assert.True(string.IsNullOrEmpty(field.Text));
            string key = OperatingSystem.IsMacOS() ? "Return" : "Enter";
            Assert.Equal($"Suggested from the page: 3 Apr 2026. Press {key} to file it, or type a date.", peer.GetHelpText());

            // Enter files the date that sits in the field, and the keyboard stays with it selected.
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(new DateOnly(2026, 4, 3), model.SelectedDocument!.DocumentDate);
            Assert.Equal("Suggestion accepted", model.UndoLabel);
            Assert.Equal("3 Apr 2026", field.SelectedText);
            Assert.Equal("Type a date like 21 Nov 2026, or choose one.", peer.GetHelpText());

            // Ctrl+Z takes it back; leaving the empty field afterwards never files the suggestion by itself.
            PressUndoChord(window);
            await WaitForAsync(() => !model.IsBusy);
            Assert.Null(model.SelectedDocument!.DocumentDate);
            await WaitForAsync(() => model.HasSuggestedDate);
            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Assert.False(field.IsFocused);
            Assert.Null(model.SelectedDocument!.DocumentDate);
            Assert.False(model.HasUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EnterFilesTheSuggestedDateWhilePlicoWorks()
    {
        using var temporary = TemporaryLibraryWithASuggestedDate(out long dated);
        // Reading Trash waits for its dialog, so Plico stays busy as it does while a watched folder's import runs.
        var pending = new TaskCompletionSource<TrashDecision?>();
        using var model = new InboxViewModel(new FixtureDialogs { TrashChoice = _ => pending.Task });
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        Task? work = null;
        try
        {
            await WaitForPreviewAsync(model);
            ClickAt(window, QueueRow(window, dated));
            await WaitForPreviewAsync(model);
            await WaitForAsync(() => model.HasSuggestedDate);
            var field = Named<TextBox>(window, "DocumentDateField");
            PressKey(window, Key.D, PhysicalKey.D, "d");
            Assert.True(field.IsFocused);

            // Plico starts working; Enter in the empty field still files the date the page suggests, as a typed date
            // would be, and offers its Undo at once.
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBusy);
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(new DateOnly(2026, 4, 3), model.SelectedDocument!.DocumentDate);
            Assert.Equal("Suggestion accepted", model.ToastTitle);
            Assert.True(model.ToastOffersUndo);
            Assert.Equal("3 Apr 2026", field.SelectedText);
        }
        finally
        {
            pending.TrySetResult(null);
            if (work is not null) await work;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheCalendarLeavesTheKeyboardInTheDateFieldAndEscapeClosesIt()
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
            var open = model.SelectedDocument!;
            var field = Named<TextBox>(window, "DocumentDateField");
            var glyph = Named<Button>(window, "DocumentDateCalendarButton");
            var picker = Named<Popup>(window, "DocumentDatePicker");

            // The glyph opens the calendar and the keyboard is in the field it belongs to.
            ClickAt(window, glyph);
            Assert.True(picker.IsOpen);
            Assert.True(field.IsFocused, $"With the calendar open the keyboard is on {KeyboardOwner(window)}.");

            // Escape closes the calendar, the topmost layer, and leaves the keyboard in the field.
            PressKey(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(picker.IsOpen, "Escape left the calendar open.");
            Assert.True(field.IsFocused);

            // A date typed while the calendar is open goes into the field: none of its letters runs a shortcut. Enter
            // saves it and closes the calendar.
            ClickAt(window, glyph);
            Assert.True(picker.IsOpen);
            TypeKeys(window, "3 Mar 2026");
            Assert.Equal(open.Id, model.SelectedDocument!.Id);
            Assert.False(model.SelectedDocument!.Reviewed);
            Assert.Equal(2, model.InboxCount);
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(new DateOnly(2026, 3, 3), model.SelectedDocument!.DocumentDate);
            Assert.False(picker.IsOpen);
            Assert.True(field.IsFocused);

            // Opened over a saved date, the calendar leaves that date selected in the field, so typing replaces it.
            ClickAt(window, glyph);
            Assert.True(picker.IsOpen);
            Assert.Equal("3 Mar 2026", field.SelectedText);
            TypeKeys(window, "4 Mar 2026");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(new DateOnly(2026, 3, 4), model.SelectedDocument!.DocumentDate);
            Assert.Equal(2, model.InboxCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ADateTypedWhileTheCalendarIsOpenIsSavedWhenTheKeyboardLeaves()
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
            var open = model.SelectedDocument!;
            var field = Named<TextBox>(window, "DocumentDateField");
            var picker = Named<Popup>(window, "DocumentDatePicker");

            // The calendar is opened, then D: the keyboard is in the field and the date is typed rather than chosen.
            ClickAt(window, Named<Button>(window, "DocumentDateCalendarButton"));
            PressKey(window, Key.D, PhysicalKey.D, "d");
            Assert.True(picker.IsOpen);
            Assert.True(field.IsFocused);
            TypeText(window, "3 Mar 2026");

            // Tab leaves the field: the date is saved and offers Undo, and the calendar closes with it.
            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.False(field.IsFocused);
            Assert.Equal(open.Id, model.SelectedDocument!.Id);
            Assert.Equal(new DateOnly(2026, 3, 3), model.SelectedDocument!.DocumentDate);
            Assert.Equal("Date set", model.UndoLabel);
            Assert.False(picker.IsOpen, "The calendar stayed open after the keyboard left its field.");
            Assert.Equal("3 Mar 2026", field.Text);

            // A click on another document does the same. The calendar is a light-dismiss popover, so the first click
            // only closes it and leaves the typed date in the field with the keyboard; the next saves it and opens the
            // clicked document.
            var other = model.Documents.Single(document => document.Id != open.Id);
            ClickAt(window, Named<Button>(window, "DocumentDateCalendarButton"));
            TypeKeys(window, "4 Mar 2026");
            ClickAt(window, QueueRow(window, other.Id));
            Assert.False(picker.IsOpen);
            Assert.True(field.IsFocused);
            Assert.Equal("4 Mar 2026", field.Text);
            ClickAt(window, QueueRow(window, other.Id));
            await WaitForPreviewAsync(model);
            Assert.Equal(other.Id, model.SelectedDocument!.Id);
            Assert.Equal(new DateOnly(2026, 3, 4), model.Documents.Single(document => document.Id == open.Id).DocumentDate);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Two Inbox documents, the April lease printing the date Plico then suggests for it (3 Apr 2026).</summary>
    private static TemporaryLibrary TemporaryLibraryWithASuggestedDate(out long dated)
    {
        var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var catalog = new LibraryCatalog(Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"));
        catalog.Upsert("April lease.pdf", "April lease", new PdfText("Lease renewal\nInvoice date: 2026-04-03", 1, true),
            new FileVersion(1, 100, true), new byte[32]);
        using (var library = InboxLibrary.Open(temporary.Path))
            dated = library.Find(new DocumentFilter("", null, false, 0, DocumentSort.Automatic)).Items.Single(document => document.Title == "April lease").Id;
        return temporary;
    }

    /// <summary>The window's control called <paramref name="name"/>.</summary>
    private static T Named<T>(Window window, string name) where T : Control => Assert.IsType<T>(window.FindControl<T>(name));
}
