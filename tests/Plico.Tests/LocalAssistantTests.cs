using Plico.Core;
using System.Globalization;
using Xunit;

namespace Plico.Tests;

public sealed class LocalAssistantTests
{
    [Fact]
    public async Task ProposalsNeedExplicitApplyAndUndoProtectsLaterEdits()
    {
        using var folder = new TemporaryLibrary();
        using var library = InboxLibrary.Open(folder.Path);
        var token = TestContext.Current.CancellationToken;
        var document = await library.ImportAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf"), token);
        var known = library.SaveCorrespondent(null, "Northstar", []);
        var model = new FixtureLanguageModel
        {
            Answer = """{"title":"Lease decision","date":"2026-02-31","tags":["property","bad\u0000tag"],"correspondents":["Northstar","Unknown"]}"""
        };
        var proposal = await library.SuggestLocalFilingAsync(document.Id, model, OcrLanguage.English, CultureInfo.InvariantCulture, token);
        Assert.Null(proposal.Edit.DocumentDate);
        Assert.Equal("property", Assert.Single(proposal.Edit.Tags));
        Assert.Equal(known.Id, Assert.Single(proposal.Edit.CorrespondentIds));
        Assert.Equal(document.Title, Assert.Single(library.Find(new(), token).Items).Title);

        library.ApplyLocalFiling(proposal.Edit, token);
        Assert.Equal("Lease decision", Assert.Single(library.Find(new(), token).Items).Title);
        Assert.True(library.UndoLocalFiling(document.Id, token));
        Assert.Equal(document.Title, Assert.Single(library.Find(new(), token).Items).Title);
        Assert.Empty(library.ReadDocumentMetadata(document.Id).CorrespondentIds);
        library.ApplyLocalFiling(proposal.Edit, token);
        library.SetTitle(document.Id, "My later title");
        Assert.Throws<InvalidOperationException>(() => library.UndoLocalFiling(document.Id, token));
        Assert.Equal("My later title", Assert.Single(library.Find(new(), token).Items).Title);
    }

    [Fact]
    public async Task ChatSuppliesSelectedTextAndItsOnlyReference()
    {
        using var folder = new TemporaryLibrary();
        using var library = InboxLibrary.Open(folder.Path);
        var token = TestContext.Current.CancellationToken;
        var northstar = await library.ImportAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf"), token);
        await library.ImportAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf"), token);
        var model = new FixtureLanguageModel { Answer = "A renewal notice [1]." };
        var result = await library.AskLocalAssistantAsync("What is this about?", northstar.Id, model, cancellationToken: token);
        Assert.Equal(northstar.Id, Assert.Single(result.Sources).Id);
        Assert.Contains("Northstar", model.Context, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Match Ledger", model.Context, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(model.Answer, result.Text);
    }

    private sealed class FixtureLanguageModel : ILocalLanguageModel
    {
        internal string Answer { get; init; } = "";
        internal string Context { get; private set; } = "";
        public Task<string> CompleteAsync(IReadOnlyList<LocalModelMessage> messages, bool jsonObject,
            CancellationToken cancellationToken)
        {
            Context = messages[^1].Content;
            return Task.FromResult(Answer);
        }
    }
}
