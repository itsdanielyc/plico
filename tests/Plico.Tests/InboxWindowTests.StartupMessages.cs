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

            // The status card keeps the problem for as long as no library is open, and says what selecting it does, as it
            // does for every problem. Where new files go means nothing without a library, so the card leaves it out.
            var card = Assert.IsType<Panel>(window.FindControl<Panel>("StatusCard"));
            string[] cardTexts = VisibleTexts(card);
            Assert.True(cardTexts.SequenceEqual(["Library didn't open", "Select to choose a library folder."]),
                $"The status card says “{string.Join(" / ", cardTexts)}”.");
            // The queue says why, under the folder glyph, with the way forward.
            var empty = Assert.IsType<Panel>(window.FindControl<Panel>("QueueEmptyState"));
            Assert.Equal(new[] { "Library didn't open", reason }, VisibleTexts(empty).Take(2));
            AssertEmptyStateGlyph(empty, "Icon.Folder");

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
            // Both notices are in the message, whichever came last, in the person's words: a damaged file is named as
            // one, never with the parser's account of it.
            Assert.Contains("Couldn't load the saved import errors.", model.ToastStatus, StringComparison.Ordinal);
            Assert.Contains("Couldn't load the recognition settings.", model.ToastStatus, StringComparison.Ordinal);
            Assert.True(!model.ToastStatus.Contains(ocrError, StringComparison.Ordinal) && !model.ToastStatus.Contains("LineNumber", StringComparison.Ordinal),
                $"The toast reads “{model.ToastStatus}”.");
            Assert.Equal(2, CountOf(model.ToastStatus, "The file is damaged, so Plico carries on without it."));
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
    public async Task AStartupNoticeOutlastsTheImportOfWhatTheCatchUpQueued(bool phoneInboxToo)
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var watched = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = new ImportFailureStore(settingsDirectory);
        store.Save(temporary.Path, [new ImportFailure(Path.Combine(temporary.Path, "Damaged.pdf"), "Unreadable")]);
        foreach (string file in Directory.GetFiles(settingsDirectory, "import-errors-*.json")) File.WriteAllText(file, "{ not json");
        var watchPreference = new WatchFolderPreference(settingsDirectory);
        string watchFolder = Path.Combine(watched.Path, "Scans");
        Directory.CreateDirectory(watchFolder);
        watchPreference.Save(Path.GetFullPath(temporary.Path), watchFolder);
        watchPreference.SaveCatchUpOnOpen(Path.GetFullPath(temporary.Path), true);
        // A scan landed in the watched folder while Plico was closed, which is what catching up on open is for.
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf"), Path.Combine(watchFolder, "Saved scan.pdf"));
        if (phoneInboxToo)
        {
            // The phone inbox catches up on open as well, and a transfer waits there too; each watch imports its own.
            string phoneFolder = Path.Combine(watched.Path, "Phone");
            Directory.CreateDirectory(phoneFolder);
            watchPreference.Save(Path.GetFullPath(temporary.Path), phoneFolder, WatchSource.Phone);
            watchPreference.SaveCatchUpOnOpen(Path.GetFullPath(temporary.Path), true, WatchSource.Phone);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Mixed Pages.pdf"), Path.Combine(phoneFolder, "Phone transfer.pdf"));
        }

        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: watchPreference, importFailureStore: store);
        model.InitializeLibrary(() => temporary.Path);
        string notice = model.ToastStatus;
        Assert.StartsWith("Couldn't load the saved import errors.", notice, StringComparison.Ordinal);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
        // Every time the toast rises or leaves, from the moment the window opens.
        var comings = new List<string>();
        toast.PropertyChanged += (_, change) =>
        {
            if (change.Property == Avalonia.Visual.IsVisibleProperty) comings.Add(toast.IsVisible ? "rose" : "left");
        };
        window.Show();
        try
        {
            // The catch-up queues the scan and the watch imports it as its own work, a moment later.
            int documents = phoneInboxToo ? 4 : 3;
            await WaitForAsync(() => model.InboxCount == documents && !model.IsBusy);
            await WaitForPreviewAsync(model);
            Dispatcher.UIThread.RunJobs();

            // One toast, from the window opening to the last import's end: the startup notice is still there to read,
            // with the alert, and the latest import's result follows it, as the catch-up's own message did.
            Assert.Equal(($"{notice} 1 document added.", true, "rose"),
                (model.ToastStatus, ToastWearsTheAlert(toast), string.Join(", ", comings)));
            Assert.Equal("Couldn't load the saved import errors", model.ToastTitle);
        }
        finally
        {
            await model.PrepareCloseAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AScanAfterTheCatchUpsScanHasGoneSaysOnlyItsOwnNews()
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var watched = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = new ImportFailureStore(settingsDirectory);
        store.Save(temporary.Path, [new ImportFailure(Path.Combine(temporary.Path, "Damaged.pdf"), "Unreadable")]);
        foreach (string file in Directory.GetFiles(settingsDirectory, "import-errors-*.json")) File.WriteAllText(file, "{ not json");
        var watchPreference = new WatchFolderPreference(settingsDirectory);
        string watchFolder = Path.Combine(watched.Path, "Scans");
        Directory.CreateDirectory(watchFolder);
        watchPreference.Save(Path.GetFullPath(temporary.Path), watchFolder);
        watchPreference.SaveCatchUpOnOpen(Path.GetFullPath(temporary.Path), true);
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf");
        string waiting = Path.Combine(watchFolder, "Saved scan.pdf");
        File.Copy(fixture, waiting);

        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: watchPreference, importFailureStore: store);
        model.InitializeLibrary(() => temporary.Path);
        Assert.StartsWith("Couldn't load the saved import errors.", model.ToastStatus, StringComparison.Ordinal);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
        var comings = RecordComings(toast);
        window.Show();
        try
        {
            // The catch-up queues the waiting scan, which goes (a phone's sync takes it back) before the watch imports it,
            // and another scan arrives.
            await WaitForAsync(() => model.IsWatchingFolder && !model.IsBusy);
            Assert.Contains("1 document queued", model.ToastStatus, StringComparison.Ordinal);
            File.Delete(waiting);
            File.Copy(fixture, Path.Combine(watchFolder, "Later scan.pdf"));
            await WaitForAsync(() => model.InboxCount == 3 && !model.IsBusy);
            await WaitForPreviewAsync(model);
            Dispatcher.UIThread.RunJobs();

            // That scan is not what the catch-up found, so its import says only its own news, with the check: the startup
            // notice went with the catch-up's scan and does not come back. Nor does it hold the toast up through that
            // import: the toast leaves as the import begins, as it does for any work, and the import's news rises when it
            // ends.
            Assert.Equal(("1 document added.", false, true, "rose, left, rose"),
                (model.ToastStatus, ToastWearsTheAlert(toast), toast.IsVisible, string.Join(", ", comings)));
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
    public async Task ClosingTheStartupToastLeavesTheCatchUpsImportOnlyItsOwnNews(bool duringTheCatchUp)
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var watched = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = DamageSavedImportErrors(settingsDirectory, temporary.Path);
        var watchPreference = new WatchFolderPreference(settingsDirectory);
        string watchFolder = Path.Combine(watched.Path, "Scans");
        Directory.CreateDirectory(watchFolder);
        watchPreference.Save(Path.GetFullPath(temporary.Path), watchFolder);
        watchPreference.SaveCatchUpOnOpen(Path.GetFullPath(temporary.Path), true);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf"), Path.Combine(watchFolder, "Saved scan.pdf"));

        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: watchPreference, importFailureStore: store);
        model.InitializeLibrary(() => temporary.Path);
        string notice = model.ToastStatus;
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        var toast = Named<Border>(window, "Toast");
        // Every message that is news from the person's click on Close on, and what the click left behind.
        var said = new List<string>();
        (bool Clicked, bool Busy, bool Visible) closed = default;
        void CloseTheToast()
        {
            model.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(InboxViewModel.ToastStatus)) said.Add(model.ToastStatus);
            };
            ClickAt(window, Named<Button>(window, "CloseToastButton"));
            closed = (true, model.IsBusy, toast.IsVisible);
        }
        if (duringTheCatchUp)
        {
            // The person closes the notice while the catch-up is still looking at the folder (on a slow drive, say). The
            // click lands as the catch-up says it is checking the files there, before it looks, so it is never too late.
            void CloseWhileChecking(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
            {
                if (change.PropertyName != nameof(InboxViewModel.WorkStatus) || model.WorkStatus != "Checking existing files…") return;
                model.PropertyChanged -= CloseWhileChecking;
                CloseTheToast();
            }
            model.PropertyChanged += CloseWhileChecking;
        }
        window.Show();
        try
        {
            string watching = "Watching for new or updated files in Scans. 1 document queued for the folder watch.";
            if (!duringTheCatchUp)
            {
                // The catch-up joins the notice and queues the waiting scan. The person reads the toast and closes it
                // before the watch imports the scan.
                await WaitForAsync(() => model.IsWatchingFolder && !model.IsBusy);
                Assert.Equal(($"{notice} {watching}", true, 2), (model.ToastStatus, toast.IsVisible, model.InboxCount));
                CloseTheToast();
            }
            await WaitForAsync(() => model.InboxCount == 3 && !model.IsBusy);
            // The click closed the toast, during the catch-up or after it as each case says.
            Assert.Equal((true, duringTheCatchUp, false), closed);
            await WaitForPreviewAsync(model);
            Dispatcher.UIThread.RunJobs();

            // The notice was read and closed, so it does not come back with what the catch-up or its import say: each says
            // only its own news, and the import's rises with the check.
            string[] expected = duringTheCatchUp ? [watching, "1 document added."] : ["1 document added."];
            Assert.Equal((string.Join(" | ", expected), false, true),
                (string.Join(" | ", said), ToastWearsTheAlert(toast), toast.IsVisible));
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
    public async Task TheCatchUpOnOpenSaysEachSavedWatchItResumed(bool phoneFolderGone)
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var watched = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        string settingsDirectory = Path.Combine(settings.Path, "Settings");
        var store = DamageSavedImportErrors(settingsDirectory, temporary.Path);
        // The scanner and the phone inbox both catch up whenever the library opens, and each has a file waiting, unless
        // the phone's folder has gone.
        var watchPreference = new WatchFolderPreference(settingsDirectory);
        string root = Path.GetFullPath(temporary.Path);
        string scanFolder = Path.Combine(watched.Path, "Scans");
        string phoneFolder = Path.Combine(watched.Path, "Phone");
        Directory.CreateDirectory(scanFolder);
        watchPreference.Save(root, scanFolder);
        watchPreference.SaveCatchUpOnOpen(root, true);
        watchPreference.Save(root, phoneFolder, WatchSource.Phone);
        watchPreference.SaveCatchUpOnOpen(root, true, WatchSource.Phone);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf"), Path.Combine(scanFolder, "Saved scan.pdf"));
        if (!phoneFolderGone)
        {
            Directory.CreateDirectory(phoneFolder);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Mixed Pages.pdf"), Path.Combine(phoneFolder, "Phone transfer.pdf"));
        }

        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: watchPreference, importFailureStore: store);
        model.InitializeLibrary(() => temporary.Path);
        string notice = model.ToastStatus;
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        // Every message that is news from the moment the window opens.
        var said = new List<string>();
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(InboxViewModel.ToastStatus)) said.Add(model.ToastStatus);
        };
        window.Show();
        try
        {
            await WaitForAsync(() => model.InboxCount == (phoneFolderGone ? 3 : 4) && !model.IsBusy);
            await WaitForPreviewAsync(model);
            Dispatcher.UIThread.RunJobs();

            // The catch-up's message says each watch it resumed, the scanner's first, after the startup notice; a watch
            // that could not resume is a problem, so it leads the catch-up's part, and the notice still leads the toast.
            string scanner = "Watching for new or updated files in Scans. 1 document queued for the folder watch.";
            string catchUp = phoneFolderGone
                ? $"Couldn't resume the phone inbox folder watch. The folder is unavailable. {phoneFolder} {scanner}"
                : $"{scanner} Watching for new or updated files in Phone. 1 document queued for the folder watch.";
            Assert.Equal($"{notice} {catchUp}", said.FirstOrDefault());
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALibraryThatDidNotOpenIsNamedWholeOnTheCardAtTheMinimumWindow(bool french)
    {
        if (french) DesktopText.SetLanguage(InterfaceLanguage.French);
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { Width = 900, Height = 590 };
        using (InboxLibrary.Open(temporary.Path)) model.InitializeLibrary(() => temporary.Path);
        window.DataContext = model;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var card = Assert.IsType<Panel>(window.FindControl<Panel>("StatusCard"));
            var title = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == model.StatusCardTitle);
            Assert.Equal(DesktopText.Translate("Library didn't open"), title.Text);
            var lines = title.TextLayout.TextLines;
            Assert.True(lines.All(line => !line.HasCollapsed),
                $"The card's title “{title.Text}” is cut in {title.Bounds.Width:F0} px after {lines.Count} lines.");
            // Why, in the interface's language: Plico.Core says it in English.
            string expected = french
                ? "Plico n’a pas pu verrouiller cette bibliothèque. Fermez toute autre fenêtre de Plico qui utilise ce dossier."
                : "Plico could not lock this library. Close any other copy that has this folder open.";
            Assert.Equal(expected, model.EmptyMessage);
            Assert.Equal(expected, model.ToastStatus);
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task ALibraryThatDidNotOpenStaysTheQueuesStateInEveryView()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { Width = 1280, Height = 800 };
        using (InboxLibrary.Open(temporary.Path)) model.InitializeLibrary(() => temporary.Path);
        string reason = model.LibraryProblem!;
        window.DataContext = model;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var empty = Assert.IsType<Panel>(window.FindControl<Panel>("QueueEmptyState"));
            var choose = Named<Button>(window, "EmptyChooseLibraryButton");

            // Another view, then a search: neither has a library to look in, so the queue goes on saying why, and never
            // that the library is empty or that nothing matches.
            ClickAt(window, Named<Button>(window, "AllDocumentsButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(VisibleTexts(empty).Take(2).SequenceEqual(["Library didn't open", reason]),
                $"In All documents the queue says “{string.Join(" / ", VisibleTexts(empty))}”.");
            ClickAt(window, Named<TextBox>(window, "SearchBox"));
            TypeText(window, "lease");
            await WaitForAsync(() => model.Query == "lease");
            await Task.Delay(400, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.True(VisibleTexts(empty).Take(2).SequenceEqual(["Library didn't open", reason]),
                $"After a search the queue says “{string.Join(" / ", VisibleTexts(empty))}”.");
            Assert.True(choose.IsEffectivelyVisible);
            AssertEmptyStateGlyph(empty, "Icon.Folder");
            // Nothing is counted where there is nothing to count.
            Assert.Equal("", model.ResultCount);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The empty state's tile shows the glyph with this resource key, and only that one.</summary>
    private static void AssertEmptyStateGlyph(Panel empty, string key)
    {
        var glyphs = empty.GetVisualDescendants().OfType<Icon>().Where(icon => icon.IsEffectivelyVisible).ToArray();
        var expected = Avalonia.Application.Current!.FindResource(key);
        Assert.True(glyphs.Length == 1 && ReferenceEquals(glyphs[0].Data, expected),
            $"The empty state shows {glyphs.Length} glyph(s), not {key}.");
    }

    /// <summary>Saves an import error for the library and damages the file, so opening the library cannot read it.</summary>
    private static ImportFailureStore DamageSavedImportErrors(string settingsDirectory, string libraryPath)
    {
        var store = new ImportFailureStore(settingsDirectory);
        store.Save(libraryPath, [new ImportFailure(Path.Combine(libraryPath, "Damaged.pdf"), "Unreadable")]);
        foreach (string file in Directory.GetFiles(settingsDirectory, "import-errors-*.json")) File.WriteAllText(file, "{ not json");
        return store;
    }

    /// <summary>Every time the toast rises or leaves from now on, in order.</summary>
    private static List<string> RecordComings(Border toast)
    {
        var comings = new List<string>();
        toast.PropertyChanged += (_, change) =>
        {
            if (change.Property == Avalonia.Visual.IsVisibleProperty) comings.Add(toast.IsVisible ? "rose" : "left");
        };
        return comings;
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>The toast shows its alert glyph and not the check: the message says something went wrong.</summary>
    private static void AssertToastWearsTheAlert(Border toast) =>
        Assert.True(ToastWearsTheAlert(toast), "The toast reports a failure beside the success check.");

    /// <summary>Whether the toast shows its alert glyph and not the check.</summary>
    private static bool ToastWearsTheAlert(Border toast)
    {
        var glyphs = toast.GetVisualDescendants().OfType<Icon>().Take(2).ToArray();
        return glyphs.Length == 2 && !glyphs[0].IsVisible && glyphs[1].IsVisible;
    }

    /// <summary>The words of every visible text inside <paramref name="root"/>, in layout order.</summary>
    private static string[] VisibleTexts(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text)).Select(text => text.Text!).ToArray();
}
