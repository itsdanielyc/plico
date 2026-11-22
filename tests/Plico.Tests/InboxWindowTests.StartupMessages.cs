using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// What Plico has to say while it starts, before the window is there to show it: a library that will not open keeps a
/// place on the status card and in the queue, with a way forward, and notices from opening the library or reading the
/// settings rise as a toast once the window opens. The window is made in App's order: the model first, then the window.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ALibraryThatWillNotOpenSaysWhyOnTheCardAndInTheQueue()
    {
        using var temporary = new TemporaryLibrary();
        using var other = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(other.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var dialogs = new FixtureDialogs { FolderPath = other.Path };
        using var model = new InboxViewModel(dialogs);
        var window = new MainWindow { Width = 1280, Height = 800 };
        // Another copy of Plico has this library open, so it cannot be locked.
        using (InboxLibrary.Open(temporary.Path))
        {
            model.InitializeLibrary(() => temporary.Path);
        }
        string reason = model.Status;
        Assert.False(model.HasLibrary);
        Assert.Contains("lock", reason, StringComparison.Ordinal);
        window.DataContext = model;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // The status card keeps the problem and its reason for as long as no library is open.
            var card = Assert.IsType<Panel>(window.FindControl<Panel>("StatusCard"));
            string[] cardTexts = VisibleTexts(card);
            Assert.True(cardTexts.Contains("Library didn't open") && cardTexts.Contains(reason),
                $"The status card says “{string.Join(" / ", cardTexts)}”; the reason the library did not open is not on it.");
            // The queue says so too, with the way forward.
            var empty = Assert.IsType<Panel>(window.FindControl<Panel>("QueueEmptyState"));
            Assert.Contains("Library didn't open", VisibleTexts(empty));

            // The message also rises as a toast once the window opens.
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            Assert.True(toast.IsVisible, "The reason the library did not open never rose as a toast.");
            Assert.Equal(reason, model.ToastStatus);

            // Selecting the card chooses another library, which opens.
            ClickAt(window, card);
            await WaitForAsync(() => !model.IsBusy && model.HasLibrary);
            await WaitForPreviewAsync(model);
            Assert.Equal(other.Path, model.LibraryPath);
            Assert.DoesNotContain("Library didn't open", VisibleTexts(card));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NoticesRaisedWhilePlicoStartsRiseOnceTheWindowOpens()
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = new ImportFailureStore(settingsDirectory);
        store.Save(temporary.Path, [new ImportFailure(Path.Combine(temporary.Path, "Damaged.pdf"), "Unreadable")]);
        // The saved import errors and the recognition settings are both unreadable.
        foreach (string file in Directory.GetFiles(settingsDirectory, "import-errors-*.json")) File.WriteAllText(file, "{ not json");
        File.WriteAllText(Path.Combine(settingsDirectory, "ocr-processing.json"), "{ not json");
        var ocrPreference = new OcrProcessingPreference(settingsDirectory);
        string ocrError = Assert.IsAssignableFrom<Exception>(Record.Exception(() => ocrPreference.Load())).Message;

        using var model = new InboxViewModel(new FixtureDialogs(), importFailureStore: store);
        model.InitializeLibrary(() => temporary.Path);
        model.InitializeOcrProcessing(ocrPreference);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            Assert.True(toast.IsVisible, $"Nothing rose to say what happened while Plico started: “{model.Status}”.");
            // Both notices are in the message, whichever came last.
            Assert.Contains("Couldn't load the saved import errors.", model.ToastStatus, StringComparison.Ordinal);
            Assert.Contains(ocrError, model.ToastStatus, StringComparison.Ordinal);
            Assert.True(model.HasLibrary);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AStartupNoticeOutlastsTheCatchUpThatOpeningStarts()
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var watched = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = new ImportFailureStore(settingsDirectory);
        store.Save(temporary.Path, [new ImportFailure(Path.Combine(temporary.Path, "Damaged.pdf"), "Unreadable")]);
        foreach (string file in Directory.GetFiles(settingsDirectory, "import-errors-*.json")) File.WriteAllText(file, "{ not json");
        // The library's saved watch catches up whenever it opens.
        var watchPreference = new WatchFolderPreference(settingsDirectory);
        string watchFolder = Path.Combine(watched.Path, "Scans");
        Directory.CreateDirectory(watchFolder);
        watchPreference.Save(Path.GetFullPath(temporary.Path), watchFolder);
        watchPreference.SaveCatchUpOnOpen(Path.GetFullPath(temporary.Path), true);

        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: watchPreference, importFailureStore: store);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForAsync(() => !model.IsBusy && model.IsWatchingFolder);
            await WaitForPreviewAsync(model);
            Dispatcher.UIThread.RunJobs();

            // The catch-up has said its piece, and what opening the library had to say is still there to read.
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            Assert.True(toast.IsVisible);
            Assert.True(model.ToastStatus.Contains("Couldn't load the saved import errors.", StringComparison.Ordinal),
                $"Once the catch-up ended the toast says only “{model.ToastStatus}”; the startup notice is gone.");
            Assert.Contains("Scans", model.ToastStatus, StringComparison.Ordinal);
            Assert.Equal("Couldn't load the saved import errors", model.ToastTitle);
            AssertToastWearsTheAlert(toast);
        }
        finally
        {
            await model.PrepareCloseAsync();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AProblemWhilePlicoStartsWearsTheAlertAndLeadsWithTheProblem(bool locked)
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = new ImportFailureStore(settingsDirectory);
        store.Save(temporary.Path, [new ImportFailure(Path.Combine(temporary.Path, "Damaged.pdf"), "Unreadable")]);
        foreach (string file in Directory.GetFiles(settingsDirectory, "import-errors-*.json")) File.WriteAllText(file, "{ not json");
        using var model = new InboxViewModel(new FixtureDialogs(), importFailureStore: store);
        var window = new MainWindow { Width = 1280, Height = 800 };
        if (locked)
        {
            // Another copy of Plico has this library open.
            using (InboxLibrary.Open(temporary.Path)) model.InitializeLibrary(() => temporary.Path);
        }
        else
        {
            // The library opens, but its saved import errors cannot be read.
            model.InitializeLibrary(() => temporary.Path);
        }
        window.DataContext = model;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            if (!locked) await WaitForPreviewAsync(model);
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            Assert.True(toast.IsVisible);
            Assert.NotEqual("Library ready", model.ToastTitle);
            Assert.Equal(locked ? "Plico could not lock this library" : "Couldn't load the saved import errors", model.ToastTitle);
            AssertToastWearsTheAlert(toast);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The toast shows its alert glyph and not the check: the message says something went wrong.</summary>
    private static void AssertToastWearsTheAlert(Border toast)
    {
        var glyphs = toast.GetVisualDescendants().OfType<Icon>().Take(2).ToArray();
        Assert.True(glyphs.Length == 2 && !glyphs[0].IsVisible && glyphs[1].IsVisible,
            "The toast reports a failure beside the success check.");
    }

    /// <summary>The words of every visible text inside <paramref name="root"/>, in layout order.</summary>
    private static string[] VisibleTexts(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text)).Select(text => text.Text!).ToArray();
}
