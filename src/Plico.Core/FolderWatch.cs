using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Plico.Core;

/// <summary>Supplies stable new files from an intake folder while the desktop app is open.</summary>
public sealed class FolderWatch : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan WatchRestartCooldown = TimeSpan.FromSeconds(2);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly EnumerationOptions ExistingFileOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false
    };

    private sealed class Observation(FileVersion? sample = null, bool includeKnownVersion = false)
    {
        internal FileVersion? Sample { get; } = sample;
        internal bool IncludeKnownVersion { get; } = includeKnownVersion;
    }

    private readonly FileSystemWatcher _watcher;
    private readonly string _canonicalFolder;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task> _import;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lazy<Task> _disposal;
    private readonly ConcurrentDictionary<string, Observation> _pending = new(PathComparer);
    private readonly Channel<bool> _wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly Dictionary<string, FileVersion> _knownFiles;
    private Exception? _watchError;
    private long? _lastRestart;

    private FolderWatch(string folder, string canonicalFolder, Func<IReadOnlyList<string>, CancellationToken, Task> import,
        Dictionary<string, FileVersion> baseline, FileSystemWatcher watcher)
    {
        Folder = folder;
        _canonicalFolder = canonicalFolder;
        _import = import;
        _knownFiles = baseline;
        _disposal = new Lazy<Task>(DisposeOnceAsync);
        _watcher = watcher;
        try
        {
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher.Dispose();
            _stop.Dispose();
            throw;
        }
        Completion = Task.Run(() => WatchAsync(_stop.Token));
    }

    /// <summary>The directory being watched on this computer.</summary>
    public string Folder { get; }

    /// <summary>Completes on stop, or reports loss of the filesystem watch.</summary>
    public Task Completion { get; }

    /// <summary>Starts a nonrecursive watch. Existing files are picked up when a new write or rename occurs.</summary>
    public static FolderWatch Start(
        string folder, string pdfDestination, Func<IReadOnlyList<string>, CancellationToken, Task> import) =>
        Start(folder, pdfDestination, import, CancellationToken.None);

    /// <summary>Starts a watch after taking a cancellable inventory of existing scanner output.</summary>
    public static FolderWatch Start(string folder, string pdfDestination,
        Func<IReadOnlyList<string>, CancellationToken, Task> import, CancellationToken cancellationToken) =>
        Start(folder, pdfDestination, import, static path => new FileSystemWatcher(path), cancellationToken);

    internal static FolderWatch Start(string folder, string pdfDestination,
        Func<IReadOnlyList<string>, CancellationToken, Task> import, Func<string, FileSystemWatcher> createWatcher,
        CancellationToken cancellationToken = default, string? metadataDirectory = null, FolderWatch? otherWatch = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        cancellationToken.ThrowIfCancellationRequested();
        string source = ResolveDirectory(folder);
        string canonicalSource = ResolveDirectoryAncestors(new DirectoryInfo(source));
        if (IsSameOrInside(canonicalSource, pdfDestination) ||
            (metadataDirectory is not null && IsSameOrInside(canonicalSource, metadataDirectory)))
        {
            throw new ArgumentException("Choose a scanner output or phone folder outside the library's managed folders.", nameof(folder));
        }
        if (otherWatch is not null && Path.GetRelativePath(otherWatch._canonicalFolder, canonicalSource) == ".")
        {
            throw new ArgumentException("This folder is already being watched. Choose a separate intake folder.", nameof(folder));
        }
        var baseline = ReadFolderVersions(source, cancellationToken);
        return new FolderWatch(source, canonicalSource, import, baseline, createWatcher(source));
    }

    // O(n) time to scan n directory entries and O(k) space for the k supported paths.
    /// <summary>Queues supported nonempty files already in the watched directory for stability checking.</summary>
    /// <returns>The number of files newly added to the watch's queue.</returns>
    public int QueueExistingFiles(CancellationToken cancellationToken = default)
    {
        using var cancelWithWatch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var token = cancelWithWatch.Token;
        token.ThrowIfCancellationRequested();
        var candidates = EnumerateScanFiles(Folder, token)
            .Where(file => ReadVersion(file) is { Length: > 0 })
            .Select(file => file.FullName)
            .ToList();

        // Gather the candidates before changing the live queue so cancellation leaves the request unqueued.
        token.ThrowIfCancellationRequested();
        int queued = 0;
        foreach (string file in candidates)
        {
            var requested = new Observation(includeKnownVersion: true);
            var observation = _pending.AddOrUpdate(file, requested, static (_, pending) =>
                pending.IncludeKnownVersion ? pending : new Observation(pending.Sample, includeKnownVersion: true));
            if (ReferenceEquals(observation, requested)) queued++;
        }
        if (queued > 0) _wakeups.Writer.TryWrite(true);
        return queued;
    }

    /// <summary>Stops intake and waits for the active watch operation to unwind.</summary>
    public ValueTask DisposeAsync() => new(_disposal.Value);

    private async Task DisposeOnceAsync()
    {
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await Completion.ConfigureAwait(false);
        }
        finally
        {
            _watcher.Dispose();
            _stop.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs change) => ObservePath(change.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs change)
    {
        if (!PathComparer.Equals(change.OldFullPath, change.FullPath)) ObservePath(change.OldFullPath);
        ObservePath(change.FullPath);
    }

    private void ObservePath(string path)
    {
        if (_stop.IsCancellationRequested || !InboxLibrary.SupportsImport(path)) return;
        _pending.AddOrUpdate(path, static _ => new Observation(), static (_, pending) =>
            new Observation(includeKnownVersion: pending.IncludeKnownVersion));
        _wakeups.Writer.TryWrite(true);
    }

    private void OnError(object sender, ErrorEventArgs error)
    {
        Volatile.Write(ref _watchError, error.GetException());
        _wakeups.Writer.TryWrite(true);
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Files can arrive between the initial inventory and the first live notification.
            ReconcileFolder(cancellationToken);
            if (!_pending.IsEmpty) _wakeups.Writer.TryWrite(true);
            while (await _wakeups.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _wakeups.Reader.TryRead(out _);
                RecoverAfterNotificationLoss(cancellationToken);
                while (!_pending.IsEmpty)
                {
                    await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
                    RecoverAfterNotificationLoss(cancellationToken);
                    var ready = TakeStableFiles();
                    if (ready.Count == 0) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    await _import(ready, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    // O(pending) time and up to O(ready) space. A later notification invalidates the first sample atomically.
    private List<string> TakeStableFiles()
    {
        var ready = new List<string>();
        foreach (var entry in _pending)
        {
            if (ReadVersion(new FileInfo(entry.Key)) is not { Length: > 0 } current)
            {
                if (_pending.TryRemove(entry)) _knownFiles.Remove(entry.Key);
                continue;
            }
            if (entry.Value.Sample != current)
            {
                _pending.TryUpdate(entry.Key, new Observation(current, entry.Value.IncludeKnownVersion), entry.Value);
                continue;
            }
            if (!_pending.TryRemove(entry)) continue;
            if (!entry.Value.IncludeKnownVersion && _knownFiles.TryGetValue(entry.Key, out var known) && current == known)
            {
                continue;
            }
            _knownFiles[entry.Key] = current;
            ready.Add(entry.Key);
        }
        return ready;
    }

    private void RecoverAfterNotificationLoss(CancellationToken cancellationToken)
    {
        var interruption = Interlocked.Exchange(ref _watchError, null);
        if (interruption is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (interruption is not InternalBufferOverflowException || !_watcher.EnableRaisingEvents)
            {
                RestartWatcher(interruption);
            }
            ReconcileFolder(cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new IOException($"The folder watch could not recover {Folder}. {error.Message}", error);
        }
    }

    private void RestartWatcher(Exception interruption)
    {
        long now = Stopwatch.GetTimestamp();
        // A backend may report an unsuccessful restart on its notification thread.
        if (_lastRestart is { } previous && Stopwatch.GetElapsedTime(previous, now) < WatchRestartCooldown)
        {
            throw new IOException("File notifications stopped again after the watch restarted.", interruption);
        }
        _lastRestart = now;
        _watcher.EnableRaisingEvents = false;
        _watcher.EnableRaisingEvents = true;
    }

    // O(n + k) time for n current entries and k known paths, with O(n) temporary file stamps.
    private void ReconcileFolder(CancellationToken cancellationToken)
    {
        var current = ReadFolderVersions(Folder, cancellationToken);
        foreach (var (path, version) in current)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_knownFiles.TryGetValue(path, out var known) || version != known) _pending.TryAdd(path, new Observation());
        }
        // Keep the last delivered version until a changed file settles. Forget removals so reused names start fresh.
        foreach (string path in _knownFiles.Keys)
        {
            if (!current.ContainsKey(path)) _knownFiles.Remove(path);
        }
    }

    private static Dictionary<string, FileVersion> ReadFolderVersions(string folder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var versions = new Dictionary<string, FileVersion>(PathComparer);
        foreach (var file in EnumerateScanFiles(folder, cancellationToken))
        {
            if (ReadVersion(file) is { Length: > 0 } version) versions[file.FullName] = version;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return versions;
    }

    private static IEnumerable<FileInfo> EnumerateScanFiles(string folder, CancellationToken cancellationToken)
    {
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", ExistingFileOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (InboxLibrary.SupportsImport(file.Name)) yield return file;
        }
    }

    private static FileVersion? ReadVersion(FileInfo file) => file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0
        ? new FileVersion(file.LastWriteTimeUtc.Ticks, file.Length, Present: true)
        : null;

    private static string ResolveDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) throw new DirectoryNotFoundException($"The folder is unavailable. {directory.FullName}");
        return (directory.ResolveLinkTarget(returnFinalTarget: true) ?? directory).FullName;
    }

    private static bool IsSameOrInside(string canonicalPath, string root)
    {
        string canonicalRoot = ResolveDirectoryAncestors(new DirectoryInfo(ResolveDirectory(root)));
        string relative = Path.GetRelativePath(canonicalRoot, canonicalPath);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ResolveDirectoryAncestors(DirectoryInfo directory)
    {
        if (directory.Parent is not { } parent) return directory.FullName;
        // A selected path can pass through a directory link even when its leaf is an ordinary folder.
        var entry = new DirectoryInfo(Path.Combine(ResolveDirectoryAncestors(parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true) is DirectoryInfo target
            ? ResolveDirectoryAncestors(target)
            : entry.FullName;
    }
}
