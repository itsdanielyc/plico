using System.Globalization;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string BackupStagePrefix = ".plico-backup-";
    private const string BackupTimestampFormat = "yyyy-MM-dd HH-mm-ss";
    private const string BackupInventoryChanged = "The PDF folder changed during backup. Try again once the files are saved.";

    /// <summary>Publishes a portable copy of the PDFs and a SQLite snapshot in an existing destination directory.</summary>
    // O(files + copied bytes) time and O(files + one copy buffer) application space.
    public async Task<BackupResult> BackupAsync(string destination, IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string target = ResolveBackupTarget(destination);
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stage = null;
        try
        {
            var inventory = CaptureBackupInventory(cancellationToken);
            var proposedStage = Path.Combine(target, $"{BackupStagePrefix}{Guid.NewGuid():N}");
            if (Path.Exists(proposedStage)) throw new IOException("Could not allocate a new backup folder.");
            stage = Directory.CreateDirectory(proposedStage).FullName;
            var pdfDestination = Directory.CreateDirectory(Path.Combine(stage, PdfFolderName)).FullName;
            var catalogDestination = Directory.CreateDirectory(Path.Combine(stage, MetadataFolderName)).FullName;

            int copied = 0;
            foreach (var entry in inventory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CopyBackupPdfAsync(ResolvePdfPath(entry.Key), Path.Combine(pdfDestination, entry.Key),
                    entry.Value, cancellationToken).ConfigureAwait(false);
                copied++;
                progress?.Report(new BackupProgress(copied, inventory.Count));
            }

            cancellationToken.ThrowIfCancellationRequested();
            _catalog.BackupTo(Path.Combine(catalogDestination, CatalogFilename));
            VerifyBackupInventory(inventory, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new BackupResult(PublishBackup(stage, target), inventory.Count);
        }
        finally
        {
            if (stage is not null) TryRemoveStage(stage);
            _fileOperations.Release();
        }
    }

    private Dictionary<string, FileVersion> CaptureBackupInventory(CancellationToken cancellationToken)
    {
        var inventory = new Dictionary<string, FileVersion>(StringComparer.Ordinal);
        foreach (var path in EnumeratePdfPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            inventory.Add(RelativePdfPath(path), FileVersion.Read(path));
        }
        return inventory;
    }

    private void VerifyBackupInventory(IReadOnlyDictionary<string, FileVersion> expected, CancellationToken cancellationToken)
    {
        int count = 0;
        foreach (var path in EnumeratePdfPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(RelativePdfPath(path), out var version) || FileVersion.Read(path) != version)
            {
                throw new IOException(BackupInventoryChanged);
            }
            count++;
        }
        if (count != expected.Count) throw new IOException(BackupInventoryChanged);
    }

    private static async Task CopyBackupPdfAsync(string source, string destination, FileVersion version,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                         bufferSize: 0, options: SequentialAsync))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         bufferSize: 0, options: SequentialAsync))
        {
            await input.CopyToAsync(output, SnapshotBufferBytes, cancellationToken).ConfigureAwait(false);
            if (output.Length != version.Length) throw new IOException(BackupInventoryChanged);
        }
        File.SetLastWriteTimeUtc(destination, new DateTime(version.LastModifiedTicks, DateTimeKind.Utc));
    }

    private string ResolveBackupTarget(string destination)
    {
        var target = ResolveDirectoryLinks(destination);
        var library = ResolveDirectoryLinks(RootPath);
        var relative = Path.GetRelativePath(library, target);
        if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Choose a backup destination outside the current library.", nameof(destination));
        }
        return target;
    }

    private static string ResolveDirectoryLinks(string path)
    {
        var expanded = new DirectoryInfo(path);
        if (!expanded.Exists) throw new DirectoryNotFoundException($"The folder is unavailable. {expanded.FullName}");
        string root = Path.GetPathRoot(expanded.FullName)!;
        string resolved = root;
        foreach (var part in Path.GetRelativePath(root, expanded.FullName).Split(Path.DirectorySeparatorChar))
        {
            var directory = new DirectoryInfo(Path.Combine(resolved, part));
            resolved = (directory.ResolveLinkTarget(returnFinalTarget: true) ?? directory).FullName;
        }
        return resolved;
    }

    private static string PublishBackup(string stage, string target)
    {
        string timestamp = DateTimeOffset.Now.ToString(BackupTimestampFormat, CultureInfo.InvariantCulture);
        string stem = $"Plico backup {timestamp}";
        for (int sequence = 1; ; sequence++)
        {
            string name = sequence == 1 ? stem : $"{stem} ({sequence.ToString(CultureInfo.InvariantCulture)})";
            string destination = Path.Combine(target, name);
            if (Path.Exists(destination)) continue;
            try
            {
                Directory.Move(stage, destination);
                return destination;
            }
            catch (IOException) when (Path.Exists(destination))
            {
                // Another backup can finish in the same second.
            }
        }
    }
}
