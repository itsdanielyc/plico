using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task CatchUpOnOpenIsOptInAndSurvivesReopeningTheWindow()
    {
        using var library = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        var preference = new WatchFolderPreference(settings.Path);
        preference.Save(library.Path, source.Path);

        using (var first = new InboxViewModel(new FixtureDialogs(), watchPreference: preference))
        {
            first.InitializeLibrary(() => library.Path);
            var window = new MainWindow { DataContext = first };
            window.Show();
            try
            {
                Assert.False(first.IsWatchingFolder);
                var check = Assert.IsType<CheckBox>(OpenSettingsAt(window, "IntakeGroup").FindControl<CheckBox>("CatchUpOnOpenCheckBox"));
                Assert.True(check.IsEffectivelyVisible);
                Assert.Equal(false, check.IsChecked);
                check.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                Assert.False(first.IsWatchingFolder);
            }
            finally { window.Close(); }
        }

        string scan = PutScanInFolder(source.Path);
        using (var reopened = new InboxViewModel(new FixtureDialogs(), watchPreference: new WatchFolderPreference(settings.Path)))
        {
            reopened.InitializeLibrary(() => library.Path);
            var window = new MainWindow { DataContext = reopened };
            window.Show();
            try
            {
                await WaitForAsync(() => reopened.IsWatchingFolder && reopened.InboxCount == 1 && !reopened.IsBusy);
                Assert.True(File.Exists(scan));
                var check = Assert.IsType<CheckBox>(OpenSettingsAt(window, "IntakeGroup").FindControl<CheckBox>("CatchUpOnOpenCheckBox"));
                Assert.Equal(true, check.IsChecked);
                check.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                await WaitForAsync(() => reopened.Documents.Count == 1);
                await WaitForPreviewAsync(reopened);
            }
            finally
            {
                await reopened.PrepareCloseAsync();
                window.Close();
            }
        }

        using var disabled = new InboxViewModel(new FixtureDialogs(), watchPreference: new WatchFolderPreference(settings.Path));
        disabled.InitializeLibrary(() => library.Path);
        var disabledWindow = new MainWindow { DataContext = disabled };
        disabledWindow.Show();
        try
        {
            Assert.False(disabled.IsWatchingFolder);
            Assert.False(disabled.IsBusy);
            Assert.Equal(false, Assert.IsType<CheckBox>(OpenSettingsAt(disabledWindow, "IntakeGroup").FindControl<CheckBox>("CatchUpOnOpenCheckBox")).IsChecked);
            await WaitForPreviewAsync(disabled);
        }
        finally
        {
            await disabled.PrepareCloseAsync();
            disabledWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task ChoosingALibraryCatchesUpOnlyIfThatLibraryIsEnabled()
    {
        using var first = new TemporaryLibrary();
        using var second = new TemporaryLibrary();
        using var firstSource = new TemporaryLibrary();
        using var secondSource = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        var preference = new WatchFolderPreference(settings.Path);
        preference.Save(first.Path, firstSource.Path);
        preference.Save(second.Path, secondSource.Path);
        preference.SaveCatchUpOnOpen(second.Path, true);
        PutScanInFolder(firstSource.Path);
        PutScanInFolder(secondSource.Path);
        var dialogs = new FixtureDialogs { FolderPath = second.Path };
        using var model = new InboxViewModel(dialogs, watchPreference: preference);
        model.InitializeLibrary(() => first.Path);

        try
        {
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            await WaitForAsync(() => model.IsWatchingFolder && model.InboxCount == 1 && !model.IsBusy);
            Assert.Equal(second.Path, model.LibraryPath);
            Assert.Equal(Path.GetFileName(secondSource.Path), model.WatchedFolderName);
            await WaitForAsync(() => model.Documents.Count == 1);
            await WaitForPreviewAsync(model);
            await model.StopFolderWatchCommand.ExecuteAsync(null);
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Equal("Library already open.", model.Status);
            Assert.False(model.IsWatchingFolder);

            dialogs.FolderPath = first.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Equal(first.Path, model.LibraryPath);
            Assert.False(model.IsWatchingFolder);
            Assert.Equal(0, model.InboxCount);
        }
        finally { await model.PrepareCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task OpeningALibraryWithAMissingSavedSourceLeavesItUsable()
    {
        using var library = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        string missing = Path.Combine(source.Path, "Scanner output");
        var preference = new WatchFolderPreference(settings.Path);
        preference.Save(library.Path, missing);
        preference.SaveCatchUpOnOpen(library.Path, true);
        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: preference);
        model.InitializeLibrary(() => library.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForAsync(() => !model.IsBusy && model.Status.Contains("The folder is unavailable.", StringComparison.Ordinal));
            Assert.True(model.HasLibrary);
            Assert.True(model.CanWork);
            Assert.False(model.IsWatchingFolder);
            Assert.True(model.CanOfferResumeFolderWatch);
            Assert.Equal(missing, model.RememberedWatchFolder);
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
    public async Task AWatchPlicoAlreadyRemembersResumesWithTheCheckWhenItsSettingCannotBeWritten(bool resumeFromSettings)
    {
        using var library = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using var watched = new TemporaryLibrary();
        using (var opened = InboxLibrary.Open(library.Path)) BatchFixtures.AddTwoDocuments(opened, TestContext.Current.CancellationToken);
        string root = Path.GetFullPath(library.Path);
        string scans = Path.Combine(watched.Path, "Scans");
        Directory.CreateDirectory(scans);
        var preference = new WatchFolderPreference(settingsFolder.Path);
        preference.Save(root, scans);
        if (!resumeFromSettings) preference.SaveCatchUpOnOpen(root, true);
        // The watch folder's setting still reads, but nothing can be written over it: the file is read-only, and on
        // Linux and macOS, where a read-only file can still be replaced, its folder takes no new files.
        string setting = Assert.Single(Directory.GetFiles(settingsFolder.Path, "watch-folder-*.txt"));
        File.SetAttributes(setting, FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(settingsFolder.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: preference);
        model.InitializeLibrary(() => library.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            string line = "";
            bool lineWearsTheCheck = false;
            if (resumeFromSettings)
            {
                var settings = OpenSettingsAt(window, "IntakeGroup");
                ClickAt(settings, Assert.IsType<Button>(settings.FindControl<Button>("ResumeFolderWatchButton")));
                await WaitForAsync(() => model.IsWatchingFolder && !model.IsBusy);
                settings.UpdateLayout();
                line = Assert.IsType<TextBlock>(settings.FindControl<TextBlock>("SettingsMessage")).Text ?? "";
                lineWearsTheCheck = Assert.IsAssignableFrom<Control>(settings.FindControl<Control>("SettingsMessageDone")).IsVisible
                    && !Assert.IsAssignableFrom<Control>(settings.FindControl<Control>("SettingsMessageProblem")).IsVisible;
                settings.Close();
            }
            else
            {
                await WaitForAsync(() => model.IsWatchingFolder && !model.IsBusy);
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // Plico remembers the folder already, so resuming it has nothing to save and nothing that could fail: it says
            // only that it watches, with the check, in Settings' line too when Settings resumed it.
            string watching = "Watching for new or updated files in Scans.";
            var toast = Named<Border>(window, "Toast");
            Assert.Equal((watching, false, resumeFromSettings ? watching : "", resumeFromSettings),
                (model.ToastStatus, ToastWearsTheAlert(toast), line, lineWearsTheCheck));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(settingsFolder.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetAttributes(setting, FileAttributes.Normal);
            await model.PrepareCloseAsync();
            window.Close();
        }
    }

    private static string PutScanInFolder(string folder)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        string scan = Path.Combine(folder, "Saved scan.pdf");
        File.Copy(fixture, scan);
        return scan;
    }
}
