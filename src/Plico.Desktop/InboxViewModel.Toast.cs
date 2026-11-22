using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Plico.Desktop;

/// <summary>The one action an ink toast offers beside its message: the latest that became available wins.</summary>
internal enum ToastOffer
{
    None,
    Undo,
    OpenBackup,
    OpenExport,
    OcrResults
}

/// <summary>What a toast says: a short title in the family voice and an optional body line.</summary>
internal readonly record struct ToastText(string Title, string? Body);

/// <summary>
/// What the ink toast says and offers. The message of record is <see cref="Status"/>, every line included; the toast
/// carries <see cref="ToastStatus"/>, the latest message that is news, as a short title with no full stop and an
/// optional body ("Marked reviewed" over the document's title), never cut. While work runs, its lines go to the status
/// card (<see cref="WorkStatus"/>) and its last message becomes the toast when it ends; work that ends on a line still
/// in progress ("Set the archive number…") has nothing to say and raises no toast. An edit the person makes meanwhile
/// is news at once (<see cref="ToastIsForeground"/>). The action is the latest offer made, so "Exported" carries Open
/// export and "Marked reviewed" carries Undo. Undo has one offer at a time (see <see cref="UndoOffer"/>), which Ctrl+Z
/// and the palette reach after the toast has gone.
/// </summary>
internal sealed partial class InboxViewModel
{
    // A sentence ends at a full stop, question mark or exclamation mark followed by space and a capital, a digit or
    // an opening quote, so "58.50" and "PDFs/Inbox" stay whole.
    private static readonly Regex SentenceEnd = new(@"[.!?](?=\s+[\p{Lu}\p{N}“""«])", RegexOptions.CultureInvariant);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastOffersUndo), nameof(ToastOffersBackup), nameof(ToastOffersExport), nameof(ToastOffersOcrResults))]
    private ToastOffer _toastOffer;

    private ToastText _toastText;
    private ToastText? _pendingToast;
    private bool _pendingQuiet;
    private string _toastStatus = "";

    // The latest line of the work in hand and what its toast would say (see RunOperationAsync). A line that ends in "…"
    // says the work is under way; any other is the work's message, which becomes the toast when the work ends.
    private string _workStatus = "";
    private ToastText _workToast;
    private bool _workQuiet;
    private bool _workHasMessage;
    private ToastOffer _workOffer;

    // Above zero while the person's own edit (a title, a date, a tag, or its Undo) is saved; its messages are news at
    // once, even while work runs.
    private int _editsInHand;

    /// <summary>
    /// The latest message, a line of the work in hand included. It is raised on every assignment, even when the words
    /// repeat ("Tag added." twice), because each one is a new event.
    /// </summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            var toast = _pendingToast ?? DescribeForToast(value);
            bool quiet = _pendingQuiet;
            _pendingToast = null;
            _pendingQuiet = false;
            if (IsBusy && _editsInHand == 0) NoteWorkLine(value, toast, quiet);
            else ShowMessage(value, toast, quiet, foreground: IsBusy);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The message the toast carries and its live region speaks: the latest message that is news, never a line of the
    /// work in hand. It is raised for every such message, even when the words repeat.
    /// </summary>
    public string ToastStatus => _toastStatus;

    /// <summary>What the work in hand is doing, for the status card while Plico works ("Copied 3 of 12 PDFs…").</summary>
    public string WorkStatus => _workStatus;

    /// <summary>
    /// A message the window already shows by itself, such as "Library ready." when a library opens: it is the message
    /// of record and the live region, but raises no toast over the page.
    /// </summary>
    public bool ToastIsQuiet { get; private set; }

    /// <summary>
    /// A message that rises and stays while Plico works: the person's own edit, saved beside the work. Other news waits
    /// until the work ends.
    /// </summary>
    public bool ToastIsForeground { get; private set; }

    /// <summary>The toast's title: two to five words with no full stop ("Marked reviewed", "Backup saved").</summary>
    public string ToastTitle => _toastText.Title;

    /// <summary>The toast's second line at 84 %: what it was about or where it went ("Documents › Plico backups").</summary>
    public string ToastBody => _toastText.Body ?? "";
    public bool HasToastBody => !string.IsNullOrEmpty(_toastText.Body);

    // Counts status messages, so an offer travels with the message that follows it and not with later, unrelated ones
    // ("Exported" never carries the Undo of an earlier review). Ctrl+Z and the palette reach the current Undo offer.
    private long _statusSerial;
    private long _offerSerial = long.MinValue / 2;
    private bool OfferIsCurrent => _offerSerial >= _statusSerial - 1;

    public bool ToastOffersUndo => ToastOffer == ToastOffer.Undo && HasUndo && OfferIsCurrent;
    public bool ToastOffersBackup => ToastOffer == ToastOffer.OpenBackup && HasLatestBackup && OfferIsCurrent;
    public bool ToastOffersExport => ToastOffer == ToastOffer.OpenExport && HasLatestExport && OfferIsCurrent;
    public bool ToastOffersOcrResults => ToastOffer == ToastOffer.OcrResults && HasOcrCopyBatchResults && OfferIsCurrent;

    /// <summary>
    /// Whether the message reports a failure, so the toast shows an alert glyph instead of a check. Failures follow the
    /// family's two shapes ("Couldn't …", "… didn't …") or Plico's older "Could not …" and "… failed" wording.
    /// </summary>
    public bool StatusIsProblem => IsProblem(Status);

    /// <summary>Whether the toast's message reports a failure; see <see cref="StatusIsProblem"/>.</summary>
    public bool ToastIsProblem => IsProblem(_toastStatus);

    private static bool IsProblem(string? message)
    {
        string status = message ?? "";
        return status.StartsWith("Could", StringComparison.Ordinal) || status.StartsWith("Impossible", StringComparison.Ordinal)
            || status.Contains("didn't", StringComparison.Ordinal) || status.Contains(" failed", StringComparison.Ordinal)
            || status.Contains("échou", StringComparison.Ordinal);
    }

    /// <summary>Makes <paramref name="message"/> the toast's: the window shows it, unless it is quiet.</summary>
    private void ShowMessage(string message, ToastText toast, bool quiet, bool foreground)
    {
        _statusSerial++;
        _toastStatus = message;
        _toastText = toast;
        ToastIsQuiet = quiet;
        ToastIsForeground = foreground;
        RaiseDerived(nameof(ToastTitle));
        RaiseDerived(nameof(ToastBody));
        RaiseDerived(nameof(HasToastBody));
        RaiseDerived(nameof(ToastIsProblem));
        RaiseDerived(nameof(ToastStatus));
        RaiseToastOffers();
    }

    /// <summary>Keeps a line of the work in hand for the status card, and as the toast to show when the work ends.</summary>
    private void NoteWorkLine(string line, ToastText toast, bool quiet)
    {
        _workStatus = line;
        _workToast = toast;
        _workQuiet = quiet;
        _workHasMessage = !line.EndsWith('…');
        RaiseDerived(nameof(WorkStatus));
    }

    /// <summary>Starts a new piece of work: its working status goes to the status card and is never its message.</summary>
    private void BeginWorkMessages(string workingStatus)
    {
        _workOffer = ToastOffer.None;
        Status = workingStatus;
        _workHasMessage = false;
    }

    /// <summary>
    /// Ends the work in hand: its last message becomes the toast, with any action the work offered. Work that ended on
    /// a line still in progress, or with no line of its own, has nothing to say, so the toast and its offer stay as they
    /// were and nothing rises. Call it once the work is no longer busy.
    /// </summary>
    private void FinishWorkMessages()
    {
        var offer = _workOffer;
        _workOffer = ToastOffer.None;
        if (!_workHasMessage) return;
        _workHasMessage = false;
        ShowMessage(_workStatus, _workToast, _workQuiet, foreground: false);
        if (offer != ToastOffer.None) UpdateToastOffer(available: true, offer);
    }

    /// <summary>Saves the person's own edit; what it says is news at once, even while Plico works.</summary>
    private T SaveEdit<T>(Func<T> edit)
    {
        _editsInHand++;
        try
        {
            return edit();
        }
        finally
        {
            _editsInHand--;
        }
    }

    private void SaveEdit(Action edit) => SaveEdit(() =>
    {
        edit();
        return true;
    });

    /// <summary>Sets the message of record without a toast, for news the window already shows.</summary>
    private void Note(string status)
    {
        _pendingQuiet = true;
        Status = status;
    }

    /// <summary>Sets the message of record and gives its toast a short title and an optional body.</summary>
    private void Announce(string status, string title, string? body = null)
    {
        _pendingToast = new ToastText(title, string.IsNullOrWhiteSpace(body) ? null : body);
        Status = status;
    }

    /// <summary>Announces a finished export: "Exported" over what was exported and the folder it went to.</summary>
    private void AnnounceExport(string status, string what) =>
        Announce(status, DesktopText.Translate("Exported"), $"{what} · {FriendlyPath(LatestExportLocation)}");

    /// <summary>
    /// The toast's words for a message that did not choose its own: the first sentence as the title without its full
    /// stop, the rest as the body, and folders the person knows written as friendly paths instead of raw ones.
    /// O(n) in the message length.
    /// </summary>
    private ToastText DescribeForToast(string? status)
    {
        string text = (status ?? "").Trim();
        foreach (string? location in new[] { LatestBackupLocation, LatestExportLocation, LibraryPath })
            if (!string.IsNullOrEmpty(location) && Path.IsPathRooted(location))
                text = text.Replace(location, FriendlyPath(location), StringComparison.Ordinal);
        var end = SentenceEnd.Match(text);
        string title = end.Success ? text[..(end.Index + 1)] : text;
        string? body = end.Success ? text[(end.Index + 1)..].Trim() : null;
        return new ToastText(WithoutFullStop(title), string.IsNullOrEmpty(body) ? null : body);
    }

    private static string WithoutFullStop(string title) =>
        title.EndsWith('.') && !title.EndsWith("..", StringComparison.Ordinal) ? title[..^1] : title;

    /// <summary>
    /// A folder as a person reads it: "Documents › Plico backups" inside their home folder, and only the last two
    /// steps of a deeper path ("… › Plico › Exports").
    /// </summary>
    internal static string FriendlyPath(string? folder)
    {
        string[] steps = HomeRelativeSteps(folder);
        return steps.Length <= 3 ? string.Join(PathSeparator, steps) : "…" + PathSeparator + string.Join(PathSeparator, steps[^2..]);
    }

    /// <summary>Records an export destination and offers it, even when it is the same folder as the last export.</summary>
    private void NoteExport(string? location)
    {
        LatestExportLocation = location;
        UpdateToastOffer(location is not null, ToastOffer.OpenExport);
    }

    private void UpdateToastOffer(bool available, ToastOffer offer)
    {
        if (available && IsBusy && _editsInHand == 0)
        {
            // An action offered by the work in hand travels with the work's message, when the work ends.
            _workOffer = offer;
            return;
        }
        if (!available && _workOffer == offer) _workOffer = ToastOffer.None;
        if (available)
        {
            _offerSerial = _statusSerial;
            ToastOffer = offer;
        }
        else if (ToastOffer == offer)
        {
            ToastOffer = ToastOffer.None;
        }
        RaiseToastOffers();
    }

    private void RaiseToastOffers()
    {
        RaiseDerived(nameof(ToastOffersUndo));
        RaiseDerived(nameof(ToastOffersBackup));
        RaiseDerived(nameof(ToastOffersExport));
        RaiseDerived(nameof(ToastOffersOcrResults));
    }

    /// <summary>Called from the property hook: a new undo, backup, export or OCR report becomes the toast's action.</summary>
    private void TrackToastOffer(string? property)
    {
        switch (property)
        {
            case nameof(UndoOffer):
                UpdateToastOffer(HasUndo, ToastOffer.Undo);
                break;
            case nameof(LatestBackupLocation):
                UpdateToastOffer(HasLatestBackup, ToastOffer.OpenBackup);
                break;
            case nameof(LatestExportLocation):
                UpdateToastOffer(HasLatestExport, ToastOffer.OpenExport);
                break;
            case nameof(OcrCopyBatchResults):
                UpdateToastOffer(HasOcrCopyBatchResults, ToastOffer.OcrResults);
                break;
            case nameof(Status):
                RaiseDerived(nameof(StatusIsProblem));
                break;
        }
    }
}
