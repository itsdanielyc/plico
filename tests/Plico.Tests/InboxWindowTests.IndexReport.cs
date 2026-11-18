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
    public async Task UnavailableIndexPathsSurviveReopeningAndRepeatedRefreshes()
    {
        const string letter = "Lease Renewal.pdf";
        using var temporary = new TemporaryLibrary();
        using var otherLibrary = new TemporaryLibrary();
        string original;
        string holdingPath = Path.Combine(temporary.Path, "held.pdf");
        long id;
        var token = TestContext.Current.CancellationToken;
        using (var firstSession = InboxLibrary.Open(temporary.Path))
        {
            original = Path.Combine(firstSession.PdfDirectory, letter);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", letter), original);
            Assert.Equal(1, firstSession.Refresh(token).Indexed);
            id = Assert.Single(firstSession.Find(new DocumentFilter(), token).Items).Id;
            firstSession.AddTag(id, "Housing");
            firstSession.SetReviewed(id, true);
            File.Move(original, holdingPath);
            Assert.Equal(letter, Assert.Single(firstSession.Refresh(token).UnavailablePaths));
        }

        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var report = Assert.IsType<Button>(window.FindControl<Button>("IndexReportButton"));
            Assert.True(report.IsVisible);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(report.Command).ExecuteAsync(null);
            var opened = Assert.IsType<IndexReport>(dialogs.InspectedIndexReport);
            Assert.Equal(letter, Assert.Single(opened.UnavailablePaths));
            Assert.Empty(opened.Notices);

            dialogs.FolderPath = otherLibrary.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.False(report.IsVisible);
            dialogs.FolderPath = temporary.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.True(report.IsVisible);

            await model.RefreshIndexCommand.ExecuteAsync(null);
            Assert.True(report.IsVisible);
            Assert.Contains("1 document unavailable.", model.Status, StringComparison.Ordinal);
            Assert.Empty(model.LatestIndexRefresh!.UnavailablePaths);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(report.Command).ExecuteAsync(null);
            Assert.Equal(letter, Assert.Single(Assert.IsType<IndexReport>(dialogs.InspectedIndexReport).UnavailablePaths));

            File.Move(holdingPath, original);
            await model.RefreshIndexCommand.ExecuteAsync(null);
            Assert.False(report.IsVisible);
            model.ShowAllCommand.Execute(null);
            await WaitForPreviewAsync(model);
            var restored = Assert.Single(model.Documents);
            Assert.Equal(id, restored.Id);
            Assert.True(restored.Reviewed);
            Assert.Contains("Housing", restored.Tags);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RefreshIndexShowsAndClearsTheReportAfterRecovery()
    {
        const string letter = "Lease Renewal.pdf";
        using var temporary = new TemporaryLibrary();
        string original;
        long id;
        var token = TestContext.Current.CancellationToken;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            original = Path.Combine(library.PdfDirectory, letter);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", letter), original);
            Assert.Equal(1, library.Refresh(token).Indexed);
            id = Assert.Single(library.Find(new DocumentFilter(), token).Items).Id;
            library.AddTag(id, "Housing");
        }

        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            Assert.NotNull(model.Preview);
            string holdingPath = Path.Combine(temporary.Path, "held.pdf");
            string folder = Path.GetDirectoryName(original)!;
            string firstBroken = Path.Combine(folder, "First damaged.pdf");
            string secondBroken = Path.Combine(folder, "Second damaged.pdf");
            File.Move(original, holdingPath);
            File.WriteAllText(firstBroken, "Invalid PDF");
            File.WriteAllText(secondBroken, "Another invalid PDF");
            await model.RefreshIndexCommand.ExecuteAsync(null);

            Assert.Contains("1 document unavailable.", model.Status, StringComparison.Ordinal);
            Assert.Contains("2 index notices.", model.Status, StringComparison.Ordinal);
            Assert.Empty(model.Documents);
            var report = Assert.IsType<Button>(window.FindControl<Button>("IndexReportButton"));
            Assert.True(report.IsVisible && report.IsEnabled);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(report.Command).ExecuteAsync(null);
            var inspected = Assert.IsType<IndexReport>(dialogs.InspectedIndexReport);
            Assert.Equal(letter, Assert.Single(inspected.UnavailablePaths));
            Assert.Equal(2, inspected.Notices.Count);
            Assert.Contains(inspected.Notices, message => message.StartsWith("First damaged.pdf —", StringComparison.Ordinal));
            Assert.Contains(inspected.Notices, message => message.StartsWith("Second damaged.pdf —", StringComparison.Ordinal));

            File.Move(holdingPath, original);
            File.Delete(firstBroken);
            File.Delete(secondBroken);
            await model.RefreshIndexCommand.ExecuteAsync(null);
            await WaitForPreviewAsync(model);
            Assert.Equal(id, model.SelectedDocument?.Id);
            Assert.Contains("Housing", model.SelectedDocument!.Tags);
            Assert.False(report.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
