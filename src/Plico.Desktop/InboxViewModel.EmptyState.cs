using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

/// <summary>
/// The empty queue as the family's empty state: a title that says what is true ("No documents match"), one sentence
/// with a way forward (<see cref="EmptyMessage"/>), and one action, Clear search, when a search caused it.
/// </summary>
internal sealed partial class InboxViewModel
{
    [ObservableProperty] private string _emptyTitle = DesktopText.Translate("Nothing here yet");

    /// <summary>Whether a search is typed, so an empty result can offer to clear it.</summary>
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    [RelayCommand]
    private void ClearSearch() => Query = "";

    /// <summary>Sets the title and sentence together, so they always describe the same state.</summary>
    private void SetEmptyState(string title, string message)
    {
        EmptyTitle = title;
        EmptyMessage = message;
    }
}
