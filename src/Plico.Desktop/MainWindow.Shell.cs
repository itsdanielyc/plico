using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Plico.Desktop;

public partial class MainWindow
{
    private const double CompactWidth = 1100;
    private const double MediumWidth = 1240;
    private static readonly TimeSpan ReviewMotionDuration = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan RepeatedReviewWindow = TimeSpan.FromMilliseconds(1200);
    private DateTime _lastReview = DateTime.MinValue;
    private IReadOnlyList<PaletteCommand> _paletteCommands = [];
    private SettingsWindow? _settings;
    private readonly HashSet<Key> _armedKeys = [];
    private Key? _releasedKey;

    /// <summary>Collapse order: the filing card narrows, then the queue becomes a thumbnail strip.</summary>
    private void ApplyWorkspaceLayout(double width)
    {
        // Window size changes arrive while the XAML is still loading, before the named grids exist.
        if (ShellGrid is null || WorkspaceGrid is null || QueueHeader is null || PageColumn is null || double.IsNaN(width)) return;
        bool compact = width < CompactWidth;
        bool medium = width < MediumWidth;
        ShellGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 200 : medium ? 208 : 216);
        WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 84 : medium ? 252 : 272);
        WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(compact ? 280 : medium ? 288 : 296);
        WorkspaceGrid.Classes.Set("compact", compact);
        // Compact: the header spans the thumbnail strip and the page, and the page starts below it.
        Grid.SetColumnSpan(QueueHeader, compact ? 2 : 1);
        Grid.SetRow(PageColumn, compact ? 1 : 0);
        Grid.SetRowSpan(PageColumn, compact ? 1 : 2);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Choose the column widths for this pass before the grids measure.
        ApplyWorkspaceLayout(double.IsFinite(availableSize.Width) ? availableSize.Width : Width);
        return base.MeasureOverride(availableSize);
    }

    // ───────── Keyboard ─────────

    private bool IsTyping => FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox or ComboBox { IsDropDownOpen: true }
        || FocusManager?.GetFocusedElement() is Visual visual && visual.FindAncestorOfType<AutoCompleteBox>() is not null;

    private bool IsOverlayOpen => PaletteOverlay.IsVisible || ShortcutsOverlay.IsVisible;

    /// <summary>
    /// Single-key shortcuts run on release, so the character never lands in a field the shortcut focuses. A release
    /// only counts when the same key went down outside a text field, with no overlay open and without Ctrl, ⌘ or
    /// Alt. A letter typed into search or the palette, or a chord let go modifier first, comes back up after focus
    /// has moved and must not review or trash a document. O(1) per key event.
    /// </summary>
    private void WireShortcutKeys()
    {
        AddHandler(KeyDownEvent, (_, e) =>
        {
            bool plain = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0;
            if (plain && !IsTyping && !IsOverlayOpen) _armedKeys.Add(e.Key);
            else _armedKeys.Remove(e.Key);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (_, e) => _releasedKey = _armedKeys.Remove(e.Key) ? e.Key : null,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>True when this release ends a press that began as a shortcut.</summary>
    private bool IsShortcutRelease(KeyEventArgs e) => _releasedKey == e.Key;

    /// <summary>Single-key review shortcuts. Keys follow the character on the user's layout.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Handled || !IsShortcutRelease(e) || IsTyping || IsOverlayOpen || ViewModel is not { } model) return;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) != 0) return;
        string key = (e.KeySymbol ?? "").ToLowerInvariant();
        bool handled = true;
        switch (key)
        {
            case "/": FocusSearch(); break;
            case "?": ShowShortcuts(); break;
            case "j": case "k":
                if (!model.HasSelection) { handled = false; break; }
                model.SelectReviewNeighbour(key == "j" ? 1 : -1);
                if (model.SelectedDocument is { } next) DocumentList.ScrollIntoView(next);
                break;
            case "r" when (e.KeyModifiers & KeyModifiers.Shift) == 0: Review(); break;
            case "a": Run(model.AcceptSuggestionsCommand); break;
            case "t" when model is { CanWork: true, ShowFilingCard: true }: TagBox.Focus(); break;
            case "d": Run(model.EditDocumentDateCommand); break;
            case "e": Run(model.EditDocumentTitleCommand); break;
            case "x": Run(model.TrashDocumentCommand); break;
            case "o": Run(model.OpenPdfCommand); break;
            case "[": Run(model.PreviousPageCommand); break;
            case "]": Run(model.NextPageCommand); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private bool HandleCommandKey(KeyEventArgs e, InboxViewModel model)
    {
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (e.Key)
        {
            case Key.K: ShowPalette(); return true;
            case Key.S when shift: Run(model.ScanCommand); return true;
            case Key.OemComma: OpenSettings(); return true;
            case Key.Z when !IsTyping && !shift: Run(model.UndoCommand); return true;
            case Key.A when !IsTyping && !shift && !IsOverlayOpen: SelectAllShown(); return true;
            default: return false;
        }
    }

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private static void Run(System.Windows.Input.ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter)) command.Execute(parameter);
    }

    // ───────── Review motion ─────────

    private void ReviewButton_Click(object? sender, RoutedEventArgs e) => PlayReviewMotion();

    private void Review()
    {
        if (ViewModel is not { CanWork: true, HasSelection: true } model) return;
        PlayReviewMotion();
        model.ToggleReviewCommand.Execute(null);
        if (model.SelectedDocument is { } selected) DocumentList.ScrollIntoView(selected);
    }

    /// <summary>
    /// Slips a copy of the page into the Inbox tray while the next document is already open. Runs on a
    /// snapshot so it never delays the change, and skips itself for repeated presses and reduced motion.
    /// </summary>
    private void PlayReviewMotion()
    {
        try
        {
            StartReviewMotion();
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // Motion is decoration. A platform that cannot snapshot the page still reviews the document.
            MotionLayer.Children.Clear();
        }
    }

    private void StartReviewMotion()
    {
        if (ViewModel is not { CanWork: true, SelectedDocument: { Reviewed: false } }) return;
        var now = DateTime.UtcNow;
        bool repeated = now - _lastReview < RepeatedReviewWindow;
        _lastReview = now;
        if (repeated || MotionPreference.IsReduced || !PreviewPaper.IsEffectivelyVisible) return;
        var size = PreviewPaper.Bounds.Size;
        if (size.Width < 8 || size.Height < 8) return;
        if (PreviewPaper.TranslatePoint(default, MotionLayer) is not { } origin ||
            InboxButton.TranslatePoint(new Point(18, InboxButton.Bounds.Height / 2), MotionLayer) is not { } tray) return;

        double scaling = RenderScaling;
        var snapshot = new RenderTargetBitmap(
            new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scaling)), Math.Max(1, (int)Math.Ceiling(size.Height * scaling))),
            new Vector(96 * scaling, 96 * scaling));
        snapshot.Render(PreviewPaper);
        var ghost = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Classes = { "paper" },
            Child = new Image { Source = snapshot, Stretch = Stretch.Fill },
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = TransformOperations.Parse("translate(0px, 0px) scale(1)")
        };
        Canvas.SetLeft(ghost, origin.X);
        Canvas.SetTop(ghost, origin.Y);
        MotionLayer.Children.Add(ghost);
        var accelerate = new SplineEasing(0.4, 0, 1, 1);
        ghost.Transitions =
        [
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = ReviewMotionDuration, Easing = accelerate },
            new DoubleTransition { Property = OpacityProperty, Duration = ReviewMotionDuration, Easing = new CubicEaseIn() }
        ];
        double dx = tray.X - (origin.X + size.Width / 2);
        double dy = tray.Y - (origin.Y + size.Height / 2);
        double scale = Math.Clamp(18 / size.Width, 0.02, 0.2);
        Dispatcher.UIThread.Post(() =>
        {
            ghost.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translate({dx:0.#}px, {dy:0.#}px) scale({scale:0.###})"));
            ghost.Opacity = 0.15;
        }, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(() =>
        {
            MotionLayer.Children.Remove(ghost);
            snapshot.Dispose();
        }, ReviewMotionDuration + TimeSpan.FromMilliseconds(80));
    }

    // ───────── Overlays: palette, shortcuts, settings ─────────

    private void OpenPalette_Click(object? sender, RoutedEventArgs e) => ShowPalette();
    private void OpenShortcuts_Click(object? sender, RoutedEventArgs e) => ShowShortcuts();
    private void OpenSettings_Click(object? sender, RoutedEventArgs e) => OpenSettings();
    private void CloseOverlays_Click(object? sender, RoutedEventArgs e) => CloseOverlays();

    private void Overlay_PointerPressed(object? sender, PointerPressedEventArgs e) => CloseOverlays();
    private void OverlayContent_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void CloseOverlays()
    {
        bool wasOpen = IsOverlayOpen;
        PaletteOverlay.IsVisible = false;
        ShortcutsOverlay.IsVisible = false;
        if (wasOpen) DocumentList.Focus();
    }

    private void ShowPalette()
    {
        if (ViewModel is not { } model) return;
        ShortcutsOverlay.IsVisible = false;
        _paletteCommands = PaletteCommands.Build(this, model);
        PaletteQuery.Text = "";
        FilterPalette();
        PaletteOverlay.IsVisible = true;
        Dispatcher.UIThread.Post(() => PaletteQuery.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Filters as the query text changes, including text set by code.</summary>
    private void WirePalette() => PaletteQuery.PropertyChanged += (_, change) =>
    {
        if (change.Property == TextBox.TextProperty && PaletteOverlay.IsVisible) FilterPalette();
    };

    private void FilterPalette()
    {
        var matches = PaletteCommands.Filter(_paletteCommands, PaletteQuery.Text ?? "");
        PaletteList.ItemsSource = matches;
        PaletteList.SelectedIndex = matches.Count > 0 ? 0 : -1;
        PaletteEmpty.IsVisible = matches.Count == 0;
    }

    private void PaletteQuery_KeyDown(object? sender, KeyEventArgs e)
    {
        int count = PaletteList.ItemCount;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                PaletteList.SelectedIndex = (PaletteList.SelectedIndex + 1) % count;
                PaletteList.ScrollIntoView(PaletteList.SelectedIndex);
                break;
            case Key.Up when count > 0:
                PaletteList.SelectedIndex = (PaletteList.SelectedIndex - 1 + count) % count;
                PaletteList.ScrollIntoView(PaletteList.SelectedIndex);
                break;
            case Key.Enter: RunSelectedPaletteCommand(); break;
            case Key.Escape: CloseOverlays(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void PaletteList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RunSelectedPaletteCommand();
        else if (e.Key == Key.Escape) CloseOverlays();
        else return;
        e.Handled = true;
    }

    private void PaletteList_DoubleTapped(object? sender, TappedEventArgs e) => RunSelectedPaletteCommand();

    private void RunSelectedPaletteCommand()
    {
        if (PaletteList.SelectedItem is not PaletteCommand command) return;
        CloseOverlays();
        Dispatcher.UIThread.Post(command.Run, DispatcherPriority.Input);
    }

    private void ShowShortcuts()
    {
        PaletteOverlay.IsVisible = false;
        if (ShortcutsGrid.Children.Count == 0) ShortcutSheet.Fill(ShortcutsGrid);
        ShortcutsOverlay.IsVisible = true;
    }

    internal void OpenSettings()
    {
        if (ViewModel is not { } model) return;
        CloseOverlays();
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow { DataContext = model };
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show(this);
    }

    internal void FocusTagEntry() => TagBox.Focus();
    internal void FocusSearchField() => FocusSearch();
    internal void ReviewWithMotion() => Review();
    internal void SelectAllShown()
    {
        if (DocumentList.ItemCount > 1) DocumentList.Selection.SelectRange(0, DocumentList.ItemCount - 1);
    }
    internal void ShowShortcutSheet() => ShowShortcuts();
}
