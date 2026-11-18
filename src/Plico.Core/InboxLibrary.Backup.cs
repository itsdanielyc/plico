using System.Globalization;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string BackupStagePrefix = ".plico-backup-";
    private const string BackupFolderLabel = "Plico backup";
    private const string CopyTimestampFormat = "yyyy-MM-dd HH-mm-ss";
    private const string BackupInventoryChanged = "The library files changed during backup. Try again once the files are saved.";

    /// <summary>Publishes a portable copy of the PDFs and a SQLite snapshot in an existing destination directory.</summary>
    // O(files + copied bytes) time and O(files + one copy buffer) application space.
    public async Task<BackupResult> BackupAsync(string destination, IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string target = ResolveExternalTarget(destination);
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stage = null;
        try
        {
            RecoverPdfRevision(cancellationToken);
            var recovery = RecoverPendingPdfMove(cancellationToken);
            if (recovery.PendingDestination is not null) throw new IOException(recovery.Notice);
            var inventory = CaptureBackupInventory(cancellationToken);
            var trash = _catalog.ReadTrashFiles();
            var history = _catalog.ReadPdfVersions();
            var trashVersions = new List<(string Path, FileVersion Version)>(trash.Count);
            int total = checked(inventory.Keys.Count(IsBackupPdf) + trash.Count + history.Count);
            stage = CreateCopyStage(target, BackupStagePrefix);
            Directory.CreateDirectory(Path.Combine(stage, PdfFolderName));
            var catalogDestination = Directory.CreateDirectory(Path.Combine(stage, MetadataFolderName)).FullName;

            int copied = 0;
            foreach (var entry in inventory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CopyFileSnapshotAsync(Path.Combine(RootPath, entry.Key), Path.Combine(stage, entry.Key),
                    entry.Value, BackupInventoryChanged, cancellationToken).ConfigureAwait(false);
                if (IsBackupPdf(entry.Key))
                {
                    copied++;
                    progress?.Report(new BackupProgress(copied, total));
                }
            }
            foreach (var item in trash)
            {
                var source = VerifyTrashFile(item, cancellationToken);
                await CopyFileSnapshotAsync(source.Path, Path.Combine(catalogDestination, TrashFolderName, TrashFilename(item.DocumentId)),
                    source.Version, BackupInventoryChanged, cancellationToken).ConfigureAwait(false);
                trashVersions.Add((source.Path, source.Version));
                progress?.Report(new BackupProgress(++copied, total));
            }
            foreach (var file in history)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string folder = Directory.CreateDirectory(Path.Combine(catalogDestination, PdfVersionsFolderName,
                    PdfVersionDocumentFolder(file.DocumentId))).FullName;
                await CopyHistoryFileAsync(file, Path.Combine(folder, PdfVersionFilename(file.Version.Key)), cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(new BackupProgress(++copied, total));
            }

            cancellationToken.ThrowIfCancellationRequested();
            _catalog.BackupTo(Path.Combine(catalogDestination, CatalogFilename));
            VerifyBackupInventory(inventory, cancellationToken);
            foreach (var (path, version) in trashVersions) EnsureCurrentFile(path, version);
            cancellationToken.ThrowIfCancellationRequested();
            return new BackupResult(PublishCopyFolder(stage, target, BackupFolderLabel, cancellationToken), total);
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
        foreach (var path in EnumerateBackupFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            inventory.Add(Path.GetRelativePath(RootPath, path), FileVersion.Read(path));
        }
        return inventory;
    }

    private void VerifyBackupInventory(IReadOnlyDictionary<string, FileVersion> expected, CancellationToken cancellationToken)
    {
        int count = 0;
        foreach (var path in EnumerateBackupFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(Path.GetRelativePath(RootPath, path), out var version) || FileVersion.Read(path) != version)
            {
                throw new IOException(BackupInventoryChanged);
            }
            count++;
        }
        if (count != expected.Count) throw new IOException(BackupInventoryChanged);
    }

    private static bool IsBackupPdf(string path) => path.EndsWith(DocumentProcessor.PdfExtension, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> EnumerateBackupFiles()
    {
        foreach (string pdf in EnumeratePdfPaths()) yield return pdf;
        string pending = Path.Combine(_metadataDirectory, PendingDuplexFolder);
        if (ReadExistingAttributes(pending) is not { } directoryAttributes) yield break;
        RequireRestoreDirectory(directoryAttributes);
        foreach (string filename in new[] { DuplexSettingsFile, FrontSidesFile, BackSidesFile })
        {
            string path = Path.Combine(pending, filename);
            var attributes = ReadExistingAttributes(path);
            if (attributes is null && filename == BackSidesFile) continue;
            if (attributes is not { } value || (value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException($"The saved manual duplex file is unavailable. {path}");
            yield return path;
        }
    }

    private static async Task CopyFileSnapshotAsync(string source, string destination, FileVersion version,
        string changedMessage, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                         bufferSize: 0, options: SequentialAsync))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         bufferSize: 0, options: SequentialAsync))
        {
            await input.CopyToAsync(output, SnapshotBufferBytes, cancellationToken).ConfigureAwait(false);
            if (output.Length != version.Length) throw new IOException(changedMessage);
        }
        File.SetLastWriteTimeUtc(destination, new DateTime(version.LastModifiedTicks, DateTimeKind.Utc));
    }

    private string ResolveExternalTarget(string destination)
    {
        var target = ResolveDirectoryLinks(destination);
        var library = ResolveDirectoryLinks(RootPath);
        if (IsAtOrWithinFolder(library, target))
        {
            throw new ArgumentException("Choose a destination outside the current library.", nameof(destination));
        }
        return target;
    }

    private static bool IsAtOrWithinFolder(string folder, string path)
    {
        string relative = Path.GetRelativePath(folder, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
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

    private static string CreateCopyStage(string destination, string prefix)
    {
        string stage = Path.Combine(destination, $"{prefix}{Guid.NewGuid():N}");
        if (Path.Exists(stage)) throw new IOException("Could not allocate a new staging folder.");
        return Directory.CreateDirectory(stage).FullName;
    }

    private static string PublishCopyFolder(string stage, string target, string label, CancellationToken cancellationToken)
    {
        string timestamp = DateTimeOffset.Now.ToString(CopyTimestampFormat, CultureInfo.InvariantCulture);
        string stem = $"{label} {timestamp}";
        for (int sequence = 1; ; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = NumberedName(stem, sequence);
            string destination = Path.Combine(target, name);
            if (Path.Exists(destination)) continue;
            try
            {
                Directory.Move(stage, destination);
                return destination;
            }
            catch (IOException) when (Path.Exists(destination))
            {
                // Another copy can finish in the same second.
            }
        }
    }

    private static string NumberedName(string stem, int sequence) =>
        sequence == 1 ? stem : $"{stem} ({sequence.ToString(CultureInfo.InvariantCulture)})";
}
