using System.Globalization;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LiveLocalModelVerification
{
    [Fact]
    public async Task InstalledLoopbackModelsRankAnswerAndProposeThroughPlico()
    {
        string endpoint = ReadConfiguration("PLICO_LIVE_MODEL_ENDPOINT");
        string chatModel = ReadConfiguration("PLICO_LIVE_CHAT_MODEL");
        string embeddingModel = ReadConfiguration("PLICO_LIVE_EMBEDDING_MODEL");
        using var provider = new LocalModelClient(new(true, endpoint, chatModel, embeddingModel));
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        var token = TestContext.Current.CancellationToken;

        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var lease = await library.ImportAsync(Path.Combine(fixtures, "Lease Renewal.pdf"), token);
        await library.ImportAsync(Path.Combine(fixtures, "Match Ledger.pdf"), token);
        var results = await library.SearchSemanticallyAsync("Northstar lease renewal agreement with the landlord", provider,
            cancellationToken: token);
        Assert.Equal(lease.Id, results[0].Document.Id);
        Assert.True(results[0].Score > results[^1].Score);

        var answer = await library.AskLocalAssistantAsync("Which named company is mentioned in the lease renewal?", lease.Id, provider,
            cancellationToken: token);
        Assert.Contains("Northstar", answer.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(lease.Id, Assert.Single(answer.Sources).Id);

        var proposal = await library.SuggestLocalFilingAsync(lease.Id, provider, OcrLanguage.English, CultureInfo.InvariantCulture, token);
        Assert.False(string.IsNullOrWhiteSpace(proposal.Edit.Title));
        Assert.Equal(lease.Title, CurrentTitle());
        const string confirmedTitle = "Reviewed local-model filing";
        library.ApplyLocalFiling(proposal.Edit with { Title = confirmedTitle }, token);
        Assert.Equal(confirmedTitle, CurrentTitle());
        Assert.True(library.UndoLocalFiling(lease.Id, token));
        Assert.Equal(lease.Title, CurrentTitle());

        string CurrentTitle() => library.Find(new()).Items.Single(item => item.Id == lease.Id).Title;
    }

    private static string ReadConfiguration(string variable) => Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException($"Set {variable} for the isolated local model test.");
}
