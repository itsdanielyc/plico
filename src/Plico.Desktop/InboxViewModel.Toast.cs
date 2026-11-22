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

/// <summary>
/// What the ink toast says and offers. The message is <see cref="Status"/>; the action is the latest offer made, so
/// "Exported…" carries Open export and "Marked as reviewed." carries Undo. Older offers stay in the command palette.
/// </summary>
internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastOffersUndo), nameof(ToastOffersBackup), nameof(ToastOffersExport), nameof(ToastOffersOcrResults))]
    private ToastOffer _toastOffer;

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
            OnPropertyChanged();
            RaiseToastOffers();
        }
    }

    // Counts status messages, so an offer travels with the message that follows it and not with later, unrelated ones
    // ("Tag added." never carries the Undo of an earlier review). Ctrl+Z and the palette still reach every offer.
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
            case nameof(PendingUndo):
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
