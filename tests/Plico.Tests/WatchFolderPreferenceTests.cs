using Avalonia.Headless.XUnit;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed class WatchFolderPreferenceTests
{
    [Fact]
    public void TheRememberedSourceIsScopedToItsLibrary()
    {
        using var settings = new TemporaryLibrary();
        using var firstLibrary = new TemporaryLibrary();
        using var secondLibrary = new TemporaryLibrary();
        using var firstSource = new TemporaryLibrary();
        using var secondSource = new TemporaryLibrary();
        var preference = new WatchFolderPreference(settings.Path);
        preference.Save(firstLibrary.Path, firstSource.Path);
        Assert.Null(preference.Load(secondLibrary.Path));
        preference.Save(secondLibrary.Path, secondSource.Path);

        var reopened = new WatchFolderPreference(settings.Path);
        Assert.Equal(firstSource.Path, reopened.Load(firstLibrary.Path));
        Assert.Equal(secondSource.Path, reopened.Load(secondLibrary.Path));
    }

    [AvaloniaFact]
    public async Task ReopeningOffersToResumeWithoutStartingTheWatch()
    {
        using var settings = new TemporaryLibrary();
        using var library = new TemporaryLibrary();
        using var source = new TemporaryLibrary();
        string? watched;
        using (var first = new InboxViewModel(new FixtureDialogs { FolderPath = source.Path },
                   watchPreference: new WatchFolderPreference(settings.Path)))
        {
            first.InitializeLibrary(() => library.Path);
            await first.StartFolderWatchCommand.ExecuteAsync(null);
            Assert.True(first.IsWatchingFolder);
            watched = first.WatchedFolderPath;
            await first.StopFolderWatchCommand.ExecuteAsync(null);
        }

        using var reopened = new InboxViewModel(new FixtureDialogs(), watchPreference: new WatchFolderPreference(settings.Path));
        reopened.InitializeLibrary(() => library.Path);
        Assert.False(reopened.IsWatchingFolder);
        Assert.True(reopened.CanOfferResumeFolderWatch);

        await reopened.ResumeFolderWatchCommand.ExecuteAsync(null);
        try
        {
            Assert.True(reopened.IsWatchingFolder);
            Assert.Equal(watched, reopened.WatchedFolderPath);
        }
        finally
        {
            await reopened.StopFolderWatchCommand.ExecuteAsync(null);
        }
    }
}
