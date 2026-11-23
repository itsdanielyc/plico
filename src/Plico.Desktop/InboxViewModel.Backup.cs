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
    private Task BackUpLibraryAsync() => RunOperationAsync(DesktopText.Translate("Choose a backup destination…"), async token =>
    {
        var library = RequireLibrary();
        string? destination = await _dialogs.PickFolderAsync("Choose where to save the library backup");
        if (destination is null)
        {
            Status = DesktopText.Translate("Backup destination selection closed.");
            return;
        }

        Status = DesktopText.Translate("Backing up the library…");
        var progress = new Progress<BackupProgress>(update =>
        {
            if (!IsBusy || token.IsCancellationRequested) return;
            ReportProgress(update.Copied, update.Total);
            Status = DesktopText.Format($"Copied {update.Copied} of {update.Total} PDFs…");
        });
        var backup = await Task.Run(() => library.BackupAsync(destination, progress, token), token);
        LatestBackupLocation = backup.Location;
        Announce(DesktopText.Format($"Saved a backup of {DocumentCount(backup.PdfCount)} to {backup.Location}."), DesktopText.Translate("Backup saved"),
            $"{DocumentCount(backup.PdfCount)} · {FriendlyPath(Path.GetDirectoryName(backup.Location) ?? backup.Location)}");
    });
}
