using Plico.Core;

namespace Plico.Desktop;

internal partial interface IWorkspaceDialogs
{
    Task<LocalModelSettings?> EditLocalModelsAsync(LocalModelSettings settings);
    Task<string?> EnterSemanticQueryAsync(string previousQuery);
    Task<DocumentSimilarityMatch?> ShowSimilarityMatchesAsync(string title, IReadOnlyList<DocumentSimilarityMatch> matches);
    Task<LocalFilingEdit?> ReviewLocalFilingAsync(LocalFilingProposal proposal, MetadataDefinitions definitions);
    Task ShowLocalChatAsync(LocalChatInput input);
}

internal sealed partial class WorkspaceDialogs
{
    public Task<LocalModelSettings?> EditLocalModelsAsync(LocalModelSettings settings) =>
        new LocalModelSettingsDialog(settings).ShowDialog<LocalModelSettings?>(owner);

    public Task<string?> EnterSemanticQueryAsync(string previousQuery) => new SemanticQueryDialog(previousQuery).ShowDialog<string?>(owner);

    public Task<DocumentSimilarityMatch?> ShowSimilarityMatchesAsync(string title, IReadOnlyList<DocumentSimilarityMatch> matches) =>
        new SimilarityResultsDialog(title, matches).ShowDialog<DocumentSimilarityMatch?>(owner);

    public Task<LocalFilingEdit?> ReviewLocalFilingAsync(LocalFilingProposal proposal, MetadataDefinitions definitions) =>
        new LocalFilingDialog(proposal, definitions).ShowDialog<LocalFilingEdit?>(owner);

    public Task ShowLocalChatAsync(LocalChatInput input) => new LocalChatDialog(input).ShowDialog(owner);
}

internal sealed partial class FixtureDialogs
{
    internal Func<LocalModelSettings, Task<LocalModelSettings?>>? LocalModelChoice { get; set; }
    internal Func<string, Task<string?>>? SemanticQueryChoice { get; set; }
    internal Func<string, IReadOnlyList<DocumentSimilarityMatch>, Task<DocumentSimilarityMatch?>>? SimilarityChoice { get; set; }
    internal Func<LocalFilingProposal, MetadataDefinitions, Task<LocalFilingEdit?>>? LocalFilingChoice { get; set; }
    internal Func<LocalChatInput, Task>? LocalChatChoice { get; set; }

    public Task<LocalModelSettings?> EditLocalModelsAsync(LocalModelSettings settings) =>
        LocalModelChoice?.Invoke(settings) ?? Task.FromResult<LocalModelSettings?>(null);

    public Task<string?> EnterSemanticQueryAsync(string previousQuery) =>
        SemanticQueryChoice?.Invoke(previousQuery) ?? Task.FromResult<string?>(null);

    public Task<DocumentSimilarityMatch?> ShowSimilarityMatchesAsync(string title, IReadOnlyList<DocumentSimilarityMatch> matches) =>
        SimilarityChoice?.Invoke(title, matches) ?? Task.FromResult<DocumentSimilarityMatch?>(null);

    public Task<LocalFilingEdit?> ReviewLocalFilingAsync(LocalFilingProposal proposal, MetadataDefinitions definitions) =>
        LocalFilingChoice?.Invoke(proposal, definitions) ?? Task.FromResult<LocalFilingEdit?>(null);

    public Task ShowLocalChatAsync(LocalChatInput input) => LocalChatChoice?.Invoke(input) ?? Task.CompletedTask;
}
