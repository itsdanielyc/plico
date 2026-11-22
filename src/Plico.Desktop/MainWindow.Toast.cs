using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Plico.Desktop;

/// <summary>
/// The ink toast that replaced the status bar. It rises from the bottom of the page canvas when the status message
/// changes, carries at most one action, leaves after about six seconds and stays while the pointer or the keyboard
/// is on it. Progress and anything that needs attention go to the sidebar's status card instead.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ToastEntrance = TimeSpan.FromMilliseconds(260);
    private readonly DispatcherTimer _toastTimer = new() { Interval = ToastLifetime };
    private InboxViewModel? _toastModel;

    private void WireToast()
    {
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (Toast.IsPointerOver || Toast.IsKeyboardFocusWithin) return;
            Toast.IsVisible = false;
        };
        Toast.PointerExited += (_, _) => RestartToastTimer();
        Toast.LostFocus += (_, _) => RestartToastTimer();
        DataContextChanged += (_, _) => AttachToast();
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
    }

    private void ToastModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not InboxViewModel model) return;
        switch (e.PropertyName)
        {
            case nameof(InboxViewModel.Status) or nameof(InboxViewModel.ToastOffer):
                if (!model.IsBusy) ShowToast(model);
                break;
            case nameof(InboxViewModel.IsBusy):
                // While work runs its progress is on the status card; the result arrives as a toast when it ends.
                if (model.IsBusy) HideToast();
                else ShowToast(model);
                break;
        }
    }

    private void ShowToast(InboxViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Status)) return;
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

    private void CloseToast_Click(object? sender, RoutedEventArgs e)
    {
        HideToast();
        if (!DocumentList.Focus()) SearchBox.Focus();
    }
}
