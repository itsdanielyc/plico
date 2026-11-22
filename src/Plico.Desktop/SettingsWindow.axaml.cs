using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Plico.Desktop;

/// <summary>
/// Library, intake, recognition and filing preferences, bound to the same view model as the main window. What its own
/// actions have to say appears at the foot of its sheet, since the main window's toast is under it.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>A section is current once its heading is within this distance of where the first heading rests.</summary>
    private const double CurrentSectionSlack = 8;

    private InboxViewModel? _model;
    // True from a press in Settings until what it started has finished: messages raised meanwhile are Settings' own.
    private bool _actionInHand;
    private bool _busyBeforeAction;
    private bool _waitingForWork;
    private Window? _watchedOwner;

    public SettingsWindow()
    {
        InitializeComponent();
        SettingsScroll.ScrollChanged += (_, _) => MarkCurrentSection();
        SettingsScroll.SizeChanged += (_, _) => FitScrollTail();
        ModelsGroup.SizeChanged += (_, _) => FitScrollTail();
        Opened += (_, _) =>
        {
            MarkCurrentSection();
            WatchOwnerInput(Owner as Window);
        };
        AddHandler(PointerPressedEvent, (_, _) => BeginAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => BeginAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => SettleAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (_, _) => SettleAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        // A key that goes down here may come up somewhere else: Alt of Alt+Tab comes up in the window in front, and Enter
        // on Watch a folder… comes up in the picker. Leaving Settings, or input in the main window, settles it too.
        Deactivated += (_, _) => SettleAction();
        DataContextChanged += (_, _) => AttachModel(DataContext as InboxViewModel);
        Closed += (_, _) =>
        {
            AttachModel(null);
            WatchOwnerInput(null);
        };
    }

    /// <summary>Input given to the main window means the person has left Settings: what was in hand here is settled.</summary>
    private void WatchOwnerInput(Window? owner)
    {
        if (_watchedOwner is not null)
        {
            _watchedOwner.RemoveHandler(KeyDownEvent, OwnerInput);
            _watchedOwner.RemoveHandler(KeyUpEvent, OwnerInput);
            _watchedOwner.RemoveHandler(PointerPressedEvent, OwnerInput);
        }
        _watchedOwner = owner;
        if (owner is null) return;
        owner.AddHandler(KeyDownEvent, OwnerInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.AddHandler(KeyUpEvent, OwnerInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        owner.AddHandler(PointerPressedEvent, OwnerInput, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OwnerInput(object? sender, RoutedEventArgs e) => SettleActionNow();

    private void AttachModel(InboxViewModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= Model_PropertyChanged;
        _model = model;
        if (_model is not null) _model.PropertyChanged += Model_PropertyChanged;
    }

    /// <summary>A press or a key in Settings: what follows from it, until it has settled, is Settings' to say.</summary>
    private void BeginAction()
    {
        if (_actionInHand) return;
        _actionInHand = true;
        _busyBeforeAction = _model?.IsBusy == true;
    }

    /// <summary>
    /// Once the input in hand has been handled, an action that started work waits for it to end; anything else is done.
    /// Work that was already running (a watched import, say) is not this action's.
    /// </summary>
    private void SettleAction() => Dispatcher.UIThread.Post(SettleActionNow, DispatcherPriority.Background);

    private void SettleActionNow()
    {
        if (!_actionInHand) return;
        if (_model is { IsBusy: true } && !_busyBeforeAction) _waitingForWork = true;
        else if (!_waitingForWork) _actionInHand = false;
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not InboxViewModel model) return;
        switch (e.PropertyName)
        {
            case nameof(InboxViewModel.ToastStatus):
                // The person's own edits in the main window are its news, not Settings'.
                if (_actionInHand && !model.ToastIsQuiet && !model.ToastIsForeground) ShowMessage(model.ToastStatus, model.ToastIsProblem);
                break;
            case nameof(InboxViewModel.IsBusy) when !model.IsBusy && _waitingForWork:
                // The work's message follows straight after it stops being busy; the action is done after that.
                Dispatcher.UIThread.Post(() =>
                {
                    _waitingForWork = false;
                    _actionInHand = false;
                }, DispatcherPriority.Background);
                break;
        }
    }

    /// <summary>Shows a message at the foot of the sheet, with a check, or an alert for a failure, until the next one.</summary>
    private void ShowMessage(string message, bool problem)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        SettingsMessage.Text = message;
        SettingsMessageDone.IsVisible = !problem;
        SettingsMessageProblem.IsVisible = problem;
        SettingsMessageBar.IsVisible = true;
    }

    /// <summary>The section names a caller can open: GeneralGroup, RecognitionGroup, IntakeGroup and the rest.</summary>
    internal void ShowSection(string section)
    {
        if (this.FindControl<Control>(section) is not { } target) return;
        // Layout may not have run yet when the window has just been created.
        Dispatcher.UIThread.Post(() =>
        {
            if (target.TranslatePoint(default, SettingsPage) is not { } position) return;
            SettingsScroll.Offset = new Vector(0, Math.Max(0, position.Y));
            MarkCurrentSection(section);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Jump_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string target }) ShowSection(target);
    }

    private void ShowShortcuts_Click(object? sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main)
        {
            Close();
            main.ShowShortcutSheet();
        }
    }

    /// <summary>The white pill follows the page: the last section whose heading has reached the top is current.</summary>
    private void MarkCurrentSection(string? forced = null)
    {
        string? current = forced;
        if (current is null)
        {
            foreach (var section in SettingsPage.Children.OfType<StackPanel>())
            {
                if (section.TranslatePoint(default, SettingsScroll) is not { } top) continue;
                if (top.Y <= SettingsPage.Margin.Top + CurrentSectionSlack || current is null) current = section.Name;
            }
        }
        foreach (var row in SectionList.Children.OfType<Button>())
            row.Classes.Set("current", Equals(row.Tag, current));
    }

    /// <summary>Leaves enough room under the last section for its heading to reach the top of the page.</summary>
    private void FitScrollTail()
    {
        double room = SettingsScroll.Viewport.Height - ModelsGroup.Bounds.Height - SettingsPage.Margin.Top - SettingsPage.Spacing;
        ScrollTail.Height = Math.Max(56, room);
    }
}
