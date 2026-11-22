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
    /// all unavailable on a drive that is not plugged in, is not new: it keeps its saved searches, its tags and its
    /// toolbar, and only a library that has had no document opens on the welcome page. Two more catalog reads, only
    /// while no PDF is present: O(u + t) for u unavailable and t trashed records.
    /// </summary>
    private void RefreshInboxCount()
    {
        (InboxCount, bool hasPresentDocuments) = _library?.ReadInboxStatus() ?? default;
        _libraryHasDocuments = hasPresentDocuments ||
            (_library is { } library && (library.CountUnavailableDocuments() > 0 || library.GetTrash().Count > 0));
        NotifyZeroState();
    }

    private void RevealNewInboxDocument(long id)
    {
        RefreshInboxCount();
        Navigate(inboxOnly: true, preferredId: id);
    }
}
