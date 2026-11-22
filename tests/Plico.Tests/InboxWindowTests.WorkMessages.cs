using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// Messages and the work in hand, with the keyboard and the pointer a person uses. Work that ends with nothing to say
/// raises no toast, so an old message or the work's own "…" never rises as news; an edit made while Plico works shows
/// its own toast and Undo at once, while the status card keeps saying what the work is doing.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task OpeningTheIndexReportFromTheCardRaisesNoToast()
    {
        const string letter = "Lease Renewal.pdf";
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string holdingPath = Path.Combine(temporary.Path, "held.pdf");
        using (var first = InboxLibrary.Open(temporary.Path))
        {
            string original = Path.Combine(first.PdfDirectory, letter);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", letter), original);
            first.Refresh(token);
            BatchFixtures.AddTwoDocuments(first, token);
            File.Move(original, holdingPath);
            first.Refresh(token);
        }
        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            Assert.False(toast.IsVisible);
            Assert.Equal("1 file is missing", model.StatusCardTitle);

            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("IndexReportButton")));
            await WaitForAsync(() => !model.IsBusy && dialogs.InspectedIndexReport is not null);

            // Reading the report has nothing to announce: "Library ready" was never news, and it is not now.
            Assert.False(toast.IsVisible, $"Opening the index report raised the toast “{model.ToastTitle}”.");
            Assert.Equal("1 file is missing", model.StatusCardTitle);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheImportErrorsRaisesNoEarlierToastAgain()
    {
        using var library = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        using (var opened = InboxLibrary.Open(library.Path)) BatchFixtures.AddTwoDocuments(opened, token);
        var store = new ImportFailureStore(Path.Combine(settings.Path, "Settings"));
        store.Save(library.Path, [new ImportFailure(Path.Combine(library.Path, "Damaged.pdf"), "Unreadable")]);
        var dialogs = new FixtureDialogs { ImportErrorsChoice = ImportErrorsAction.Close };
        using var model = new InboxViewModel(dialogs, importFailureStore: store);
        model.InitializeLibrary(() => library.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));

            // R marks the document reviewed and its toast rises; the person closes it.
            PressKey(window, Key.R, PhysicalKey.R, "r");
            await WaitForPreviewAsync(model);
            Assert.True(toast.IsVisible);
            Assert.Equal("Marked reviewed", model.ToastTitle);
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("CloseToastButton")));
            Assert.False(toast.IsVisible);

            // Looking at the import errors and closing them changes nothing, so no toast rises: least of all the review's
            // again, which would read as a second review.
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("ImportErrorsButton")));
            await WaitForAsync(() => !model.IsBusy && dialogs.InspectedImportFailures.Count == 1);
            Assert.False(toast.IsVisible, $"Closing the import errors raised the toast “{model.ToastTitle}”.");
            // The review's Undo is still the offer, for Ctrl+Z.
            Assert.Equal("Marked reviewed", model.UndoLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CancelledArchiveActionsRaiseNoToast()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var dialogs = new FixtureDialogs { ArchiveSerialChoice = null };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));

            // Ctrl+K, "set the archive", Enter, and the number dialog is closed without a number.
            PressKey(window, Key.K, PhysicalKey.K, "k", OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
            TypeText(window, "set the archive");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            await WaitForAsync(() => !model.IsBusy);
            Assert.False(toast.IsVisible, $"Cancelling the archive number raised the toast “{model.ToastTitle}”.");

            // Settings › Filing › Archive label sheets, closed the same way.
            var settings = OpenSettingsAt(window, "FilingGroup");
            var labels = settings.GetLogicalDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.PrintArchiveLabelsCommand));
            ClickAt(settings, labels);
            await WaitForAsync(() => !model.IsBusy);
            Assert.False(toast.IsVisible, $"Cancelling the label sheets raised the toast “{model.ToastTitle}”.");
            settings.Close();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnEditMadeWhilePlicoWorksShowsItsToastAndUndoAtOnce()
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
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            var undo = Assert.IsType<Button>(window.FindControl<Button>("UndoButton"));
            var tagField = Assert.IsType<AutoCompleteBox>(window.FindControl<AutoCompleteBox>("TagBox"));
            work = model.SaveSearchCommand.ExecuteAsync(null);
            Assert.True(model.IsBusy);
            string working = model.StatusCardTitle;

            // A tag typed and added while Plico works: its toast rises at once and carries Undo.
            ClickAt(window, tagField);
            TypeText(window, "Lease");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Contains("Lease", model.SelectedDocument!.Tags);
            Assert.True(toast.IsVisible, "The tag added while Plico works raised no toast.");
            Assert.Equal(("Tag added", "“Lease”"), (model.ToastTitle, model.ToastBody));
            Assert.True(undo.IsEffectivelyVisible && undo.IsEffectivelyEnabled, "The toast's Undo is missing or turned off while Plico works.");
            // The status card goes on saying what the work is doing.
            Assert.Equal(working, model.StatusCardTitle);
            Assert.True(model.IsBusy);

            // Its Undo works while Plico works, as the edit did.
            ClickAt(window, undo);
            Assert.DoesNotContain("Lease", model.SelectedDocument!.Tags);
            Assert.False(model.HasUndo);
            Assert.True(model.IsBusy);

            // A tag the document already has says so at once.
            ClickAt(window, tagField);
            TypeText(window, "Lease");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            TypeText(window, "lease");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.True(toast.IsVisible);
            Assert.Equal(("Already tagged", "“Lease”"), (model.ToastTitle, model.ToastBody));
            Assert.Equal(working, model.StatusCardTitle);

            // When the work ends its own message follows, without the edit's Undo, which Ctrl+Z still reaches.
            gate.TrySetResult(null);
            await work;
            Assert.True(toast.IsVisible);
            Assert.Equal("Saved search editor closed", model.ToastTitle);
            Assert.False(model.ToastOffersUndo);
            Assert.Equal("Tag added", model.UndoLabel);
        }
        finally
        {
            gate.TrySetResult(null);
            if (work is not null) await work;
            await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
