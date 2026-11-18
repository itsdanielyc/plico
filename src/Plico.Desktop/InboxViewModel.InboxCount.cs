using CommunityToolkit.Mvvm.ComponentModel;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInboxCount))]
    [NotifyPropertyChangedFor(nameof(InboxAutomationName))]
    private long _inboxCount;

    public bool HasInboxCount => InboxCount > 0;
    public string InboxAutomationName => HasInboxCount ? $"Inbox, {DocumentCount(InboxCount)} to review" : "Inbox";

    private void RefreshInboxCount() => InboxCount = _library?.CountUnreviewedDocuments() ?? 0;

    private void RevealNewInboxDocument(long id)
    {
        RefreshInboxCount();
        Navigate(inboxOnly: true, preferredId: id);
    }
}
