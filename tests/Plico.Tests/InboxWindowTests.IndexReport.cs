using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task IndexReportOffersFolderRestoreWhenPdfsAreUnavailable()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new IndexReportDialog(new IndexReport(["Filed/Lease Renewal.pdf"], []));
        var closed = dialog.ShowDialog<IndexReportDecision?>(owner);
        try
        {
            var restore = Assert.IsType<Button>(dialog.FindControl<Button>("RestoreFromFolderButton"));
            Assert.True(restore.IsVisible && restore.IsEnabled);
            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsType<IndexReportDecision.RestoreFolder>(await closed);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await closed;
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task IndexReportSelectsAnUnavailablePathForRestore()
    {
        const string relativePath = "Filed/Lease Renewal.pdf";
        var owner = new Window();
        owner.Show();
        var dialog = new IndexReportDialog(new IndexReport([relativePath], ["The last index refresh was interrupted."]));
        var closed = dialog.ShowDialog<IndexReportDecision?>(owner);
        try
        {
            var entries = Assert.IsType<ListBox>(dialog.FindControl<ListBox>("IndexReportList"));
            var restore = Assert.IsType<Button>(dialog.FindControl<Button>("RestorePdfButton"));
            entries.SelectedIndex = 1;
            Assert.False(restore.IsEnabled);
            entries.SelectedIndex = 0;
            Assert.True(restore.IsEnabled);
            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(relativePath, Assert.IsType<IndexReportDecision.RestorePdf>(await closed).RelativePath);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await closed;
            owner.Close();
        }
    }

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

            dialogs.IndexReportChoice = new IndexReportDecision.RestorePdf(letter);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(report.Command).ExecuteAsync(null);
            Assert.Equal("PDF restore cancelled.", model.Status);
            Assert.True(report.IsVisible);
            Assert.False(File.Exists(original));

            dialogs.RestoreCandidate = holdingPath;
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(report.Command).ExecuteAsync(null);
            Assert.Equal("PDF restored.", model.Status);
            Assert.False(report.IsVisible);
            await WaitForPreviewAsync(model);
            var restored = Assert.Single(model.Documents);
            Assert.Equal(id, restored.Id);
            Assert.True(restored.Reviewed);
            Assert.Contains("Housing", restored.Tags);
            Assert.Equal(File.ReadAllBytes(holdingPath), File.ReadAllBytes(original));
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
