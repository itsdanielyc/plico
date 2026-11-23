using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchMatchButtonsStayReachableAlongsideZoomControls(bool useFrench)
    {
        using var temporary = new TemporaryLibrary();
        DesktopText.SetLanguage(useFrench ? InterfaceLanguage.French : InterfaceLanguage.English);
        using var model = new InboxViewModel(new FixtureDialogs(RedesignFixture("Match Ledger.pdf")));
        var window = new MainWindow { DataContext = model };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await model.AddFilesCommand.ExecuteAsync(null);
            model.Query = "ledger renewal";
            await WaitForAsync(() => model.HasPageMatchAction);
            await model.GoToMatchCommand.ExecuteAsync(null);
            Assert.True(model.PageMatchNavigationActive);
            await WaitForPreviewAsync(model);

            var page = window.FindControl<Border>("PageColumn")!;
            var previous = window.FindControl<Button>("PreviousMatchButton")!;
            var next = window.FindControl<Button>("GoToMatchButton")!;
            var fit = window.FindControl<Button>("PreviewFitButton")!;
            var open = window.FindControl<Button>("OpenPdfButton")!;
            var compositor = ElementComposition.GetElementVisual(previous)!.Compositor;
            foreach (double width in new[] { window.MinWidth, 1100, 1280 })
            {
                window.Width = width;
                await WaitForAsync(() => Math.Abs(window.Bounds.Width - width) < 1);
                window.UpdateLayout();
                // Pointer hit testing reads the last rendered frame, while Bounds already describes the new layout.
                var frame = compositor.RequestCompositionBatchCommitAsync();
                await Dispatcher.UIThread.AwaitWithPriority(frame.Rendered.WaitAsync(TestContext.Current.CancellationToken), DispatcherPriority.Input);
                var match = BoundsIn(next, page);
                var zoom = BoundsIn(fit, page);
                Assert.False(match.Intersects(zoom), $"At {width}px the next-match button {match} overlaps Fit {zoom}.");
                foreach (var button in new[] { previous, next, fit, open })
                {
                    var bounds = BoundsIn(button, page);
                    Assert.True(page.Bounds.Size.Width + 1 >= bounds.Right && bounds.X >= -1);
                    Assert.True(button.IsEffectivelyVisible);
                    var center = BoundsIn(button, window).Center;
                    var hit = window.InputHitTest(center) as Visual;
                    Assert.True(hit is not null && (ReferenceEquals(hit, button) || button.IsVisualAncestorOf(hit)),
                        $"At {width}px the center of {button.Name} ({center}) hits {hit?.GetType().Name ?? "nothing"}.");
                }
            }
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task ASearchThatFinishesBehindThePaletteCannotRetargetTrash()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var other = Assert.Single(model.Documents, document => document.Id != model.SelectedDocument!.Id);
            var search = window.FindControl<TextBox>("SearchBox")!;
            search.SetCurrentValue(TextBox.TextProperty, other.Title);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
            var palette = window.FindControl<Border>("PaletteOverlay")!;
            var query = window.FindControl<TextBox>("PaletteQuery")!;
            query.Text = "Move to Trash";
            Assert.True(palette.IsVisible);
            Assert.Equal("Move to Trash", Assert.IsType<PaletteCommand>(window.FindControl<ListBox>("PaletteList")!.SelectedItem).Title);
            await WaitForAsync(() => model.SelectedDocument?.Id == other.Id);
            if (palette.IsVisible)
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await WaitForAsync(() => !model.IsBusy);
            Assert.Equal(2, model.InboxCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PaletteResultsSpeakTheCommandTitleWhenNavigatedByKeyboard()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
            window.FindControl<TextBox>("PaletteQuery")!.Text = "trash";
            window.UpdateLayout();
            var list = window.FindControl<ListBox>("PaletteList")!;
            var command = Assert.IsType<PaletteCommand>(list.SelectedItem);
            var item = Assert.IsType<ListBoxItem>(list.ContainerFromItem(command));
            Assert.Equal(command.Title, ControlAutomationPeer.CreatePeerForElement(item).GetName());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FrenchQueueDatesAndTagActionsUseTheInterfaceLanguage()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var document = await library.ImportAsync(RedesignFixture("Lease Renewal.pdf"), TestContext.Current.CancellationToken);
            library.SetDocumentDate(document.Id, new DateOnly(2026, 1, 12));
            library.AddTag(document.Id, "Maison");
            library.SaveFilingRule(new(null, "Bail", "deposit", FilingRuleMatch.AnyWord));
        }
        DesktopText.SetLanguage(InterfaceLanguage.French);
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { DataContext = model };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await WaitForAsync(() => model.HasSuggestedTags);
            window.UpdateLayout();
            var list = window.FindControl<ListBox>("DocumentList")!;
            // The row's meta line writes the document's own date in the interface's language, then its pages.
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), block =>
                block.IsEffectivelyVisible && block.Text?.StartsWith("12 janv. 2026 · ", StringComparison.Ordinal) == true);
            var names = VisibleButtons(window).Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName()).ToArray();
            Assert.Contains("Retirer l’étiquette Maison", names);
            Assert.Contains("Ajouter l’étiquette suggérée Bail", names);
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    private static Rect BoundsIn(Control control, Visual ancestor) =>
        new(control.TranslatePoint(default, ancestor)!.Value, control.Bounds.Size);
}
