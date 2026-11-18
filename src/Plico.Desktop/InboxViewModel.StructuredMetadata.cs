using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task ManageMetadataAsync() => RunOperationAsync("Edit metadata definitions…", async token =>
    {
        await _dialogs.ManageMetadataDefinitionsAsync(RequireLibrary());
        RefreshTagNavigation();
        Status = "Metadata definitions closed.";
    });

    [RelayCommand]
    private Task EditMetadataAsync() => RunOperationAsync("Edit document metadata…", async token =>
    {
        if (SelectedDocument is not { } document) return;
        var library = RequireLibrary();
        var input = await Task.Run(() =>
        {
            var definitions = library.GetMetadataDefinitions();
            IReadOnlyList<MetadataLinkTarget> links = definitions.CustomFields.Any(field => field.Kind == CustomFieldKind.DocumentLink)
                ? library.GetMetadataLinkTargets(document.Id, token) : [];
            return new MetadataEditInput(document, library.ReadDocumentMetadata(document.Id), definitions,
                library.SuggestCorrespondents(document.Id, token), links);
        }, token);
        var changed = await _dialogs.EditStructuredMetadataAsync(input);
        if (changed is null)
        {
            Status = "Metadata editor closed.";
            return;
        }
        await Task.Run(() => library.SaveDocumentMetadata(document.Id, changed, token), token);
        LoadResults(document.Id, retainLoadedResults: true);
        Status = "Document metadata saved.";
    });
}
