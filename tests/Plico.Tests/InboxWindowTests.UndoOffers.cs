using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// Undo belongs to its action: each undoable action makes one offer that names what it reverses, a later undoable
/// action replaces it, any other change to a document retires it, and Ctrl+Z only ever acts on the current offer.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task EachUndoOfferNamesItsActionAndCtrlZReversesOnlyTheLatest()
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
            var undo = Assert.IsType<Button>(window.FindControl<Button>("UndoButton"));
            var reviewed = model.SelectedDocument!;

            model.ToggleReviewCommand.Execute(null);
            await WaitForPreviewAsync(model);
            Assert.Equal(("Marked reviewed", reviewed.Title), (model.ToastTitle, model.ToastBody));
            Assert.Equal("Marked reviewed", model.UndoLabel);
            Assert.True(undo.IsVisible);

            var trashed = model.SelectedDocument!;
            Assert.NotEqual(reviewed.Id, trashed.Id);
            await model.TrashDocumentCommand.ExecuteAsync(null);
            Assert.Equal(("Moved to Trash", trashed.Title), (model.ToastTitle, model.ToastBody));
            Assert.Equal("Moved to Trash", model.UndoLabel);
            Assert.True(undo.IsVisible);

            // Ctrl+Z reverses the latest offer only: the PDF comes back from Trash and the review stays.
            await PressUndoKeyAsync(window, model);
            Assert.False(model.HasUndo);
            model.ShowAllCommand.Execute(null);
            Assert.Equal(2, model.Documents.Count);
            Assert.True(model.Documents.Single(document => document.Id == reviewed.Id).Reviewed);
            Assert.False(model.Documents.Single(document => document.Id == trashed.Id).Reviewed);
            Assert.Equal(1, model.InboxCount);

            // With no offer left, Ctrl+Z does nothing at all.
            await PressUndoKeyAsync(window, model);
            Assert.True(model.Documents.Single(document => document.Id == reviewed.Id).Reviewed);
            Assert.Equal(1, model.InboxCount);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnUndoAfterAnUnrelatedChangeDoesNotReverseTheEarlierOne()
    {
        using var temporary = new TemporaryLibrary();
        string filed;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            filed = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        }
        var dialogs = new FixtureDialogs { FolderPath = filed };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var reviewed = model.SelectedDocument!;

            // Looking around changes nothing, so the offer outlives its toast and Ctrl+Z still reverses the review.
            model.ToggleReviewCommand.Execute(null);
            model.ShowAllCommand.Execute(null);
            model.ShowInboxCommand.Execute(null);
            await WaitForPreviewAsync(model);
            await PressUndoKeyAsync(window, model);
            Assert.Equal(2, model.InboxCount);

            // A change Plico cannot undo, here moving the other PDF into a folder, retires the review's offer.
            model.ShowInboxCommand.Execute(null);
            await WaitForPreviewAsync(model);
            Assert.Equal(reviewed.Id, model.SelectedDocument?.Id);
            model.ToggleReviewCommand.Execute(null);
            await WaitForPreviewAsync(model);
            var moved = model.SelectedDocument!;
            Assert.NotEqual(reviewed.Id, moved.Id);
            await model.MovePdfCommand.ExecuteAsync(null);
            Assert.StartsWith("Filed", model.SelectedDocument!.RelativePath, StringComparison.Ordinal);
            Assert.False(model.HasUndo);
            Assert.False(Assert.IsType<Button>(window.FindControl<Button>("UndoButton")).IsVisible);

            await PressUndoKeyAsync(window, model);
            model.ShowAllCommand.Execute(null);
            Assert.True(model.Documents.Single(document => document.Id == reviewed.Id).Reviewed);
            Assert.StartsWith("Filed", model.Documents.Single(document => document.Id == moved.Id).RelativePath, StringComparison.Ordinal);
            Assert.Equal(1, model.InboxCount);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ATagTheDocumentAlreadyHasChangesNothingAndKeepsTheOffer()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            foreach (var document in library.Find(new DocumentFilter("", null, false, 0, DocumentSort.Automatic)).Items)
                library.AddTag(document.Id, "Tax");
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var reviewed = model.SelectedDocument!;
            model.ToggleReviewCommand.Execute(null);
            await WaitForPreviewAsync(model);

            // The next document already has the tag: nothing changes, the toast says so, and the review's offer stays.
            model.TagInput = "tax";
            model.AddTagCommand.Execute(null);
            Assert.Equal(("Already tagged", "“Tax”"), (model.ToastTitle, model.ToastBody));
            Assert.Equal("Marked reviewed", model.UndoLabel);
            await PressUndoKeyAsync(window, model);
            model.ShowAllCommand.Execute(null);
            Assert.False(model.Documents.Single(document => document.Id == reviewed.Id).Reviewed);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DismissingAReminderRetiresTheOffer()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            foreach (var document in library.Find(new DocumentFilter("", null, false, 0, DocumentSort.Automatic)).Items)
                library.SaveDeadlines(document.Id, [new(DeadlineKind.Due, DateOnly.FromDateTime(DateTime.Today), 0)], TestContext.Current.CancellationToken);
        }
        var dialogs = new FixtureDialogs();
        dialogs.RemindersChoice = reminders => Task.FromResult<ReminderChoice?>(new(ReminderAction.Dismiss, reminders[^1]));
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var reviewed = model.SelectedDocument!;
            model.ToggleReviewCommand.Execute(null);
            await WaitForPreviewAsync(model);
            Assert.True(model.HasUndo);

            // Dismissing a reminder in Upcoming changes a document, so Undo can no longer reach past it to the review.
            await model.RemindersCommand.ExecuteAsync(null);
            Assert.Equal("Reminder dismissed.", model.Status);
            Assert.False(model.HasUndo);
            await PressUndoKeyAsync(window, model);
            model.ShowAllCommand.Execute(null);
            Assert.True(model.Documents.Single(document => document.Id == reviewed.Id).Reviewed);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Ctrl+Z (⌘Z on macOS) on the window, then waits for whatever it started to finish.</summary>
    private static async Task PressUndoKeyAsync(MainWindow window, InboxViewModel model)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = modifier });
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Z, KeyModifiers = modifier });
        await WaitForAsync(() => !model.IsBusy);
    }
}
