using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

/// <summary>
/// Reverses the latest review-loop change. <paramref name="Revert"/> returns the document to select afterwards.
/// Each revert goes through the library, so the PDF folder and the catalog stay consistent.
/// </summary>
internal sealed record UndoAction(string Label, Func<CancellationToken, Task<long?>> Revert, long? TrashedDocumentId = null);

internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndo), nameof(UndoLabel))]
    private UndoAction? _pendingUndo;

    public bool HasUndo => PendingUndo is not null;
    public string UndoLabel => PendingUndo?.Label ?? "";

    [RelayCommand]
    private Task UndoAsync()
    {
        if (!CanWork || PendingUndo is not { } undo) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Undoing…"), async token =>
        {
            var library = RequireLibrary();
            long? restored = await undo.Revert(token);
            PendingUndo = null;
            Tags = library.GetTags();
            RefreshInboxCount();
            LoadResults(restored, retainLoadedResults: true);
            Status = DesktopText.Format($"Undone: {undo.Label}.");
        });
    }

    private void OfferReviewUndo(IReadOnlyList<long> documentIds, bool reviewed)
    {
        var library = _library;
        if (library is null || documentIds.Count == 0) return;
        string label = documentIds.Count == 1
            ? DesktopText.Translate(reviewed ? "Marked reviewed" : "Returned to Inbox")
            : DesktopText.Format($"{DocumentCount(documentIds.Count)} {(reviewed ? DesktopText.Translate("marked reviewed") : DesktopText.Translate("returned to Inbox"))}");
        PendingUndo = new UndoAction(label, async token =>
        {
            await Task.Run(() => library.SetReviewedDocuments(documentIds, !reviewed), token);
            return documentIds[0];
        });
    }

    private void OfferTrashUndo(long documentId, string title)
    {
        var library = _library;
        if (library is null) return;
        PendingUndo = new UndoAction(DesktopText.Format($"Moved {title} to Trash"), async token =>
        {
            var restored = await library.RestoreTrashedPdfAsync(documentId, token);
            return restored.Id;
        }, TrashedDocumentId: documentId);
    }
}
