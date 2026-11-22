using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
    // Set once the input has settled while the work the action started, or a stop it asked for, has yet to finish.
    private bool _waitingForWork;
    private Window? _watchedOwner;
    // How the control with the keyboard got it, so a keyboard moved for it shows the focus ring only if it did.
    private NavigationMethod _focusNavigation;

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
        FollowInput(this);
        // A menu that one of Settings' buttons opens (the phone inbox's) shows its items in a popup whose input does not
        // pass through Settings, so a press or a key on one of them is followed from the item itself.
        foreach (var item in this.GetLogicalDescendants().OfType<Button>().Select(button => button.Flyout).OfType<MenuFlyout>()
                     .SelectMany(menu => menu.Items.OfType<MenuItem>()))
            FollowInput(item);
        // A key that goes down here may come up somewhere else: Alt of Alt+Tab comes up in the window in front, and Enter
        // on Watch a folder… comes up in the picker. Leaving Settings, or input in the main window, settles it too.
        Deactivated += (_, _) => SettleAction();
        AddHandler(GotFocusEvent, (_, e) => _focusNavigation = e.NavigationMethod, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, Control_LostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += (_, _) => AttachModel(DataContext as InboxViewModel);
        Closed += (_, _) =>
        {
            AttachModel(null);
            WatchOwnerInput(null);
        };
    }

    /// <summary>A press or a key that reaches <paramref name="element"/> begins an action of Settings', and its release settles it.</summary>
    private void FollowInput(Interactive element)
    {
        element.AddHandler(PointerPressedEvent, (_, _) => BeginAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        element.AddHandler(KeyDownEvent, (_, _) => BeginAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        element.AddHandler(PointerReleasedEvent, (_, _) => SettleAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
        element.AddHandler(KeyUpEvent, (_, _) => SettleAction(), RoutingStrategies.Tunnel, handledEventsToo: true);
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

    private void OwnerInput(object? sender, RoutedEventArgs e)
    {
        // What an action taken in the main window says is the main window's, even while Settings waits for what it started.
        if (_model is not null) _model.SettingsHasActionInHand = false;
        SettleActionNow();
    }

    /// <summary>
    /// A control that turns off while it has the keyboard (a recognition switch once Plico starts working) would drop
    /// the keyboard out of Settings, so the next Tab started at the top. It goes to its section's row in the list
    /// instead, and back to the control when the control turns on again, unless the person has moved it meanwhile.
    /// </summary>
    private void Control_LostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NewFocusedElement is not null || e.Source is not Control { IsEffectivelyEnabled: false } control || !IsActive) return;
        var navigation = _focusNavigation;
        if (SectionRow(control) is not { } row || !row.Focus(navigation)) return;
        WaitToReturnKeyboard(control, row, navigation);
    }

    /// <summary>
    /// Gives the keyboard back to <paramref name="control"/> when it turns on again: from its section's row, or from
    /// nowhere unless another of Plico's windows is in front. A keyboard that is nowhere because the person went to
    /// another application (the desktop clears it then) comes back to the control, which has it when Settings is in
    /// front again. The keyboard is the whole application's, so once the person has gone back to the main window or a
    /// dialog, a keyboard that is nowhere there (the main window's queue reloaded as the work ended) is that window's to
    /// land; taking it would send what is typed there to Settings' switch.
    /// </summary>
    private void WaitToReturnKeyboard(Control control, Button row, NavigationMethod navigation)
    {
        void Return(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property != IsEffectivelyEnabledProperty || !control.IsEffectivelyEnabled) return;
            control.PropertyChanged -= Return;
            if (IsVisible && (row.IsFocused || !AnotherWindowIsInFront && FocusManager?.GetFocusedElement() is null)) control.Focus(navigation);
        }
        control.PropertyChanged += Return;
    }

    /// <summary>
    /// True while another of Plico's windows is the one in front: the main window, or a dialog over it or over Settings.
    /// False while Settings is in front, and while the person is in another application.
    /// </summary>
    private bool AnotherWindowIsInFront => !IsActive && IsOrOwnsActiveWindow(Owner as Window ?? this);

    private static bool IsOrOwnsActiveWindow(Window window) => window.IsActive || window.OwnedWindows.Any(IsOrOwnsActiveWindow);

    /// <summary>The row in the section list that jumps to the section holding <paramref name="control"/>.</summary>
    private Button? SectionRow(Control control)
    {
        var section = control.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault(panel => panel.Parent == SettingsPage);
        return section is null ? null : SectionList.Children.OfType<Button>().FirstOrDefault(row => Equals(row.Tag, section.Name));
    }

    private void AttachModel(InboxViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= Model_PropertyChanged;
            // Settings has nothing in hand once it has closed.
            _model.SettingsHasActionInHand = false;
        }
        _model = model;
        if (_model is not null) _model.PropertyChanged += Model_PropertyChanged;
    }

    /// <summary>
    /// A press or a key in Settings: what follows from it, until it has settled, is Settings' to say, and the model hears
    /// that what an aside says now is Settings' (<see cref="InboxViewModel.SettingsHasActionInHand"/>).
    /// </summary>
    private void BeginAction()
    {
        if (_model is not null) _model.SettingsHasActionInHand = true;
        if (_actionInHand) return;
        _actionInHand = true;
        _busyBeforeAction = _model?.IsBusy == true;
    }

    /// <summary>
    /// Once the input in hand has been handled, an action that started work waits for it to end, and one that stopped a
    /// watch waits until the stop has said so (<see cref="InboxViewModel.AsideIsUnderWay"/>); anything else is done. Work
    /// that was already running (a watched import, say) is not this action's.
    /// </summary>
    private void SettleAction() => Dispatcher.UIThread.Post(SettleActionNow, DispatcherPriority.Background);

    private void SettleActionNow()
    {
        if (!_actionInHand) return;
        if (_model is { } model && IsStillUnderWay(model)) _waitingForWork = true;
        else if (!_waitingForWork) EndAction();
    }

    /// <summary>Whether the work the action in hand started, or a stop it asked for, has yet to finish.</summary>
    private bool IsStillUnderWay(InboxViewModel model) => model.IsBusy && !_busyBeforeAction || model.AsideIsUnderWay;

    private void EndAction()
    {
        _waitingForWork = false;
        _actionInHand = false;
        if (_model is not null) _model.SettingsHasActionInHand = false;
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not InboxViewModel model) return;
        switch (e.PropertyName)
        {
            case nameof(InboxViewModel.ToastStatus):
                // The person's own edits, and what an action taken in the main window says, are its news, not Settings'.
                if (_actionInHand && !model.ToastIsQuiet && !model.ToastIsForeground) ShowMessage(model.ToastStatus, model.ToastIsProblem);
                break;
            case nameof(InboxViewModel.IsBusy) or nameof(InboxViewModel.AsideIsUnderWay) when _waitingForWork && !IsStillUnderWay(model):
                // The work's message follows straight after it stops being busy, and a stop has said so before it is no
                // longer under way; the action is done after that.
                Dispatcher.UIThread.Post(EndAction, DispatcherPriority.Background);
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
