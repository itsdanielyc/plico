using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    public bool HasActiveTag => _activeTag is not null;

    [RelayCommand]
    private Task RenameActiveTagAsync()
    {
        if (_activeTag is not { } tag) return Task.CompletedTask;
        return RunOperationAsync("Rename tag…", async token =>
        {
            var library = RequireLibrary();
            var names = await Task.Run(library.GetCatalogTagNames, token);
            var request = await _dialogs.RenameTagAsync(tag, names);
            if (request is null)
            {
                Status = "Tag rename closed.";
                return;
            }

            string label = await Task.Run(() => library.RenameTag(tag, request.Name, request.MergeIntoExisting, token), token);
            Tags = library.GetTags();
            if (_activeTag == tag) Navigate(inboxOnly: false, tag: label, preferredId: SelectedDocument?.Id);
            else LoadResults();
            Status = request.MergeIntoExisting ? $"Tags merged into “{label}”." : $"Tag renamed to “{label}”.";
        });
    }
}
