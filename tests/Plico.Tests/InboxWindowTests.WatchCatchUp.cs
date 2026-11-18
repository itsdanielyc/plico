using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task AStoppedWatchResumesAndCatchesUpWithoutDuplicatingScans()
    {
        const string existingName = "Previously imported.pdf";
        const string missedName = "Saved while closed.pdf";
        var token = TestContext.Current.CancellationToken;
        using var root = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        string existing = Path.Combine(source.Path, existingName);
        string missed = Path.Combine(source.Path, missedName);
        File.Copy(Path.Combine(fixtures, "Lease Renewal.pdf"), existing);
        long openId;
        using (var library = InboxLibrary.Open(root.Path))
        {
            openId = (await library.ImportAsync(existing, token)).Id;
        }
        var watchPreference = new WatchFolderPreference(settings.Path);
        watchPreference.Save(root.Path, source.Path);
        File.Copy(Path.Combine(fixtures, "Match Ledger.pdf"), missed);

        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: watchPreference);
        model.InitializeLibrary(() => root.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            Assert.False(model.IsWatchingFolder);
            var action = Assert.IsType<Button>(window.FindControl<Button>("ImportWatchFolderFilesButton"));
            Assert.True(action.IsEffectivelyVisible);
            Assert.Equal("Resume and catch up", action.Content);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(action.Command).ExecuteAsync(null);
            await WaitForAsync(() => !model.IsBusy && model.Status == "1 document added. 1 document already in the library.");

            Assert.True(model.IsWatchingFolder);
            Assert.Equal("Import existing files", action.Content);
            Assert.Equal(2, model.InboxCount);
            Assert.Equal(openId, model.SelectedDocument?.Id);
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root.Path, "PDFs"), "*.pdf").Count());
            Assert.True(File.Exists(Path.Combine(root.Path, "PDFs", missedName)));
            Assert.True(File.Exists(existing));
            Assert.True(File.Exists(missed));
        }
        finally
        {
            await model.StopFolderWatchCommand.ExecuteAsync(null);
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CatchUpWithAMissingFolderLeavesTheWatchStoppedAndRetryable()
    {
        using var library = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        string missing = Path.Combine(source.Path, "Scanner output");
        var preference = new WatchFolderPreference(settings.Path);
        preference.Save(library.Path, missing);
        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: preference);
        model.InitializeLibrary(() => library.Path);

        await model.ImportWatchFolderFilesCommand.ExecuteAsync(null);

        Assert.False(model.IsWatchingFolder);
        Assert.True(model.CanOfferResumeFolderWatch);
        Assert.Equal(missing, model.RememberedWatchFolder);
        Assert.Contains("The folder is unavailable.", model.Status, StringComparison.Ordinal);

        Directory.CreateDirectory(missing);
        await model.ImportWatchFolderFilesCommand.ExecuteAsync(null);
        try
        {
            Assert.True(model.IsWatchingFolder);
            Assert.Equal(Path.GetFileName(missing), model.WatchedFolderName);
        }
        finally
        {
            await model.StopFolderWatchCommand.ExecuteAsync(null);
        }
    }
}
