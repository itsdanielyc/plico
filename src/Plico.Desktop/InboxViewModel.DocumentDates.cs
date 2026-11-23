using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// The document date is a field on the filing card. It shows the date as the interface writes it ("21 Nov 2026"),
/// takes a date typed the same way, holds the dashed suggestion while it is empty, and saves on Enter, on leaving it,
/// from its calendar or its clear button. A date that cannot be read stays in the field with one line saying what to
/// type, and Escape puts the saved date back. Several selected documents keep their dialog.
/// </summary>
internal sealed partial class InboxViewModel
{
    // The document and date the field's text was last set from, so a reload of the same document keeps what is typed.
    private (long? Id, DateOnly? Date) _dateFieldSource;

    [ObservableProperty] private string _documentDateText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocumentDateError))]
    private string _documentDateError = "";

    public bool HasDocumentDateError => DocumentDateError.Length > 0;

    /// <summary>What the empty field says, or nothing while the suggested date sits in it instead.</summary>
    public string DocumentDatePlaceholder => HasSuggestedDate ? "" : DesktopText.Translate("Add a date");

    /// <summary>The dashed suggestion sits in the field while it is empty, and steps aside once a date is typed.</summary>
    public bool ShowSuggestedDateInField => HasSuggestedDate && string.IsNullOrWhiteSpace(DocumentDateText);

    /// <summary>
    /// The field's help, its tooltip and what a screen reader says with it: how to type a date, or, while the suggested
    /// date sits in the empty field, that date and the key that files it.
    /// </summary>
    public string DocumentDateFieldHelp => ShowSuggestedDateInField
        ? PlatformText.SuggestedDateHelp(SuggestedDateLabel)
        : DesktopText.ByKey("DocumentDateFieldTip");

    /// <summary>True while the field holds the open document's saved date, with nothing new typed over it.</summary>
    internal bool DocumentDateTextIsSaved => !HasDocumentDateError && DocumentDateText.Trim() == SelectedDocumentDateLabel;

    /// <summary>The D keycap sits in the field while it has neither a date nor a suggestion to show.</summary>
    public bool ShowDateKeycap => !HasSelectedDocumentDate && !HasSuggestedDate;

    /// <summary>
    /// Saves what is typed in the date field: a date, or nothing to clear it. False when it cannot be read. It saves while
    /// Plico works in the background too. When the field lost the keyboard to a click, <paramref name="reloadAfterInput"/>
    /// waits for that click before the results reload.
    /// </summary>
    internal bool CommitDocumentDate(bool reloadAfterInput = false)
    {
        if (!HasLibrary || SelectedDocument is not { } document) return false;
        string text = DocumentDateText.Trim();
        DateOnly? date = null;
        if (text.Length > 0)
        {
            if (!DisplayDates.TryParseDay(text, out var typed))
            {
                DocumentDateError = DesktopText.Format($"Use a date like {DisplayDates.Day(DateOnly.FromDateTime(DateTime.Today))}.");
                return false;
            }
            date = typed;
        }
        SaveDocumentDate(document, date, reloadAfterInput);
        return true;
    }

    /// <summary>Puts the open document's saved date back into the field, as Escape does.</summary>
    internal void RestoreDocumentDateText()
    {
        _dateFieldSource = (SelectedDocument?.Id, SelectedDocument?.DocumentDate);
        DocumentDateText = SelectedDocumentDateLabel;
        DocumentDateError = "";
    }

    /// <summary>Saves a day chosen in the field's calendar.</summary>
    internal void PickDocumentDate(DateOnly date)
    {
        if (HasLibrary && SelectedDocument is { } document) SaveDocumentDate(document, date);
    }

    [RelayCommand]
    private void ClearDocumentDate()
    {
        if (HasLibrary && SelectedDocument is { } document) SaveDocumentDate(document, null);
    }

    private void SaveDocumentDate(DocumentSummary document, DateOnly? date, bool reloadAfterInput = false) =>
        SaveEdit(() => SaveDocumentDateNow(document, date, reloadAfterInput));

    private void SaveDocumentDateNow(DocumentSummary document, DateOnly? date, bool reloadAfterInput)
    {
        if (date == document.DocumentDate)
        {
            RestoreDocumentDateText();
            return;
        }
        try
        {
            var library = RequireLibrary();
            library.SetDocumentDate(document.Id, date);
            ReloadAfterEdit(document.Id, reloadAfterInput);
            // A reload that waits for a click leaves the typed date in place; the reload writes it the interface's way.
            if (reloadAfterInput) DocumentDateError = "";
            else RestoreDocumentDateText();
            OfferUndo(DateUndo(library, document, date),
                date.HasValue ? DesktopText.Translate("Date set.") : DesktopText.Translate("Date cleared."));
        }
        catch (Exception error)
        {
            Fail(error.Message);
        }
    }

    /// <summary>Keeps the field in step with the open document, but leaves what is typed when the same document reloads.</summary>
    private void SyncDocumentDateField()
    {
        if (_dateFieldSource != (SelectedDocument?.Id, SelectedDocument?.DocumentDate)) RestoreDocumentDateText();
    }

    /// <summary>Puts back the date a document had before it was set or cleared.</summary>
    private static UndoOffer DateUndo(InboxLibrary library, DocumentSummary document, DateOnly? date) =>
        UndoOffer.InPlace(DesktopText.Translate(date.HasValue ? "Date set" : "Date cleared"), date is { } day ? DisplayDates.Day(day) : document.Title,
            () =>
            {
                library.SetDocumentDate(document.Id, document.DocumentDate);
                return document.Id;
            });

    internal Task EditBatchDocumentDatesAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selection = _batchSelection;
        var selectedIds = selection.Select(document => document.Id).ToArray();
        DateOnly? commonDate = selection[0].DocumentDate;
        if (commonDate.HasValue && selection.Any(document => document.DocumentDate != commonDate)) commonDate = null;

        return RunOperationAsync("Edit document dates…", async token =>
        {
            var edit = await _dialogs.EditBatchDocumentDatesAsync(commonDate, selectedIds.Length);
            if (edit is null)
            {
                Status = DesktopText.Translate("Document date editor closed.");
                return;
            }

            var library = RequireLibrary();
            await Task.Run(() => library.SetDocumentDates(selectedIds, edit.Date, token), token);
            RetireUndo();
            LoadResults(retainLoadedResults: true);
            string count = DocumentCount(selectedIds.Length);
            // "Date set" over "2 documents", like the single document's toast, so the title stays a few words.
            if (edit.Date.HasValue) Announce(DesktopText.Format($"Date set for {count}."), DesktopText.Translate("Date set"), count);
            else Announce(DesktopText.Format($"Date cleared for {count}."), DesktopText.Translate("Date cleared"), count);
        });
    }
}
