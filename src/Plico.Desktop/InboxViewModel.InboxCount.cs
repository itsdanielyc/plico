using CommunityToolkit.Mvvm.ComponentModel;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInboxCount))]
    [NotifyPropertyChangedFor(nameof(InboxAutomationName))]
    private long _inboxCount;

    public bool HasInboxCount => InboxCount > 0;
    public string InboxAutomationName => HasInboxCount ? DesktopText.Format($"Inbox, {DocumentCount(InboxCount)} to review") : DesktopText.Translate("Inbox");

    /// <summary>
    /// Reads the Inbox count and whether the library has ever had a document. A library whose PDFs are all in Trash, or
    /// all unavailable on a drive that is not plugged in, keeps its saved searches, tags and toolbar. The catalog
    /// checks for a single record in the same snapshot as the count.
    /// </summary>
    private void RefreshInboxCount()
    {
        (InboxCount, _libraryHasDocuments) = _library?.ReadInboxStatus(includeMissingAndTrashed: true) ?? default;
        NotifyZeroState();
    }

    private void RevealNewInboxDocument(long id)
    {
        RefreshInboxCount();
        Navigate(inboxOnly: true, preferredId: id);
    }
}
