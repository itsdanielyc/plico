using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// The toast takes the keyboard like any control, and the keyboard never falls out of the window with it. When the
/// control holding it goes (Undo has done its work, Close is pressed, the palette closes over a toast that has gone), the
/// keyboard lands on the open document's row in the queue, where J, K and Tab carry on. Background work leaves a toast
/// that holds the pointer or the keyboard where it is.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task UndoFromTheKeyboardLeavesTheKeyboardOnTheRestoredDocument()
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
            ClickAt(window, QueueRow(window, reviewed.Id));
            PressKey(window, Key.R, PhysicalKey.R, "r");
            await WaitForPreviewAsync(model);
            Assert.NotEqual(reviewed.Id, model.SelectedDocument!.Id);

            // Tab reaches the toast's Undo and Enter presses it.
            TabTo(window, Named<Button>(window, "UndoButton"));
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            await WaitForAsync(() => !model.IsBusy);
            await WaitForPreviewAsync(model);
            Dispatcher.UIThread.RunJobs();

            // The review is undone and the keyboard is on the restored document's row, not nowhere.
            Assert.Equal(reviewed.Id, model.SelectedDocument!.Id);
            Assert.Equal(2, model.InboxCount);
            Assert.True(KeyboardIsOnRow(window, reviewed.Id), $"After Undo the keyboard is on {KeyboardOwner(window)}.");

            // So Tab carries on from the queue rather than from the top of the sidebar, and J moves on from there.
            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Assert.NotEqual("Button#LibrarySwitcherButton", KeyboardOwner(window));
            Assert.NotEqual("nothing", KeyboardOwner(window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BackgroundWorkLeavesAToastThatHoldsTheKeyboardOrThePointer()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        // Reading Trash waits for its dialog, so Plico stays busy as it does while a watched folder's import runs.
        var pending = new TaskCompletionSource<TrashDecision?>();
        var dialogs = new FixtureDialogs { TrashChoice = _ => pending.Task };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        Task? work = null;
        try
        {
            await WaitForPreviewAsync(model);
            ClickAt(window, Named<Button>(window, "ReviewButton"));
            await WaitForPreviewAsync(model);
            var open = model.SelectedDocument!;
            var toast = Named<Border>(window, "Toast");
            var close = Named<Button>(window, "CloseToastButton");

            // The keyboard is on Undo when the work begins. The toast stays; its Undo steps aside while Plico works, and
            // the keyboard goes to the open document's row instead of out of the window.
            TabTo(window, Named<Button>(window, "UndoButton"));
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBusy);
            Assert.True(toast.IsVisible, $"Work hid the toast that had the keyboard; the keyboard is on {KeyboardOwner(window)}.");
            Assert.True(KeyboardIsOnRow(window, open.Id), $"While Plico works the keyboard is on {KeyboardOwner(window)}.");
            pending.SetResult(null);
            await work;
            work = null;
            Dispatcher.UIThread.RunJobs();
            Assert.True(KeyboardIsOnRow(window, open.Id), $"After the work the keyboard is on {KeyboardOwner(window)}.");

            // On the toast's close button the keyboard stays through the work, and the toast with it.
            pending = new TaskCompletionSource<TrashDecision?>();
            TabTo(window, close);
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBusy);
            Assert.True(toast.IsVisible, $"Work hid the toast that had the keyboard; the keyboard is on {KeyboardOwner(window)}.");
            Assert.True(close.IsFocused);
            pending.SetResult(null);
            await work;
            work = null;
            Dispatcher.UIThread.RunJobs();
            Assert.True(toast.IsVisible);
            Assert.Equal("Trash is empty", model.ToastTitle);
            Assert.True(close.IsFocused);

            // The pointer resting on the toast keeps it too, with the keyboard back in the queue.
            ClickAt(window, QueueRow(window, open.Id));
            window.MouseMove(Assert.IsType<Point>(toast.TranslatePoint(new Point(toast.Bounds.Width / 2, toast.Bounds.Height / 2), window)));
            Dispatcher.UIThread.RunJobs();
            Assert.True(toast.IsPointerOver);
            pending = new TaskCompletionSource<TrashDecision?>();
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBusy);
            Assert.True(toast.IsVisible, "Work hid the toast under the pointer.");
            pending.SetResult(null);
            await work;
            work = null;
        }
        finally
        {
            pending.TrySetResult(null);
            if (work is not null) await work;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheKeyboardLandsOnTheOpenDocumentWhenTheToastOrThePaletteCloses()
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
            ClickAt(window, Named<Button>(window, "ReviewButton"));
            await WaitForPreviewAsync(model);
            var open = model.SelectedDocument!;

            // Closed from the keyboard, the toast hands the keyboard to the open document's row rather than to search.
            TabTo(window, Named<Button>(window, "CloseToastButton"));
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.False(Named<Border>(window, "Toast").IsVisible);
            Assert.True(KeyboardIsOnRow(window, open.Id), $"After closing the toast the keyboard is on {KeyboardOwner(window)}.");

            // The palette opened from the toast's Undo hides the toast; closing it puts the keyboard on the open row too.
            PressKey(window, Key.E, PhysicalKey.E, "e");
            TypeText(window, "Renamed in place");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            TabTo(window, Named<Button>(window, "UndoButton"));
            PressKey(window, Key.K, PhysicalKey.K, "k", OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
            Assert.True(Named<TextBox>(window, "PaletteQuery").IsFocused);
            PressKey(window, Key.Escape, PhysicalKey.Escape);
            Assert.True(KeyboardIsOnRow(window, open.Id), $"After closing the palette the keyboard is on {KeyboardOwner(window)}.");
        }
        finally
        {
            window.Close();
        }
    }
}
