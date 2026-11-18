using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private sealed class WatchSlot(string description, string stopMessage, WatchSource source)
    {
        internal string Description { get; } = description;
        internal string StopMessage { get; } = stopMessage;
        internal WatchSource Source { get; } = source;
        internal FolderWatch? Active { get; set; }
        internal string? RememberedFolder { get; set; }
        internal bool CatchUpOnOpen { get; set; }
        internal bool CanResume => Active is null && RememberedFolder is not null;
        internal bool CanCatchUp => Active is not null || CanResume;
        internal string CatchUpLabel => Active is null ? DesktopText.Translate("Resume and catch up") : DesktopText.Translate("Import existing files");
    }

    private readonly WatchSlot _scannerWatch = new("scanner output folder", "Folder watch stopped.", WatchSource.Scanner);
    private readonly WatchSlot _phoneWatch = new("phone inbox folder", "Phone inbox watch stopped.", WatchSource.Phone);
    private readonly WatchFolderPreference? _watchPreference;

    public bool IsWatchingFolder => _scannerWatch.Active is not null;
    public bool CanStartFolderWatch => CanWork && !IsWatchingFolder;
    public bool CanOfferResumeFolderWatch => _scannerWatch.CanResume;
    public bool CanOfferImportWatchFiles => _scannerWatch.CanCatchUp;
    public bool CanOfferCatchUpOnOpen => _watchPreference is not null && RememberedWatchFolder is not null;
    public string ImportWatchFilesLabel => _scannerWatch.CatchUpLabel;
    public string? RememberedWatchFolder => _scannerWatch.RememberedFolder;
    public string WatchedFolderPath => _scannerWatch.Active?.Folder ?? "";
    public string WatchedFolderName => _scannerWatch.Active is { } watch ? new DirectoryInfo(watch.Folder).Name : "";

    public bool CatchUpOnOpen
    {
        get => _scannerWatch.CatchUpOnOpen;
        set => SetCatchUpOnOpen(_scannerWatch, value);
    }

    public bool IsWatchingPhoneInbox => _phoneWatch.Active is not null;
    public bool CanResumePhoneInbox => _phoneWatch.CanResume;
    public bool CanCatchUpPhoneInbox => _phoneWatch.CanCatchUp;
    public bool CanSetPhoneCatchUpOnOpen => _watchPreference is not null && _phoneWatch.RememberedFolder is not null;
    public string PhoneCatchUpLabel => _phoneWatch.CatchUpLabel;
    public string? PhoneInboxFolder => _phoneWatch.RememberedFolder;
    public string PhoneInboxLabel => DesktopText.Translate(IsWatchingPhoneInbox ? "Phone inbox active…" : "Phone inbox…");

    public bool PhoneCatchUpOnOpen
    {
        get => _phoneWatch.CatchUpOnOpen;
        set => SetCatchUpOnOpen(_phoneWatch, value);
    }

    private bool HasActiveWatches => IsWatchingFolder || IsWatchingPhoneInbox;

    internal Task CatchUpWhenWindowOpensAsync() =>
        CanWork && (CatchUpOnOpen || PhoneCatchUpOnOpen)
            ? RunOperationAsync(DesktopText.Translate("Resuming saved watches…"), CatchUpSavedWatchesAsync)
            : Task.CompletedTask;

    [RelayCommand] private Task StartFolderWatchAsync() => ChooseWatchFolderAsync(_scannerWatch);
    [RelayCommand] private Task ResumeFolderWatchAsync() => ResumeWatchAsync(_scannerWatch, queueExistingFiles: false);
    [RelayCommand] private Task ImportWatchFolderFilesAsync() => CatchUpWatchAsync(_scannerWatch);
    [RelayCommand] private Task StopFolderWatchAsync() => StopWatchAsync(_scannerWatch);

    [RelayCommand] private Task ChoosePhoneInboxFolderAsync() => ChooseWatchFolderAsync(_phoneWatch);
    [RelayCommand] private Task ResumePhoneInboxAsync() => ResumeWatchAsync(_phoneWatch, queueExistingFiles: false);
    [RelayCommand] private Task CatchUpPhoneInboxAsync() => CatchUpWatchAsync(_phoneWatch);
    [RelayCommand] private Task StopPhoneInboxAsync() => StopWatchAsync(_phoneWatch);

    private void SetCatchUpOnOpen(WatchSlot slot, bool enabled)
    {
        if (slot.CatchUpOnOpen == enabled) return;
        try
        {
            if (!CanWork || _library is not { } library || _watchPreference is not { } preference ||
                slot.RememberedFolder is not { } folder) return;
            if (enabled && preference.Load(library.RootPath, slot.Source) != folder)
            {
                preference.Save(library.RootPath, folder, slot.Source);
            }
            preference.SaveCatchUpOnOpen(library.RootPath, enabled, slot.Source);
            slot.CatchUpOnOpen = enabled;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status = error.Message;
        }
        finally
        {
            OnPropertyChanged(slot.Source == WatchSource.Scanner ? nameof(CatchUpOnOpen) : nameof(PhoneCatchUpOnOpen));
        }
    }

    private Task ChooseWatchFolderAsync(WatchSlot slot) => RunOperationAsync(DesktopText.Format($"Choose a {DesktopText.Translate(slot.Description)}…"), async token =>
    {
        if (slot.Active is not null) return;
        var folder = await _dialogs.PickFolderAsync(DesktopText.Format($"Choose a {DesktopText.Translate(slot.Description)}"));
        if (folder is null)
        {
            Status = DesktopText.Translate("Folder selection closed.");
            return;
        }
        token.ThrowIfCancellationRequested();
        await StartWatchAsync(slot, folder, token);
    });

    private Task ResumeWatchAsync(WatchSlot slot, bool queueExistingFiles)
    {
        if (!slot.CanResume || slot.RememberedFolder is not { } folder) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Format($"Resuming the {DesktopText.Translate(slot.Description)} watch…"),
            token => StartWatchAsync(slot, folder, token, queueExistingFiles));
    }

    private Task CatchUpWatchAsync(WatchSlot slot)
    {
        if (slot.Active is not { } watch) return ResumeWatchAsync(slot, queueExistingFiles: true);
        return RunOperationAsync(DesktopText.Format($"Checking the {DesktopText.Translate(slot.Description)}…"), async token =>
        {
            try
            {
                int queued = await Task.Run(() => watch.QueueExistingFiles(token), token);
                if (ReferenceEquals(slot.Active, watch))
                {
                    Status = queued > 0 ? DesktopText.Format($"{DocumentCount(queued)} queued for the folder watch.") : DesktopText.Translate("No additional files to check.");
                }
            }
            catch (Exception error) when ((error is OperationCanceledException or ObjectDisposedException) &&
                                          !ReferenceEquals(slot.Active, watch)) { }
        });
    }

    private async Task StartWatchAsync(WatchSlot slot, string folder, CancellationToken cancellationToken, bool queueExistingFiles = false)
    {
        var library = RequireLibrary();
        var otherWatch = ReferenceEquals(slot, _scannerWatch) ? _phoneWatch.Active : _scannerWatch.Active;
        Status = DesktopText.Format($"Checking the {DesktopText.Translate(slot.Description)}…");
        var watch = await Task.Run(() => library.StartIntakeWatch(folder, ImportWatchedFilesAsync, otherWatch, cancellationToken),
            cancellationToken);
        int queued = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queueExistingFiles)
            {
                Status = DesktopText.Translate("Checking existing files…");
                queued = await Task.Run(() => watch.QueueExistingFiles(cancellationToken), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            await watch.DisposeAsync();
            throw;
        }
        slot.Active = watch;
        slot.RememberedFolder = watch.Folder;
        NotifyWatchChanged(slot);
        Status = DesktopText.Format($"Watching for new or updated files in {new DirectoryInfo(watch.Folder).Name}.");
        if (queued > 0) Status += " " + DesktopText.Format($"{DocumentCount(queued)} queued for the folder watch.");
        _ = ObserveWatchAsync(slot, watch);
        try
        {
            _watchPreference?.Save(library.RootPath, watch.Folder, slot.Source);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status += $" {error.Message}";
        }
    }

    private async Task CatchUpSavedWatchesAsync(CancellationToken cancellationToken)
    {
        var scannerNotice = await TryStartSavedWatchAsync(_scannerWatch, cancellationToken);
        var phoneNotice = await TryStartSavedWatchAsync(_phoneWatch, cancellationToken);
        if (JoinWatchNotices(scannerNotice, phoneNotice) is { } notice) Status = notice;
    }

    private async Task<string?> TryStartSavedWatchAsync(WatchSlot slot, CancellationToken cancellationToken)
    {
        if (!slot.CatchUpOnOpen || !slot.CanResume || slot.RememberedFolder is not { } folder) return null;
        try
        {
            await StartWatchAsync(slot, folder, cancellationToken, queueExistingFiles: true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DesktopText.Format($"Could not resume the {DesktopText.Translate(slot.Description)} watch. {error.Message}");
        }
    }

    private string? RestoreWatchFolders(string libraryRoot) =>
        JoinWatchNotices(RestoreWatchFolder(_scannerWatch, libraryRoot), RestoreWatchFolder(_phoneWatch, libraryRoot));

    private string? RestoreWatchFolder(WatchSlot slot, string libraryRoot)
    {
        slot.RememberedFolder = null;
        slot.CatchUpOnOpen = false;
        try
        {
            if (_watchPreference is { } preference && preference.Load(libraryRoot, slot.Source) is { } folder)
            {
                slot.RememberedFolder = folder;
                slot.CatchUpOnOpen = preference.LoadCatchUpOnOpen(libraryRoot, slot.Source);
            }
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
        finally
        {
            NotifyWatchChanged(slot);
        }
    }

    private static string? JoinWatchNotices(string? scannerNotice, string? phoneNotice) =>
        scannerNotice is null ? phoneNotice : phoneNotice is null ? scannerNotice : $"{scannerNotice} {phoneNotice}";

    private Task StopAllWatchesAsync() => Task.WhenAll(StopWatchAsync(_scannerWatch), StopWatchAsync(_phoneWatch));

    private async Task StopWatchAsync(WatchSlot slot)
    {
        if (slot.Active is not { } watch) return;
        slot.Active = null;
        NotifyWatchChanged(slot);
        try
        {
            await watch.DisposeAsync();
            if (slot.Active is null) Status = DesktopText.Translate(slot.StopMessage);
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
            await RunOperationAsync(DesktopText.Translate("Importing from the watched folder…"),
                token => ImportFilesAsync(files, token, ImportOrigin.FolderWatch, revealAdded: false), cancellationToken);
        });

    private async Task ObserveWatchAsync(WatchSlot slot, FolderWatch watch)
    {
        try
        {
            await watch.Completion;
        }
        catch (Exception)
        {
            if (ReferenceEquals(slot.Active, watch)) await StopWatchAsync(slot);
        }
    }

    private void NotifyWatchChanged(WatchSlot slot)
    {
        if (slot.Source == WatchSource.Scanner)
        {
            OnPropertyChanged(nameof(IsWatchingFolder));
            OnPropertyChanged(nameof(CanStartFolderWatch));
            OnPropertyChanged(nameof(CanOfferResumeFolderWatch));
            OnPropertyChanged(nameof(CanOfferImportWatchFiles));
            OnPropertyChanged(nameof(CanOfferCatchUpOnOpen));
            OnPropertyChanged(nameof(CatchUpOnOpen));
            OnPropertyChanged(nameof(ImportWatchFilesLabel));
            OnPropertyChanged(nameof(RememberedWatchFolder));
            OnPropertyChanged(nameof(WatchedFolderPath));
            OnPropertyChanged(nameof(WatchedFolderName));
            return;
        }
        OnPropertyChanged(nameof(IsWatchingPhoneInbox));
        OnPropertyChanged(nameof(CanResumePhoneInbox));
        OnPropertyChanged(nameof(CanCatchUpPhoneInbox));
        OnPropertyChanged(nameof(CanSetPhoneCatchUpOnOpen));
        OnPropertyChanged(nameof(PhoneCatchUpOnOpen));
        OnPropertyChanged(nameof(PhoneCatchUpLabel));
        OnPropertyChanged(nameof(PhoneInboxFolder));
        OnPropertyChanged(nameof(PhoneInboxLabel));
    }
}
