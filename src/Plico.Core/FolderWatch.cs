using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Plico.Core;

/// <summary>Supplies stable new files from a scanner's output folder while the desktop app is open.</summary>
public sealed class FolderWatch : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(650);

    private sealed class Observation(FileVersion? sample = null)
    {
        internal FileVersion? Sample { get; } = sample;
    }

    private readonly FileSystemWatcher _watcher;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task> _import;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lazy<Task> _disposal;
    private readonly ConcurrentDictionary<string, Observation> _pending = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Channel<bool> _wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private Exception? _watchError;

    private FolderWatch(string folder, Func<IReadOnlyList<string>, CancellationToken, Task> import)
    {
        Folder = folder;
        _import = import;
        _disposal = new Lazy<Task>(DisposeOnceAsync);
        _watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Error += OnError;
        try
        {
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher.Dispose();
            _stop.Dispose();
            throw;
        }
        Completion = WatchAsync(_stop.Token);
    }

    /// <summary>The directory being watched on this computer.</summary>
    public string Folder { get; }

    /// <summary>Completes on stop, or reports loss of the filesystem watch.</summary>
    public Task Completion { get; }

    /// <summary>Starts a nonrecursive watch. Existing files are picked up when a new write or rename occurs.</summary>
    public static FolderWatch Start(
        string folder, string pdfDestination, Func<IReadOnlyList<string>, CancellationToken, Task> import)
    {
        ArgumentNullException.ThrowIfNull(import);
        string source = ResolveDirectory(folder);
        if (Path.GetRelativePath(ResolveDirectory(pdfDestination), source) == ".")
        {
            throw new ArgumentException("Choose your scanner's output folder. This folder already holds the library PDFs.", nameof(folder));
        }
        return new FolderWatch(source, import);
    }

    /// <summary>Stops new notifications and waits for in-flight intake to unwind.</summary>
    public ValueTask DisposeAsync() => new(_disposal.Value);

    private async Task DisposeOnceAsync()
    {
        _watcher.Dispose();
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await Completion.ConfigureAwait(false);
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs change)
    {
        if (!InboxLibrary.SupportsImport(change.FullPath)) return;
        _pending[change.FullPath] = new Observation();
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
            while (await _wakeups.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _wakeups.Reader.TryRead(out _);
                ThrowOnWatchError();
                while (!_pending.IsEmpty)
                {
                    await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
                    ThrowOnWatchError();
                    var ready = TakeStableFiles();
                    if (ready.Count > 0) await _import(ready, cancellationToken).ConfigureAwait(false);
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
            var file = new FileInfo(entry.Key);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length == 0)
            {
                _pending.TryRemove(entry);
                continue;
            }
            var current = new FileVersion(file.LastWriteTimeUtc.Ticks, file.Length, Present: true);
            if (entry.Value.Sample != current)
            {
                _pending.TryUpdate(entry.Key, new Observation(current), entry.Value);
                continue;
            }
            if (_pending.TryRemove(entry)) ready.Add(entry.Key);
        }
        return ready;
    }

    private void ThrowOnWatchError()
    {
        if (Volatile.Read(ref _watchError) is { } error)
        {
            throw new IOException($"The folder watch lost access to {Folder}. {error.Message}", error);
        }
    }

    private static string ResolveDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) throw new DirectoryNotFoundException($"The folder is unavailable. {directory.FullName}");
        return (directory.ResolveLinkTarget(returnFinalTarget: true) ?? directory).FullName;
    }
}
