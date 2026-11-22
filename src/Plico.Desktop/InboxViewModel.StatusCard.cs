using CommunityToolkit.Mvvm.ComponentModel;

namespace Plico.Desktop;

/// <summary>
/// The status card at the foot of the sidebar: Plico's live state in a dot, a title and a detail line. Work in
/// progress shows its own status and a progress bar; anything that needs the person comes before the watch.
/// </summary>
internal sealed partial class InboxViewModel
{
    /// <summary>The fraction of a counted batch that is done, or a negative value when the work has no count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkProgress), nameof(WorkPercent))]
    private double _workProgress = -1;

    public bool HasWorkProgress => WorkProgress >= 0;
    public double WorkPercent => Math.Clamp(WorkProgress, 0, 1) * 100;

    /// <summary>A watch stopped on its own (its folder went away or could not be read), or could not resume.</summary>
    public bool HasFailedWatch => _scannerWatch.Failed || _phoneWatch.Failed;

    /// <summary>The card's own action, Settings › Intake, applies when nothing else needs the person.</summary>
    public bool ShowStatusCardButton => !HasImportFailures && !ShowIndexReport;
    public bool ShowIndexReportCard => ShowIndexReport && !HasImportFailures;

    public bool StatusCardIsLive => !NeedsAttention && !HasFailedWatch && HasActiveWatches;
    public bool StatusCardIsWarning => NeedsAttention;
    public bool StatusCardIsFailed => !NeedsAttention && HasFailedWatch;

    /// <summary>"Watching Scans", "2 files need you", or the work in progress: "Copied 3 of 12 PDFs…".</summary>
    public string StatusCardTitle
    {
        get
        {
            if (IsBusy) return Status;
            if (HasImportFailures)
                return ImportFailures.Count == 1 ? DesktopText.Translate("1 file needs you") : DesktopText.Format($"{ImportFailures.Count} files need you");
            if (ShowIndexReport)
                return UnavailableCount switch
                {
                    0 => DesktopText.Translate("The index has notices"),
                    1 => DesktopText.Translate("1 file is missing"),
                    _ => DesktopText.Format($"{UnavailableCount} files are missing")
                };
            if (HasFailedWatch) return DesktopText.Translate("A folder watch stopped");
            if (IsWatchingFolder && IsWatchingPhoneInbox) return DesktopText.Translate("Watching 2 folders");
            if (IsWatchingFolder) return DesktopText.Format($"Watching {WatchedFolderName}");
            if (IsWatchingPhoneInbox) return DesktopText.Format($"Watching {PhoneFolderName}");
            return HasLibrary ? DesktopText.Translate("Not watching a folder") : DesktopText.Translate("No library open");
        }
    }

    /// <summary>The recognition language and where it runs, or what selecting the card will do about a problem.</summary>
    public string StatusCardDetail
    {
        get
        {
            if (!IsBusy && HasImportFailures) return DesktopText.Translate("Couldn't add them. Select to see why.");
            if (!IsBusy && ShowIndexReport) return DesktopText.Translate("Select to open the index report.");
            if (!IsBusy && HasFailedWatch) return DesktopText.Translate("Select to resume it in Settings.");
            return PlatformText.RecognitionPlace(RecognitionLanguage);
        }
    }

    private bool NeedsAttention => HasImportFailures || ShowIndexReport;
    private string PhoneFolderName => PhoneInboxFolder is { } folder ? new DirectoryInfo(folder).Name : "";

    private void ReportProgress(int done, int total)
    {
        if (total > 0) WorkProgress = Math.Clamp((double)done / total, 0, 1);
    }

    /// <summary>Raised from <see cref="OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs)"/> when a card input changes.</summary>
    private void NotifyStatusCard()
    {
        RaiseDerived(nameof(StatusCardTitle));
        RaiseDerived(nameof(StatusCardDetail));
        RaiseDerived(nameof(StatusCardIsLive));
        RaiseDerived(nameof(StatusCardIsWarning));
        RaiseDerived(nameof(StatusCardIsFailed));
        RaiseDerived(nameof(ShowStatusCardButton));
        RaiseDerived(nameof(ShowIndexReportCard));
    }

    private static bool IsStatusCardInput(string? property) => property is nameof(Status) or nameof(IsBusy) or nameof(ImportFailures)
        or nameof(HasImportFailures) or nameof(ShowIndexReport) or nameof(UnavailableCount) or nameof(IsWatchingFolder)
        or nameof(IsWatchingPhoneInbox) or nameof(WatchedFolderName) or nameof(PhoneInboxFolder) or nameof(RecognitionLanguage)
        or nameof(HasLibrary) or nameof(HasFailedWatch);
}
