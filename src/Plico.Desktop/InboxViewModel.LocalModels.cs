using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using System.Globalization;

namespace Plico.Desktop;

internal sealed record LocalModelSession(ILocalLanguageModel Chat, ILocalEmbeddingModel Embeddings, IDisposable? Connection = null)
    : IDisposable
{
    public void Dispose() => Connection?.Dispose();
}

internal sealed partial class InboxViewModel
{
    private LocalModelPreference? _localModelPreference;
    private Func<LocalModelSettings, LocalModelSession> _openLocalModel = settings =>
    {
        var client = new LocalModelClient(settings);
        return new(client, client, client);
    };
    private string _latestSemanticQuery = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalChat))]
    [NotifyPropertyChangedFor(nameof(HasSemanticSearch))]
    private LocalModelSettings _localModels = LocalModelSettings.Default;

    public bool HasLocalChat => LocalModels.Enabled && !string.IsNullOrEmpty(LocalModels.ChatModel);
    public bool HasSemanticSearch => LocalModels.Enabled && !string.IsNullOrEmpty(LocalModels.EmbeddingModel);

    internal void InitializeLocalModels(LocalModelPreference settings, Func<LocalModelSettings, LocalModelSession>? open = null)
    {
        _localModelPreference = settings;
        if (open is not null) _openLocalModel = open;
        try { LocalModels = settings.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Status = error.Message; }
    }

    [RelayCommand]
    private Task ConfigureLocalModelsAsync() => RunOperationAsync("Edit local model settings…", async token =>
    {
        var changed = await _dialogs.EditLocalModelsAsync(LocalModels);
        if (changed is null) { Status = "Local model settings closed."; return; }
        changed = changed.Validate();
        _localModelPreference?.Save(changed);
        LocalModels = changed;
        Status = "Local model settings saved.";
    });

    [RelayCommand]
    private Task FindRelatedAsync() => RunOperationAsync("Finding related documents…", async token =>
    {
        if (SelectedDocument is not { } document) return;
        var library = RequireLibrary();
        var matches = await Task.Run(() => library.FindRelatedDocuments(document.Id, cancellationToken: token), token);
        await ReviewSimilarityAsync(library, "Related documents", matches);
    });

    [RelayCommand]
    private Task SearchSemanticallyAsync() => RunOperationAsync("Choose a semantic query…", async token =>
    {
        if (!HasSemanticSearch) { Status = "Enable an embedding model in Local models."; return; }
        var query = await _dialogs.EnterSemanticQueryAsync(_latestSemanticQuery);
        if (query is null) { Status = "Semantic search closed."; return; }
        _latestSemanticQuery = query;
        var library = RequireLibrary();
        using var session = _openLocalModel(LocalModels);
        Status = "Searching the local semantic index…";
        var progress = new Progress<int>(count => Status = $"Compared {count} documents…");
        var matches = await Task.Run(() => library.SearchSemanticallyAsync(query, session.Embeddings,
            progress: progress, cancellationToken: token), token);
        await ReviewSimilarityAsync(library, "Semantic matches", matches);
    });

    [RelayCommand]
    private Task RebuildSemanticIndexAsync() => RunOperationAsync("Clearing generated vectors…", async token =>
    {
        await Task.Run(RequireLibrary().ClearSemanticIndex, token);
        Status = "Semantic index cleared. The next search will rebuild it.";
    });

    private async Task ReviewSimilarityAsync(InboxLibrary library, string title, IReadOnlyList<DocumentSimilarityMatch> matches)
    {
        var selected = await _dialogs.ShowSimilarityMatchesAsync(title, matches);
        if (selected is not null) _dialogs.OpenPath(library.ResolveReminderPdf(selected.Document.Id));
        Status = matches.Count == 0 ? "No matching documents." : $"Found {DocumentCount(matches.Count)}.";
    }

    [RelayCommand]
    private Task SuggestLocalFilingAsync() => RunOperationAsync("Preparing local filing suggestions…", async token =>
    {
        if (SelectedDocument is not { } document || !HasLocalChat) return;
        var library = RequireLibrary();
        using var session = _openLocalModel(LocalModels);
        var proposal = await Task.Run(() => library.SuggestLocalFilingAsync(document.Id, session.Chat, RecognitionLanguage,
            CultureInfo.CurrentCulture, token), token);
        var edit = await _dialogs.ReviewLocalFilingAsync(proposal, library.GetMetadataDefinitions());
        if (edit is null) { Status = "Local filing suggestions closed."; return; }
        await Task.Run(() => library.ApplyLocalFiling(edit, token), token);
        Tags = library.GetTags();
        LoadResults(document.Id, retainLoadedResults: true);
        Status = "Local filing saved.";
    });

    [RelayCommand]
    private Task UndoLocalFilingAsync() => RunOperationAsync("Restoring prior metadata…", async token =>
    {
        if (SelectedDocument is not { } document) return;
        var library = RequireLibrary();
        if (!await Task.Run(() => library.UndoLocalFiling(document.Id, token), token))
        {
            Status = "There is no local filing change to undo for this PDF.";
            return;
        }
        Tags = library.GetTags();
        LoadResults(document.Id, retainLoadedResults: true);
        Status = "Local filing undone.";
    });

    [RelayCommand]
    private Task AskLocalModelAsync() => RunOperationAsync("Opening the local assistant…", async token =>
    {
        if (!HasLocalChat) { Status = "Enable a chat model in Local models."; return; }
        var library = RequireLibrary();
        var document = SelectedDocument;
        using var session = _openLocalModel(LocalModels);
        await _dialogs.ShowLocalChatAsync(new(document?.Title, AskAsync, id => _dialogs.OpenPath(library.ResolveReminderPdf(id))));
        Status = "Local assistant closed.";
        async Task<LocalAssistantAnswer> AskAsync(LocalChatRequest request, CancellationToken dialogToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, dialogToken);
            return await Task.Run(() => library.AskLocalAssistantAsync(request.Question, request.SearchLibrary ? null : document?.Id,
                session.Chat, HasSemanticSearch ? session.Embeddings : null, request.History, linked.Token), linked.Token);
        }
    });
}
