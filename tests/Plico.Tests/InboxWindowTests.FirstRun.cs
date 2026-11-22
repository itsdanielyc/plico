using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task AFirstRunShowsOnlyTheWelcomeWaysInUntilTheFirstDocument()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            library.SaveSearch("Receipts", "receipt", DocumentSort.Automatic);
        }
        using var model = new InboxViewModel(new FixtureDialogs(RedesignFixture("Lease Renewal.pdf")));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(model.ShowWelcome);
            var scan = Assert.IsType<Button>(window.FindControl<Button>("ScanButton"));
            var add = Assert.IsType<Button>(window.FindControl<Button>("AddFilesButton"));
            var more = Assert.IsType<Button>(window.FindControl<Button>("AddFromMenuButton"));
            // The hero carries the two ways in; the toolbar and the sidebar wait for the first document.
            Assert.False(scan.IsEffectivelyVisible, "The toolbar's Scan shows beside the welcome hero.");
            Assert.False(add.IsEffectivelyVisible);
            Assert.False(more.IsEffectivelyVisible);
            var savedSearches = Assert.IsType<StackPanel>(window.FindControl<StackPanel>("SavedSearchesSection"));
            var tags = Assert.IsType<StackPanel>(window.FindControl<StackPanel>("TagsSection"));
            var welcomeAdd = Assert.IsType<Button>(window.FindControl<Button>("WelcomeAddFilesButton"));
            var welcomeScan = Assert.IsType<Button>(window.FindControl<Button>("WelcomeScanButton"));
            Assert.True(welcomeAdd.IsEffectivelyVisible && welcomeAdd.IsEnabled);
            Assert.True(welcomeScan.IsEffectivelyVisible && welcomeScan.IsEnabled);
            Assert.False(savedSearches.IsEffectivelyVisible);
            Assert.False(tags.IsEffectivelyVisible);
            Assert.Single(model.SavedSearches);

            await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(welcomeAdd.Command).ExecuteAsync(null);
            await WaitForPreviewAsync(model);
            model.TagInput = "Housing";
            model.AddTagCommand.Execute(null);
            window.UpdateLayout();

            Assert.False(model.ShowWelcome);
            Assert.True(scan.IsEffectivelyVisible);
            Assert.True(add.IsEffectivelyVisible);
            Assert.True(more.IsEffectivelyVisible);
            Assert.True(savedSearches.IsEffectivelyVisible);
            Assert.True(tags.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ALibraryWhoseDocumentsAreAllInTrashIsNotNewAgain()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            library.SaveSearch("Leases", "lease", DocumentSort.Automatic);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            await model.TrashDocumentCommand.ExecuteAsync(null);
            await WaitForPreviewAsync(model);
            await model.TrashDocumentCommand.ExecuteAsync(null);
            window.UpdateLayout();

            // Both PDFs are in Trash, not gone: the library keeps its saved searches and its toolbar, and says the Inbox is clear.
            Assert.False(model.HasDocuments);
            Assert.False(model.ShowWelcome);
            Assert.True(model.ShowInboxClear);
            Assert.True(Assert.IsType<StackPanel>(window.FindControl<StackPanel>("SavedSearchesSection")).IsEffectivelyVisible);
            Assert.True(Assert.IsType<Button>(window.FindControl<Button>("ScanButton")).IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
