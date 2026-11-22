using Avalonia.Controls;
using Avalonia.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    /// <summary>
    /// The welcome page, command palette, shortcuts sheet, Settings window, and the review loop with its motion and
    /// undo on a document imported into the Inbox.
    /// </summary>
    private static async Task VerifyRedesignSurfacesAsync(MainWindow window, InboxViewModel model, string evidence, string fixture)
    {
        window.Width = 1280;
        window.Height = 800;
        model.ShowInboxCommand.Execute(null);
        await UntilAsync(() => model.ShowInboxZero || (model.HasSelection && model.Preview is not null), () => model.Status);
        // The journey ends in an empty second library, which opens on the welcome page.
        Require(model.ShowWelcome, $"An empty library did not open on the welcome page. {model.Status}");
        await CaptureAsync(window, evidence, "welcome.png");

        // The welcome page fits the minimum window without scrolling, and its decorative mark gives way first.
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        var welcomeScroll = (ScrollViewer)Control<StackPanel>(window, "WelcomePage").Parent!;
        await UntilAsync(() => welcomeScroll.Viewport.Width is > 0 and < 700,
            () => $"The window did not reach its minimum size. {welcomeScroll.Viewport}");
        Require(welcomeScroll.Extent.Height <= welcomeScroll.Viewport.Height + 1 && welcomeScroll.Extent.Width <= welcomeScroll.Viewport.Width + 1,
            $"The welcome page did not fit {window.MinWidth} x {window.MinHeight}: {welcomeScroll.Extent} in {welcomeScroll.Viewport}.");
        await CaptureAsync(window, evidence, "welcome-compact.png");
        window.Width = 1280;
        window.Height = 800;

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
        var palette = Control<Border>(window, "PaletteOverlay");
        var query = Control<TextBox>(window, "PaletteQuery");
        Require(palette.IsVisible, "Ctrl+K did not open the command palette.");
        query.Text = "export";
        var list = Control<ListBox>(window, "PaletteList");
        await UntilAsync(() => list.SelectedItem is PaletteCommand { Title: var title } && title.StartsWith("Export", StringComparison.Ordinal),
            () => "The palette did not rank an export command first.");
        await CaptureAsync(window, evidence, "command-palette.png");
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Require(!palette.IsVisible, "Escape did not close the command palette.");

        window.ShowShortcutSheet();
        await UntilAsync(() => Control<Grid>(window, "ShortcutsGrid").Children.Count == 2, () => "The shortcuts sheet is empty.");
        await CaptureAsync(window, evidence, "keyboard-shortcuts.png");
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

        window.OpenSettings();
        var settings = await WaitForOwnedAsync<SettingsWindow>(window);
        Require(Equals(Control<ComboBox>(settings, "OcrLanguageBox").SelectedItem, model.RecognitionLanguage),
            "Settings did not show the recognition language.");
        await CaptureAsync(settings, evidence, "settings.png");
        settings.Close();

        if (model.SelectedDocument is not { Reviewed: false })
        {
            await model.ImportAssociatedPdfsAsync([Path.Combine(Path.GetDirectoryName(fixture)!, "Lease Renewal.pdf")]);
            model.ShowInboxCommand.Execute(null);
        }
        await UntilAsync(() => model.SelectedDocument is { Reviewed: false } && model.Preview is not null && !model.IsReadingInBackground,
            () => $"No Inbox document to review. {model.Status}");
        var document = model.SelectedDocument!;
        long before = model.InboxCount;
        var review = Control<Button>(window, "ReviewButton");
        Require(review.Focus(), "The filing card could not receive keyboard focus.");
        review.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.R, KeySymbol = "r" });
        review.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.R, KeySymbol = "r" });
        Require(model.InboxCount == before - 1 && model.HasUndo, $"R did not mark the document reviewed with an undo. {model.Status}");
        await CaptureAsync(window, evidence, "review-undo.png");
        await model.UndoCommand.ExecuteAsync(null);
        await UntilAsync(() => model.InboxCount == before && model.SelectedDocument is { Reviewed: false } restored && restored.Id == document.Id,
            () => model.Status);
    }

    private static async Task<T> WaitForOwnedAsync<T>(Window owner) where T : Window
    {
        await UntilAsync(() => owner.OwnedWindows.OfType<T>().Any(window => window.IsVisible), () => $"{typeof(T).Name} did not open.");
        return owner.OwnedWindows.OfType<T>().First();
    }
}
