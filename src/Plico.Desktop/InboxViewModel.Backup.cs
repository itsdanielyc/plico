using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [ObservableProperty] private string? _latestBackupLocation;

    public bool HasLatestBackup => LatestBackupLocation is not null;

    partial void OnLatestBackupLocationChanged(string? value) => OnPropertyChanged(nameof(HasLatestBackup));

    [RelayCommand]
    private void OpenBackupFolder()
    {
        if (LatestBackupLocation is { } location) OpenPath(() => location);
    }

    [RelayCommand]
    private Task BackUpLibraryAsync() => RunOperationAsync("Choose a backup destination…", async token =>
    {
        var library = RequireLibrary();
        string? destination = await _dialogs.PickFolderAsync("Choose where to save the library backup");
        if (destination is null)
        {
            Status = "Backup destination selection closed.";
            return;
        }

        Status = "Backing up the library…";
        var progress = new Progress<BackupProgress>(update =>
        {
            if (IsBusy && !token.IsCancellationRequested) Status = $"Copied {update.Copied} of {update.Total} PDFs…";
        });
        var backup = await Task.Run(() => library.BackupAsync(destination, progress, token), token);
        LatestBackupLocation = backup.Location;
        Status = $"Saved a backup of {DocumentCount(backup.PdfCount)} to {backup.Location}.";
    });
}
