using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// Settings with the pointer a person uses. Settings sits over the main window and its toast, so what its own actions
/// have to say appears in Settings, and a switch that cannot be changed while Plico works is turned off until it can.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ASettingsActionThatIsRefusedSaysWhyInSettings()
    {
        using var temporary = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs, watchPreference: new WatchFolderPreference(settingsFolder.Path));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var settings = OpenSettingsAt(window, "IntakeGroup");
            Assert.DoesNotContain(VisibleTexts(settings), text => text.Contains("managed folders", StringComparison.Ordinal));

            // Watch a folder… with a folder inside the library: Plico refuses it, and Settings says why.
            dialogs.FolderPath = Path.Combine(temporary.Path, "PDFs");
            ClickAt(settings, Assert.IsType<Button>(settings.FindControl<Button>("StartFolderWatchButton")));
            await WaitForAsync(() => !model.IsBusy);
            Assert.False(model.IsWatchingFolder);
            settings.UpdateLayout();
            var said = VisibleTexts(settings).Where(text => text.Contains("outside the library's managed folders", StringComparison.Ordinal)).ToArray();
            Assert.True(said.Length == 1,
                $"Settings shows nothing of “{model.ToastStatus}”; it went only to the main window's toast, under Settings.");
            var line = settings.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == said[0]);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(line));

            // News from elsewhere (a review in the main window, back in it with a click on the open row; the keyboard
            // stayed in Settings until then) is not a Settings message and leaves the line as it was.
            ClickAt(window, QueueRow(window, model.SelectedDocument!.Id));
            PressKey(window, Key.R, PhysicalKey.R, "r");
            await WaitForPreviewAsync(model);
            Assert.Equal("Marked reviewed", model.ToastTitle);
            Assert.Equal(said[0], line.Text);
            settings.Close();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ASettingsRefusalWearsTheAlertNotTheCheck()
    {
        using var temporary = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var dialogs = new FixtureDialogs { FolderPath = Path.Combine(temporary.Path, "PDFs") };
        using var model = new InboxViewModel(dialogs, watchPreference: new WatchFolderPreference(settingsFolder.Path));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var settings = OpenSettingsAt(window, "IntakeGroup");
            ClickAt(settings, Assert.IsType<Button>(settings.FindControl<Button>("StartFolderWatchButton")));
            await WaitForAsync(() => !model.IsBusy);
            Assert.Contains("outside the library's managed folders", Assert.IsType<TextBlock>(settings.FindControl<TextBlock>("SettingsMessage")).Text,
                StringComparison.Ordinal);
            var done = Assert.IsAssignableFrom<Control>(settings.FindControl<Control>("SettingsMessageDone"));
            var problem = Assert.IsAssignableFrom<Control>(settings.FindControl<Control>("SettingsMessageProblem"));
            Assert.True(problem.IsVisible && !done.IsVisible, "Settings shows its refusal beside the success check.");
            settings.Close();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AKeyThatComesUpInTheMainWindowLeavesSettingsWithNothingToSay()
    {
        using var temporary = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var dialogs = new FixtureDialogs { FolderPath = Path.Combine(temporary.Path, "PDFs") };
        using var model = new InboxViewModel(dialogs, watchPreference: new WatchFolderPreference(settingsFolder.Path));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            ClickAt(window, Named<Button>(window, "SettingsButton"));
            var settings = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
            settings.UpdateLayout();
            var line = Assert.IsType<TextBlock>(settings.FindControl<TextBlock>("SettingsMessage"));
            var bar = Assert.IsType<Border>(settings.FindControl<Border>("SettingsMessageBar"));

            // Alt+Tab from Settings: Alt goes down in Settings, the main window comes to the front, and Alt comes up
            // there. A review in the main window is the main window's news, not Settings'.
            settings.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            Dispatcher.UIThread.RunJobs();
            window.Activate();
            window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            Dispatcher.UIThread.RunJobs();
            PressKey(window, Key.R, PhysicalKey.R, "r");
            await WaitForPreviewAsync(model);
            Assert.Equal("Marked reviewed", model.ToastTitle);
            Assert.False(bar.IsVisible, $"Settings says “{line.Text}” for a review made in the main window.");

            // Settings' own action still speaks there: a refused folder, chosen with the pointer in Intake.
            ClickAt(settings, settings.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "IntakeGroup")));
            Dispatcher.UIThread.RunJobs();
            settings.UpdateLayout();
            ClickAt(settings, Assert.IsType<Button>(settings.FindControl<Button>("StartFolderWatchButton")));
            await WaitForAsync(() => !model.IsBusy);
            Assert.True(bar.IsVisible);
            Assert.Contains("outside the library's managed folders", line.Text, StringComparison.Ordinal);
            settings.Close();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("OrientPagesChoice")]
    [InlineData("RapidOcrChoice")]
    public async Task ASwitchThatWaitsForWorkKeepsTheKeyboardInSettingsAndGetsItBack(string switchName)
    {
        using var temporary = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var pending = new TaskCompletionSource<TrashDecision?>();
        var dialogs = new FixtureDialogs { TrashChoice = _ => pending.Task };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        model.InitializeOcrProcessing(new OcrProcessingPreference(settingsFolder.Path));
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        Task? work = null;
        try
        {
            await WaitForPreviewAsync(model);
            var settings = OpenSettingsAt(window, "RecognitionGroup");
            var choice = Assert.IsType<CheckBox>(settings.FindControl<CheckBox>(switchName));
            if (!choice.IsEffectivelyEnabled) return; // RapidOCR is not offered on this machine.
            TabTo(settings, choice);

            // Work begins (Trash left open holds Plico busy, as a watched folder's import does) and turns the switch off.
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(choice.IsEffectivelyEnabled);
            Assert.True(KeyboardIsIn(settings), $"While Plico works the keyboard is on {KeyboardOwner(settings)}, not in Settings.");

            // Once the work is done the keyboard is back on the switch, which Space turns on.
            pending.SetResult(null);
            await work;
            work = null;
            Dispatcher.UIThread.RunJobs();
            Assert.True(choice.IsFocused, $"After the work the keyboard is on {KeyboardOwner(settings)}.");
            PressKey(settings, Key.Space, PhysicalKey.Space, " ");
            Assert.Equal(true, choice.IsChecked);
            settings.Close();
        }
        finally
        {
            pending.TrySetResult(null);
            if (work is not null) await work;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ASwitchThatWaitsForWorkLeavesTheKeyboardWithTheMainWindowOnceThePersonHasGoneBackToIt()
    {
        using var temporary = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var pending = new TaskCompletionSource<TrashDecision?>();
        var dialogs = new FixtureDialogs { TrashChoice = _ => pending.Task };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        model.InitializeOcrProcessing(new OcrProcessingPreference(settingsFolder.Path));
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        Task? work = null;
        try
        {
            // X puts the open document in Trash, so Trash has one to restore.
            await WaitForPreviewAsync(model);
            long trashed = model.SelectedDocument!.Id;
            PressKey(window, Key.X, PhysicalKey.X, "x");
            await WaitForAsync(() => !model.IsBusy);
            await WaitForPreviewAsync(model);
            Assert.Equal("Moved to Trash", model.ToastTitle);

            var settings = OpenSettingsAt(window, "RecognitionGroup");
            var choice = Assert.IsType<CheckBox>(settings.FindControl<CheckBox>("OrientPagesChoice"));
            TabTo(settings, choice);
            bool orienting = model.AutoOrientPages;

            // Work begins (Trash left open holds Plico busy, as a watched folder's import does) and turns the switch off.
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(choice.IsEffectivelyEnabled);

            // The person goes back to the main window and works in the queue.
            LeaveAsOnADesktop(settings);
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            ClickAt(window, QueueRow(window, model.SelectedDocument!.Id));
            Assert.True(KeyboardIsOnRow(window, model.SelectedDocument!.Id), $"In the main window the keyboard is on {KeyboardOwner(window)}.");

            // The work ends by reloading the queue under the row with the keyboard, as an import that adds documents does.
            pending.SetResult(new TrashDecision(trashed, Purge: false));
            await work;
            work = null;
            Dispatcher.UIThread.RunJobs();
            await WaitForPreviewAsync(model);
            Assert.False(choice.IsFocused, $"When the work ended Settings took the keyboard from the main window, in front: it is on {KeyboardOwner(window)}.");
            Assert.True(KeyboardIsOnRow(window, model.SelectedDocument!.Id), $"After the work the keyboard is on {KeyboardOwner(window)}.");

            // Keys typed in the main window act there: Space never flips Settings' switch, and R reviews.
            PressKey(window, Key.Space, PhysicalKey.Space, " ");
            PressKey(window, Key.R, PhysicalKey.R, "r");
            await WaitForPreviewAsync(model);
            Assert.Equal(orienting, model.AutoOrientPages);
            Assert.Equal("Marked reviewed", model.ToastTitle);
            settings.Close();
        }
        finally
        {
            pending.TrySetResult(null);
            if (work is not null) await work;
            window.Close();
        }
    }

    /// <summary>
    /// The window stops being the one in front, as a desktop says when the person clicks another window. Headless keeps
    /// every shown window active, so the window hears it from its platform here, as it would on a desktop. The platform's
    /// callback is Avalonia's private API, so it is reached by reflection.
    /// </summary>
    private static void LeaveAsOnADesktop(Window window)
    {
        var platform = window.PlatformImpl;
        Assert.NotNull(platform);
        var callback = typeof(Window).Assembly.GetType("Avalonia.Platform.IWindowBaseImpl")?.GetProperty("Deactivated");
        Assert.NotNull(callback);
        var deactivated = Assert.IsType<Action>(callback.GetValue(platform));
        deactivated();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsActive, $"{window.GetType().Name} is still active after its platform said it was not.");
    }

    [AvaloniaTheory]
    [InlineData("theme")]
    [InlineData("language")]
    public async Task AChoiceSettingsSavesWhilePlicoWorksIsSaidInSettingsAndLeavesTheCardToTheWork(string choice)
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var pending = new TaskCompletionSource<TrashDecision?>();
        using var model = new InboxViewModel(new FixtureDialogs { TrashChoice = _ => pending.Task });
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        Task? work = null;
        try
        {
            await WaitForPreviewAsync(model);
            var settings = OpenSettingsAt(window, "GeneralGroup");
            var line = Assert.IsType<TextBlock>(settings.FindControl<TextBlock>("SettingsMessage"));
            var bar = Assert.IsType<Border>(settings.FindControl<Border>("SettingsMessageBar"));
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));

            // Work begins (Trash left open holds Plico busy, as a watched folder's import does); the status card says so.
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBusy);
            string working = model.StatusCardTitle;

            // The person chooses the dark theme with the pointer, or another interface language from the keyboard; each
            // is saved on the spot, while Plico works.
            string said;
            if (choice == "theme")
            {
                ClickAt(settings, settings.GetVisualDescendants().OfType<RadioButton>()
                    .Single(card => AutomationProperties.GetName(card) == "Dark"));
                Assert.True(model.UsesDarkTheme);
                said = "Appearance saved.";
            }
            else
            {
                var language = Assert.IsType<ComboBox>(settings.FindControl<ComboBox>("InterfaceLanguageBox"));
                TabTo(settings, language);
                PressKey(settings, Key.Down, PhysicalKey.ArrowDown);
                Assert.Equal(1, model.InterfaceLanguageIndex);
                said = "Appearance saved. Reopen Plico to switch the interface language.";
            }
            settings.UpdateLayout();
            Assert.True(bar.IsVisible && line.Text == said,
                $"Settings' line says “{(bar.IsVisible ? line.Text : "")}”, and the status card says “{model.StatusCardTitle}”.");
            Assert.Equal(working, model.StatusCardTitle);
            // The main window's news waits for the work.
            Assert.False(toast.IsVisible, $"The main window's toast rose under Settings while Plico works, saying “{model.ToastTitle}”.");

            // When the work ends, its own result rises in the main window, and Settings' line keeps the choice.
            pending.SetResult(null);
            await work;
            work = null;
            Dispatcher.UIThread.RunJobs();
            Assert.True(toast.IsVisible, $"No toast rose when the work ended; the toast would say “{model.ToastTitle}”.");
            Assert.Equal("Trash is empty", model.ToastTitle);
            Assert.Equal(said, line.Text);
            settings.Close();
        }
        finally
        {
            pending.TrySetResult(null);
            if (work is not null) await work;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheRapidOcrSwitchWaitsWhilePlicoWorks()
    {
        using var temporary = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var pending = new TaskCompletionSource<TrashDecision?>();
        var dialogs = new FixtureDialogs { TrashChoice = _ => pending.Task };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var preference = new OcrProcessingPreference(settingsFolder.Path);
        model.InitializeOcrProcessing(preference);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        Task? work = null;
        try
        {
            await WaitForPreviewAsync(model);
            var settings = OpenSettingsAt(window, "RecognitionGroup");
            var rapid = Assert.IsType<CheckBox>(settings.FindControl<CheckBox>("RapidOcrChoice"));
            Assert.Equal(false, rapid.IsChecked);

            // Trash left open holds Plico busy, as a watched folder's import does.
            work = model.OpenTrashCommand.ExecuteAsync(null);
            Assert.True(model.IsBusy);
            settings.UpdateLayout();
            ClickAt(settings, rapid);
            Assert.True(rapid.IsChecked == model.UseRapidOcr,
                $"While Plico works the switch shows {rapid.IsChecked} but Plico uses {(model.UseRapidOcr ? "RapidOCR" : "Tesseract")}.");
            Assert.False(rapid.IsEffectivelyEnabled, "The RapidOCR switch takes a click while Plico works.");

            pending.SetResult(null);
            await work;
            work = null;
            Assert.Equal(false, rapid.IsChecked);
            if (!model.CanUseRapidOcr) return;
            // Once the work is done, one click turns it on and saves it.
            ClickAt(settings, rapid);
            Assert.True(model.UseRapidOcr);
            Assert.Equal(true, rapid.IsChecked);
            Assert.True(preference.Load().Engine == OcrEngineKind.RapidOcr);
            settings.Close();
        }
        finally
        {
            pending.TrySetResult(null);
            if (work is not null) await work;
            window.Close();
        }
    }
}
