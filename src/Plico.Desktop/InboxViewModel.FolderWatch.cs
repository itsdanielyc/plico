using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private FolderWatch? _folderWatch;
    private readonly WatchFolderPreference? _watchPreference;
    private bool _catchUpOnOpen;
    [ObservableProperty] private string? _rememberedWatchFolder;

    public bool IsWatchingFolder => _folderWatch is not null;
    public bool CanStartFolderWatch => CanWork && !IsWatchingFolder;
    public bool CanOfferResumeFolderWatch => RememberedWatchFolder is not null && !IsWatchingFolder;
    public bool CanOfferImportWatchFiles => IsWatchingFolder || CanOfferResumeFolderWatch;
    public bool CanOfferCatchUpOnOpen => _watchPreference is not null && RememberedWatchFolder is not null;
    public string ImportWatchFilesLabel => IsWatchingFolder ? "Import existing files" : "Resume and catch up";
    public string WatchedFolderPath => _folderWatch?.Folder ?? "";
    public string WatchedFolderName => IsWatchingFolder
        ? new DirectoryInfo(WatchedFolderPath).Name
        : "";

    public bool CatchUpOnOpen
    {
        get => _catchUpOnOpen;
        set
        {
            if (value == _catchUpOnOpen) return;
            if (IsBusy || _library is not { } library || _watchPreference is not { } preference || RememberedWatchFolder is not { } folder)
            {
                OnPropertyChanged(nameof(CatchUpOnOpen));
                return;
            }
            try
            {
                if (value && preference.Load(library.RootPath) != folder) preference.Save(library.RootPath, folder);
                preference.SaveCatchUpOnOpen(library.RootPath, value);
                SetProperty(ref _catchUpOnOpen, value);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Status = error.Message;
                OnPropertyChanged(nameof(CatchUpOnOpen));
            }
        }
    }

    internal Task CatchUpWhenWindowOpensAsync() =>
        CanWork && CatchUpOnOpen ? ResumeRememberedWatchAsync(queueExistingFiles: true) : Task.CompletedTask;

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
        await StartWatchAsync(folder, token);
    });

    [RelayCommand]
    private Task ResumeFolderWatchAsync() => ResumeRememberedWatchAsync(queueExistingFiles: false);

    private Task ResumeRememberedWatchAsync(bool queueExistingFiles)
    {
        if (RememberedWatchFolder is not { } folder || IsWatchingFolder) return Task.CompletedTask;
        return RunOperationAsync("Resuming folder watch…", token => StartWatchAsync(folder, token, queueExistingFiles));
    }

    [RelayCommand]
    private Task ImportWatchFolderFilesAsync()
    {
        if (_folderWatch is not { } watch) return ResumeRememberedWatchAsync(queueExistingFiles: true);
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

    private async Task StartWatchAsync(string folder, CancellationToken cancellationToken, bool queueExistingFiles = false)
    {
        var library = RequireLibrary();
        Status = "Checking the scanner output folder…";
        var watch = await Task.Run(() => FolderWatch.Start(folder, library.PdfDirectory, ImportWatchedFilesAsync, cancellationToken),
            cancellationToken);
        int queued = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queueExistingFiles)
            {
                Status = "Checking existing files…";
                queued = await Task.Run(() => watch.QueueExistingFiles(cancellationToken), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            await watch.DisposeAsync();
            throw;
        }
        _folderWatch = watch;
        RememberedWatchFolder = watch.Folder;
        NotifyWatchChanged();
        Status = $"Watching for new or updated files in {WatchedFolderName}.";
        if (queued > 0) Status += $" {DocumentCount(queued)} queued for the folder watch.";
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
        SetProperty(ref _catchUpOnOpen, false, nameof(CatchUpOnOpen));
        try
        {
            if (_watchPreference is { } preference && preference.Load(libraryRoot) is { } folder)
            {
                RememberedWatchFolder = folder;
                SetProperty(ref _catchUpOnOpen, preference.LoadCatchUpOnOpen(libraryRoot), nameof(CatchUpOnOpen));
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
    }

    partial void OnRememberedWatchFolderChanged(string? value)
    {
        OnPropertyChanged(nameof(CanOfferResumeFolderWatch));
        OnPropertyChanged(nameof(CanOfferImportWatchFiles));
        OnPropertyChanged(nameof(CanOfferCatchUpOnOpen));
    }

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
        OnPropertyChanged(nameof(CanOfferImportWatchFiles));
        OnPropertyChanged(nameof(ImportWatchFilesLabel));
        OnPropertyChanged(nameof(WatchedFolderPath));
        OnPropertyChanged(nameof(WatchedFolderName));
    }
}
