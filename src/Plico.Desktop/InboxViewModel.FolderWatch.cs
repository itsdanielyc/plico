using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private FolderWatch? _folderWatch;
    private readonly WatchFolderPreference? _watchPreference;
    [ObservableProperty] private string? _rememberedWatchFolder;

    public bool IsWatchingFolder => _folderWatch is not null;
    public bool CanStartFolderWatch => CanWork && !IsWatchingFolder;
    public bool CanOfferResumeFolderWatch => RememberedWatchFolder is not null && !IsWatchingFolder;
    public string WatchedFolderPath => _folderWatch?.Folder ?? "";
    public string WatchedFolderName => IsWatchingFolder
        ? new DirectoryInfo(WatchedFolderPath).Name
        : "";

    [RelayCommand]
    private Task StartFolderWatchAsync() => RunOperationAsync("Choose a scanner output folder…", async token =>
    {
        if (IsWatchingFolder) return;
        var folder = await _dialogs.PickFolderAsync("Choose a scanner output folder");
        if (folder is null)
        {
            Status = "Folder selection closed.";
            return;
        }
        token.ThrowIfCancellationRequested();
        StartWatch(folder);
    });

    [RelayCommand]
    private Task ResumeFolderWatchAsync()
    {
        if (RememberedWatchFolder is not { } folder || IsWatchingFolder) return Task.CompletedTask;
        return RunOperationAsync("Resuming folder watch…", _ =>
        {
            StartWatch(folder);
            return Task.CompletedTask;
        });
    }

    [RelayCommand]
    private Task ImportWatchFolderFilesAsync()
    {
        if (_folderWatch is not { } watch) return Task.CompletedTask;
        return RunOperationAsync("Checking the watched folder…", async token =>
        {
            try
            {
                int queued = await Task.Run(() => watch.QueueExistingFiles(token), token);
                if (ReferenceEquals(_folderWatch, watch))
                {
                    Status = queued > 0 ? $"{DocumentCount(queued)} queued for the folder watch." : "No additional files to check.";
                }
            }
            catch (Exception error) when ((error is OperationCanceledException or ObjectDisposedException) &&
                                          !ReferenceEquals(_folderWatch, watch)) { }
        });
    }

    private void StartWatch(string folder)
    {
        var library = RequireLibrary();
        var watch = FolderWatch.Start(folder, library.PdfDirectory, ImportWatchedFilesAsync);
        _folderWatch = watch;
        RememberedWatchFolder = watch.Folder;
        NotifyWatchChanged();
        Status = $"Watching for new or updated files in {WatchedFolderName}.";
        _ = ObserveWatchAsync(watch);
        try
        {
            _watchPreference?.Save(library.RootPath, watch.Folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status += $" {error.Message}";
        }
    }

    private string? RestoreWatchFolder(string libraryRoot)
    {
        RememberedWatchFolder = null;
        try
        {
            RememberedWatchFolder = _watchPreference?.Load(libraryRoot);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
    }

    partial void OnRememberedWatchFolderChanged(string? value) => OnPropertyChanged(nameof(CanOfferResumeFolderWatch));

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
        OnPropertyChanged(nameof(CanOfferResumeFolderWatch));
        OnPropertyChanged(nameof(WatchedFolderPath));
        OnPropertyChanged(nameof(WatchedFolderName));
    }
}
