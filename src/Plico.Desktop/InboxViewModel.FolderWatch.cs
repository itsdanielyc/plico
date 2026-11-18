using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private FolderWatch? _folderWatch;

    public bool IsWatchingFolder => _folderWatch is not null;
    public bool CanStartFolderWatch => CanWork && !IsWatchingFolder;
    public string WatchedFolderPath => _folderWatch?.Folder ?? "";
    public string WatchedFolderName => IsWatchingFolder
        ? new DirectoryInfo(WatchedFolderPath).Name
        : "";

    [RelayCommand]
    private Task StartFolderWatchAsync() => RunOperationAsync("Choose a scanner output folder…", async token =>
    {
        if (IsWatchingFolder) return;
        var library = RequireLibrary();
        var folder = await _dialogs.PickFolderAsync("Choose a scanner output folder");
        if (folder is null)
        {
            Status = "Folder selection closed.";
            return;
        }
        token.ThrowIfCancellationRequested();
        var watch = FolderWatch.Start(folder, library.PdfDirectory, ImportWatchedFilesAsync);
        _folderWatch = watch;
        NotifyWatchChanged();
        Status = $"Watching for new or updated files in {WatchedFolderName}.";
        _ = ObserveWatchAsync(watch);
    });

    [RelayCommand]
    private async Task StopFolderWatchAsync()
    {
        if (_folderWatch is not { } watch) return;
        _folderWatch = null;
        NotifyWatchChanged();
        try
        {
            await watch.DisposeAsync();
            if (_folderWatch is null) Status = "Folder watch stopped.";
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    private Task ImportWatchedFilesAsync(IReadOnlyList<string> files, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            while (_operationCompletion is { } active)
            {
                await active.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await RunOperationAsync("Importing from the watched folder…",
                token => ImportFilesAsync(files, token, revealAdded: false), cancellationToken);
        });

    private async Task ObserveWatchAsync(FolderWatch watch)
    {
        try
        {
            await watch.Completion;
        }
        catch (Exception)
        {
            if (ReferenceEquals(_folderWatch, watch)) await StopFolderWatchAsync();
        }
    }

    private void NotifyWatchChanged()
    {
        OnPropertyChanged(nameof(IsWatchingFolder));
        OnPropertyChanged(nameof(CanStartFolderWatch));
        OnPropertyChanged(nameof(WatchedFolderPath));
        OnPropertyChanged(nameof(WatchedFolderName));
    }
}
