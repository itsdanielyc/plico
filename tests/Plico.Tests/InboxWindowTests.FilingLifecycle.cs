using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using PdfSharp.Pdf.IO;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task UndoPreservesTheAlreadyReviewedMembersOfAMixedSelection()
    {
        using var temporary = new TemporaryLibrary();
        long previouslyReviewed;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            previouslyReviewed = library.Find(new DocumentFilter()).Items[0].Id;
            library.SetReviewed(previouslyReviewed, true);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            model.ShowAllCommand.Execute(null);
            window.SelectAllShown();
            Assert.True(model.HasBatchSelection);
            await model.ReviewSelectedCommand.ExecuteAsync(null);
            Assert.All(model.Documents, document => Assert.True(document.Reviewed));
            await model.UndoCommand.ExecuteAsync(null);
            Assert.Equal(previouslyReviewed, Assert.Single(model.Documents, document => document.Reviewed).Id);
            Assert.Equal(1, model.InboxCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TrashUndoCanBeRetriedAfterAnOccupiedPathIsFreed()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        await WaitForPreviewAsync(model);
        var original = model.SelectedDocument!;
        string destination = Path.Combine(temporary.Path, "PDFs", original.RelativePath);
        await model.TrashDocumentCommand.ExecuteAsync(null);
        File.Copy(RedesignFixture("Match Ledger.pdf"), destination);
        await model.UndoCommand.ExecuteAsync(null);
        Assert.True(model.HasUndo, model.Status);
        Assert.Equal(File.ReadAllBytes(RedesignFixture("Match Ledger.pdf")), File.ReadAllBytes(destination));
        File.Delete(destination);
        await model.UndoCommand.ExecuteAsync(null);
        Assert.Equal(original.Id, model.SelectedDocument?.Id);
        Assert.False(model.HasUndo);
    }

    [AvaloniaFact]
    public async Task AcceptAllShowsAndFilesEveryMatchingRuleInOneAction()
    {
        using var temporary = new TemporaryLibrary();
        string[] labels = ["Building", "Deposit", "Housing", "Lease", "Rental", "Tenant"];
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            foreach (string label in labels) library.SaveFilingRule(new(null, label, "deposit", FilingRuleMatch.AnyWord));
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        await WaitForPreviewAsync(model);
        Assert.Equal(labels, model.SuggestedTags.Select(tag => tag.Label).Order(StringComparer.Ordinal));
        model.AcceptSuggestionsCommand.Execute(null);
        Assert.Equal(labels, model.SelectedDocument!.Tags.Order(StringComparer.Ordinal));
        Assert.False(model.SelectedDocument.Reviewed);
    }

    [AvaloniaFact]
    public async Task RefreshingTheSameSelectionKeepsItsPendingFilingSuggestions()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            library.SaveFilingRule(new(null, "Housing", "deposit", FilingRuleMatch.AnyWord));
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        model.ShowInboxCommand.Execute(null);
        await WaitForPreviewAsync(model);
        Assert.Contains(model.SuggestedTags, tag => tag.Label == "Housing");
    }

    [AvaloniaFact]
    public async Task ChangingARuleReplacesTheOpenCardsSuggestionsBeforeTheyCanBeAccepted()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            library.SaveFilingRule(new(null, "Old label", "deposit", FilingRuleMatch.AnyWord));
        }
        var dialogs = new FixtureDialogs
        {
            RuleChoice = (rules, save, remove) =>
            {
                remove(Assert.Single(rules).Id!.Value);
                save(new(null, "New label", "deposit", FilingRuleMatch.AnyWord));
                return Task.CompletedTask;
            }
        };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        await WaitForPreviewAsync(model);
        Assert.Contains(model.SuggestedTags, tag => tag.Label == "Old label");
        await model.ManageFilingRulesCommand.ExecuteAsync(null);
        Assert.DoesNotContain(model.SuggestedTags, tag => tag.Label == "Old label");
        await WaitForPreviewAsync(model);
        model.AcceptSuggestionsCommand.Execute(null);
        Assert.Contains("New label", model.SelectedDocument!.Tags);
        Assert.DoesNotContain("Old label", model.SelectedDocument.Tags);
    }

    [AvaloniaFact]
    public async Task RefreshRendersChangedThumbnailsForUnselectedDocuments()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var other = Assert.Single(model.Documents, document => document.Id != model.SelectedDocument?.Id);
            IImage? RowImage() => Assert.IsType<Image>(window.GetVisualDescendants().OfType<DocumentThumbnail>()
                .Single(row => row.Document?.Id == other.Id).Child).Source;
            await WaitForAsync(() => RowImage() is not null);
            var original = RowImage();
            string path = Path.Combine(temporary.Path, "PDFs", other.RelativePath);
            using (var revised = PdfReader.Open(RedesignFixture("Lease Renewal.pdf"), PdfDocumentOpenMode.Modify))
            {
                revised.Pages[0].Rotate = 90;
                revised.Save(path);
            }
            await model.RefreshIndexCommand.ExecuteAsync(null);
            window.UpdateLayout();
            await WaitForPreviewAsync(model);
            Assert.NotNull(RowImage());
            Assert.NotSame(original, RowImage());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SwitchingLibrariesKeepsTheOldLeaseUntilACancelledSearchReturns()
    {
        using var first = new TemporaryLibrary();
        using var second = new TemporaryLibrary();
        using var releaseReader = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var testToken = TestContext.Current.CancellationToken;
        using var model = new InboxViewModel(new FixtureDialogs { FolderPath = second.Path }, searchDocuments: (library, filter, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            if (!releaseReader.Wait(TimeSpan.FromSeconds(10), testToken)) throw new TimeoutException("The UI did not release its search reader.");
            return library.Find(filter, token);
        });
        model.InitializeLibrary(() => first.Path);
        model.Query = "lease";
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), testToken);
        var switching = model.ChooseLibraryCommand.ExecuteAsync(null);
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), testToken);
            Assert.False(switching.IsCompleted, "The library switch completed while its old search still held the catalog.");
            Assert.Throws<IOException>(() => { using var concurrentOwner = InboxLibrary.Open(first.Path); });
        }
        finally
        {
            releaseReader.Set();
            await switching;
        }
        using var reopened = InboxLibrary.Open(first.Path);
        Assert.Equal(second.Path, model.LibraryPath);
        Assert.True(model.ShowWelcome);
    }
}
