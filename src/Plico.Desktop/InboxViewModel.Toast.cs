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
/// What the ink toast says and offers. The message of record is <see cref="Status"/> (the live region and the log);
/// the toast shows it as a short title with no full stop and an optional body ("Marked reviewed" over the document's
/// title), never cut. The action is the latest offer made, so "Exported" carries Open export and "Marked reviewed"
/// carries Undo. Undo has one offer at a time (see <see cref="UndoOffer"/>), which Ctrl+Z and the palette reach after
/// the toast has gone.
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

    /// <summary>
    /// The latest message. It is raised on every assignment, even when the words repeat ("Tag added." twice), because
    /// each one is a new event for the toast and the live region.
    /// </summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            _statusSerial++;
            _toastText = _pendingToast ?? DescribeForToast(value);
            _pendingToast = null;
            ToastIsQuiet = _pendingQuiet;
            _pendingQuiet = false;
            RaiseDerived(nameof(ToastTitle));
            RaiseDerived(nameof(ToastBody));
            RaiseDerived(nameof(HasToastBody));
            OnPropertyChanged();
            RaiseToastOffers();
        }
    }

    /// <summary>
    /// A message the window already shows by itself, such as "Library ready." when a library opens: it is the message
    /// of record and the live region, but raises no toast over the page.
    /// </summary>
    public bool ToastIsQuiet { get; private set; }

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
    public bool StatusIsProblem
    {
        get
        {
            string status = Status ?? "";
            return status.StartsWith("Could", StringComparison.Ordinal) || status.StartsWith("Impossible", StringComparison.Ordinal)
                || status.Contains("didn't", StringComparison.Ordinal) || status.Contains(" failed", StringComparison.Ordinal)
                || status.Contains("échou", StringComparison.Ordinal);
        }
    }

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
