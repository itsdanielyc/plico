using CommunityToolkit.Mvvm.ComponentModel;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// The filing card's heading is the title's own field. E or a click opens it with the title selected; Enter or leaving
/// the field saves, Escape restores. A rename is undone from its toast, and a title that cannot be saved is rolled back
/// with a message that says why.
/// </summary>
internal sealed partial class InboxViewModel
{
    private long? _titleEditDocumentId;

    [ObservableProperty] private bool _isEditingTitle;
    [ObservableProperty] private string _titleDraft = "";

    /// <summary>Opens the heading for editing with the open document's title, or returns false when nothing can be renamed.</summary>
    internal bool BeginTitleEdit()
    {
        if (!CanWork || !ShowFilingCard || SelectedDocument is not { } document) return false;
        _titleEditDocumentId = document.Id;
        TitleDraft = document.Title;
        IsEditingTitle = true;
        return true;
    }

    /// <summary>Closes the editor and leaves the title as it was.</summary>
    internal void CancelTitleEdit()
    {
        _titleEditDocumentId = null;
        IsEditingTitle = false;
    }

    /// <summary>
    /// Saves the draft as the title of the document whose heading was opened, and closes the editor. An unchanged title
    /// saves nothing; an empty or overlong one is rolled back, and the toast says why.
    /// </summary>
    internal void CommitTitleEdit()
    {
        if (!IsEditingTitle || _titleEditDocumentId is not { } documentId) return;
        string draft = TitleDraft;
        CancelTitleEdit();
        if (_library is not { } library || !CanWork) return;
        var document = SelectedDocument?.Id == documentId ? SelectedDocument : Documents.FirstOrDefault(item => item.Id == documentId);
        if (document is null) return;
        string title;
        try
        {
            title = InboxLibrary.NormalizeTitle(draft);
        }
        catch (ArgumentException)
        {
            Status = DesktopText.Format($"Couldn't rename. Enter a title of 1 to {InboxLibrary.MaximumTitleLength} characters.");
            return;
        }
        if (title == document.Title) return;
        try
        {
            library.SetTitle(documentId, title);
            LoadResults(SelectedDocument?.Id ?? documentId, retainLoadedResults: true);
            OfferUndo(TitleUndo(library, documentId, document.Title, title), DesktopText.Translate("Title saved."));
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    /// <summary>
    /// The editor belongs to one document. A click elsewhere saves it as focus leaves the field, before the click
    /// selects anything; a selection Plico changes by itself closes it and leaves the title as it was.
    /// </summary>
    private void CloseTitleEditFor(DocumentSummary? selected)
    {
        if (IsEditingTitle && selected?.Id != _titleEditDocumentId) CancelTitleEdit();
    }

    /// <summary>Puts back the title a document had before it was renamed.</summary>
    private static UndoOffer TitleUndo(InboxLibrary library, long documentId, string previous, string current) =>
        new(DesktopText.Translate("Renamed"), $"“{current}”", async token =>
        {
            await Task.Run(() => library.SetTitle(documentId, previous), token);
            return documentId;
        });
}
