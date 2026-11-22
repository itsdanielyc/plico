using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// The one change Undo can reverse. <see cref="Title"/> names it as the toast does ("Marked reviewed", "Moved to
/// Trash") and <see cref="Body"/> says what it was about. <see cref="Revert"/> goes through the library, so the PDF
/// folder and the catalog stay consistent, and returns the document to select afterwards.
/// </summary>
internal sealed record UndoOffer(string Title, string? Body, Func<CancellationToken, Task<long?>> Revert);

/// <summary>
/// Undo belongs to its action. Each undoable action makes one offer and replaces the one before it; any other change
/// Plico makes to a document retires it, so Undo never reaches past the latest change. Looking around (searching,
/// moving through the queue, exporting, adding files) leaves the offer, and Ctrl+Z keeps it after the toast has gone.
/// </summary>
internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndo), nameof(UndoLabel))]
    private UndoOffer? _undoOffer;

    public bool HasUndo => UndoOffer is not null;

    /// <summary>What Undo would reverse, in the words of its toast.</summary>
    public string UndoLabel => UndoOffer?.Title ?? "";

    /// <summary>Reverses the current offer, and does nothing when there is none.</summary>
    [RelayCommand]
    private Task UndoAsync()
    {
        if (!CanWork || UndoOffer is not { } offer) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Undoing…"), async token =>
        {
            var library = RequireLibrary();
            long? restored = await offer.Revert(token);
            // A failed revert keeps the offer, so it can be tried again once the cause is fixed.
            if (ReferenceEquals(UndoOffer, offer)) UndoOffer = null;
            Tags = library.GetTags();
            RefreshInboxCount();
            LoadResults(restored, retainLoadedResults: true);
            Status = DesktopText.Format($"Undone: {offer.Title}.");
        });
    }

    /// <summary>Makes <paramref name="offer"/> the one Undo reverses and announces it: its toast names it and carries Undo.</summary>
    private void OfferUndo(UndoOffer offer, string status)
    {
        UndoOffer = offer;
        Announce(status, offer.Title, offer.Body);
    }

    /// <summary>A change that cannot be undone was made to a document, so Undo can no longer reach past it.</summary>
    private void RetireUndo() => UndoOffer = null;

    /// <summary>Puts documents back into or out of the Inbox, as they were before a review.</summary>
    private static UndoOffer ReviewUndo(InboxLibrary library, IReadOnlyList<long> documentIds, bool reviewed, string title, string? body) =>
        new(title, body, async token =>
        {
            await Task.Run(() => library.SetReviewedDocuments(documentIds, !reviewed), token);
            return documentIds[0];
        });

    /// <summary>Brings a PDF back from Trash to where it was.</summary>
    private static UndoOffer TrashUndo(InboxLibrary library, DocumentSummary document) =>
        new(DesktopText.Translate("Moved to Trash"), document.Title, async token =>
        {
            var restored = await library.RestoreTrashedPdfAsync(document.Id, token);
            return restored.Id;
        });

    /// <summary>
    /// Takes accepted suggestions back off one document: the tags it did not have before and, when the suggested date
    /// was accepted, the date it had before (none, since a date is only suggested for an undated document).
    /// </summary>
    private static UndoOffer FilingUndo(InboxLibrary library, long documentId, string title, string? body,
        IReadOnlyList<string> addedTags, bool restoreDate, DateOnly? previousDate) =>
        new(title, body, async token =>
        {
            // Each step is idempotent, so a revert interrupted part-way can simply run again.
            await Task.Run(() =>
            {
                foreach (string tag in addedTags) library.RemoveTag(documentId, tag);
                if (restoreDate) library.SetDocumentDate(documentId, previousDate);
            }, token);
            return documentId;
        });

    /// <summary>
    /// Takes a tag back off the documents in a selection that did not have it before. Like every other offer it is a
    /// short title over what it was about ("Tag added" over "“Tax” · 3 documents"), so the count never parts from its noun.
    /// </summary>
    private static UndoOffer BatchTagUndo(InboxLibrary library, IReadOnlyList<long> documentIds, string label, int selectedCount) =>
        new(DesktopText.Translate("Tag added"), $"“{label}” · {DocumentCount(selectedCount)}", async token =>
        {
            await Task.Run(() => library.RemoveTagFromDocuments(documentIds, label, token), token);
            return null;
        });

    /// <summary>Takes a tag back off one document, or puts a removed one back with its catalog spelling and colour.</summary>
    private static UndoOffer TagUndo(InboxLibrary library, long documentId, string label, bool added) =>
        new(DesktopText.Translate(added ? "Tag added" : "Tag removed"), $"“{label}”", async token =>
        {
            await Task.Run(() =>
            {
                if (added) library.RemoveTag(documentId, label);
                else library.AddTag(documentId, label);
            }, token);
            return documentId;
        });
}
