using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Plico.Desktop;

/// <summary>
/// The ink toast that replaced the status bar. It rises from the bottom of the page canvas with each message that is
/// news (<see cref="InboxViewModel.ToastStatus"/>): the person's own edit at once, the work's message when the work
/// ends. It carries at most one action, leaves after about six seconds and stays while the pointer or the keyboard is
/// on it, work in the background included. Progress and anything that needs attention go to the sidebar's status card
/// instead. When the toast lets go of the keyboard (it is closed, or the button holding it goes), the keyboard
/// lands on the open document's row rather than out of the window.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ToastEntrance = TimeSpan.FromMilliseconds(260);
    private readonly DispatcherTimer _toastTimer = new() { Interval = ToastLifetime };
    private InboxViewModel? _toastModel;

    // What the model said before the window was there waits for the window to open; once shown, it stays while the work
    // that opening starts (catching up saved watches) runs, until the next message.
    private bool _startupToastOwed;
    private bool _toastCarriesStartupNews;

    private void WireToast()
    {
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (ToastIsHeld) return;
            Toast.IsVisible = false;
        };
        Toast.PointerExited += (_, _) => RestartToastTimer();
        Toast.AddHandler(GettingFocusEvent, Toast_GettingFocus);
        Toast.LostFocus += (_, e) =>
        {
            RestartToastTimer();
            // A toast button that hides or turns off while it has the keyboard (Undo once it has run, or while Plico
            // works) lets go of it; the keyboard then goes to the open document instead of out of the window.
            if (e.NewFocusedElement is null) OweKeyboardToOpenDocument();
        };
        DataContextChanged += (_, _) => AttachToast();
        Opened += (_, _) =>
        {
            if (_startupToastOwed) ShowStartupToast();
        };
        Closed += (_, _) =>
        {
            _toastTimer.Stop();
            if (_toastModel is not null) _toastModel.PropertyChanged -= ToastModel_PropertyChanged;
            _toastModel = null;
        };
    }

    private void AttachToast()
    {
        if (_toastModel is not null) _toastModel.PropertyChanged -= ToastModel_PropertyChanged;
        _toastModel = ViewModel;
        if (_toastModel is not null) _toastModel.PropertyChanged += ToastModel_PropertyChanged;
        HideToast();
        _toastCarriesStartupNews = false;
        if (IsVisible) ShowStartupToast();
        else _startupToastOwed = true;
    }

    /// <summary>
    /// Shows what the model said before the window could: a library that did not open, what Plico recovered as it
    /// opened the library, or settings it could not read. "Library ready." alone is quiet and raises nothing.
    /// </summary>
    private void ShowStartupToast()
    {
        _startupToastOwed = false;
        if (ViewModel is not { IsBusy: false } model || model.ToastIsQuiet) return;
        ShowToast(model);
        _toastCarriesStartupNews = Toast.IsVisible;
    }

    private void ToastModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not InboxViewModel model) return;
        switch (e.PropertyName)
        {
            case nameof(InboxViewModel.ToastStatus):
                // While work runs its progress is on the status card and its message arrives when it ends; an edit the
                // person makes meanwhile is news at once.
                _toastCarriesStartupNews = false;
                if (!model.IsBusy || model.ToastIsForeground) ShowToast(model);
                break;
            case nameof(InboxViewModel.ToastOffer):
                // An action arrives with its message; it never raises a toast that was closed or has gone.
                if (Toast.IsVisible) RestartToastTimer();
                break;
            case nameof(InboxViewModel.IsBusy):
                // A toast the pointer or the keyboard is on stays, as it does when its time is up, and so does the
                // person's own edit.
                if (!model.IsBusy)
                {
                    // The work may have reloaded the queue under a keyboard that had just landed on it.
                    if (_keyboardOwedToOpenDocument) OweKeyboardToOpenDocument();
                }
                else if (!ToastIsHeld && !model.ToastIsForeground && !_toastCarriesStartupNews)
                {
                    HideToast();
                }
                break;
        }
    }

    /// <summary>
    /// True while the pointer or the keyboard is on the toast, which then stays. That includes a keyboard that has just
    /// fallen out of it and not landed yet: work that begins turns Undo off before the toast hears of the work.
    /// </summary>
    private bool ToastIsHeld => Toast.IsPointerOver || Toast.IsKeyboardFocusWithin
        || _keyboardOwedToOpenDocument && FocusManager?.GetFocusedElement() is null;

    // Set when the keyboard fell out of the toast, until it has landed on the open document with no work in hand.
    private bool _keyboardOwedToOpenDocument;

    /// <summary>Settles the keyboard on the open document once the input in hand is done; see <see cref="SettleKeyboard"/>.</summary>
    private void OweKeyboardToOpenDocument()
    {
        _keyboardOwedToOpenDocument = true;
        Dispatcher.UIThread.Post(SettleKeyboard, DispatcherPriority.Background);
    }

    /// <summary>
    /// Puts a keyboard that is nowhere on the open document's row. It runs after the input and the layout in hand, so a
    /// queue reloaded by that input has its new row; work still running may reload the queue again, so the debt stays
    /// until the work ends. A keyboard that has gone somewhere else meanwhile is left there.
    /// </summary>
    private void SettleKeyboard()
    {
        if (!_keyboardOwedToOpenDocument) return;
        if (ViewModel is not { IsBusy: true }) _keyboardOwedToOpenDocument = false;
        if (IsVisible && FocusManager?.GetFocusedElement() is null) FocusOpenDocument();
    }

    private void ShowToast(InboxViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.ToastStatus)) return;
        if (model.ToastIsQuiet)
        {
            HideToast();
            return;
        }
        bool entering = !Toast.IsVisible;
        Toast.IsVisible = true;
        if (entering) PlayToastEntrance();
        RestartToastTimer();
    }

    /// <summary>An 8 px rise and a fade over 260 ms; reduced motion shows the toast in place.</summary>
    private void PlayToastEntrance()
    {
        Toast.Transitions = null;
        if (MotionPreference.IsReduced)
        {
            Toast.Opacity = 1;
            Toast.RenderTransform = null;
            return;
        }
        var ease = new SplineEasing(0.2, 0.8, 0.2, 1);
        Toast.Opacity = 0;
        Toast.RenderTransform = TransformOperations.Parse("translateY(8px)");
        Toast.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = ToastEntrance, Easing = ease },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = ToastEntrance, Easing = ease }
        ];
        Dispatcher.UIThread.Post(() =>
        {
            Toast.Opacity = 1;
            Toast.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }, DispatcherPriority.Background);
    }

    /// <summary>True while a toast that has just appeared is still fading in, so verification captures can wait for it.</summary>
    internal bool IsToastEntering => Toast.IsVisible && Toast.Opacity < 1;

    private void RestartToastTimer()
    {
        _toastTimer.Stop();
        if (Toast.IsVisible) _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.IsVisible = false;
    }

    /// <summary>
    /// Closes the toast. Closed from the keyboard, it hands the keyboard to the open document, where J, K and Tab carry
    /// on; a click leaves the keyboard where it was, so a title or date being typed carries on.
    /// </summary>
    private void CloseToast_Click(object? sender, RoutedEventArgs e)
    {
        if (Toast.IsKeyboardFocusWithin || FocusManager?.GetFocusedElement() is null) FocusOpenDocument();
        HideToast();
    }

    /// <summary>
    /// The toast's Close and its other actions (Open backup, Open export, OCR results) work under the pointer without
    /// taking the keyboard, so a title or date being typed is neither saved nor dropped by the click; Tab still reaches
    /// them. Undo takes the keyboard like any button, which drops what is being typed rather than saving it first.
    /// </summary>
    private void Toast_GettingFocus(object? sender, FocusChangingEventArgs e)
    {
        if (e.NavigationMethod == NavigationMethod.Pointer && !ReferenceEquals(e.NewFocusedElement, UndoButton)) e.TryCancel();
    }
}
