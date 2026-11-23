using System.ComponentModel;

namespace Plico.Desktop;

/// <summary>Display strings composed from other state, raised again whenever their inputs change.</summary>
internal sealed partial class InboxViewModel
{
    private const string PathSeparator = " › ";

    /// <summary>"New files go to PDFs/Inbox": where Add files, drops and scans file new documents.</summary>
    public string IntakeDestinationLabel => DesktopText.Format($"New files go to {IntakeFolderLabel}");

    /// <summary>"Watching Scans", for an active scanner-folder watch.</summary>
    public string WatchedFolderSummary => IsWatchingFolder ? DesktopText.Format($"Watching {WatchedFolderName}") : "";

    /// <summary>The library folder as a person reads it: "Documents › Plico" inside their home folder.</summary>
    public string FriendlyLibraryPath => string.Join(PathSeparator, HomeRelativeSteps(LibraryPath));

    /// <summary>A folder's steps, starting inside the person's home folder when it is there. O(n) in the path length.</summary>
    private static string[] HomeRelativeSteps(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return [];
        string path = Path.TrimEndingDirectorySeparator(folder);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            string relative = Path.GetRelativePath(home, path);
            if (relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                path = relative;
        }
        return path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>"Watching Phone", for an active phone inbox watch.</summary>
    public string PhoneInboxSummary => IsWatchingPhoneInbox ? DesktopText.Format($"Watching {PhoneFolderName}") : "";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(IntakeFolderLabel):
                RaiseDerived(nameof(IntakeDestinationLabel));
                break;
            case nameof(IsWatchingFolder) or nameof(WatchedFolderName):
                RaiseDerived(nameof(WatchedFolderSummary));
                break;
            case nameof(IsWatchingPhoneInbox) or nameof(PhoneInboxFolder):
                RaiseDerived(nameof(PhoneInboxSummary));
                break;
            case nameof(LibraryPath):
                RaiseDerived(nameof(FriendlyLibraryPath));
                break;
        }
        if (IsStatusCardInput(e.PropertyName)) NotifyStatusCard();
        TrackToastOffer(e.PropertyName);
        NotifyCardValues(e.PropertyName);
    }

    /// <summary>Raises a composed property without running the input checks again, so composition cannot loop.</summary>
    private void RaiseDerived(string property) => base.OnPropertyChanged(new PropertyChangedEventArgs(property));
}
