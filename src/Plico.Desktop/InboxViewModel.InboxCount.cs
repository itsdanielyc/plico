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

    private void RefreshInboxCount()
    {
        (InboxCount, _libraryHasDocuments) = _library?.ReadInboxStatus() ?? default;
        NotifyZeroState();
    }

    private void RevealNewInboxDocument(long id)
    {
        RefreshInboxCount();
        Navigate(inboxOnly: true, preferredId: id);
    }
}
