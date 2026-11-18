using System.Security.Cryptography;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private sealed record ChangedPdf(string Path, string RelativePath, FileVersion Version,
        byte[] Fingerprint, IndexedPdf? Previous);

    private sealed record DiskInventory(HashSet<string> Observed, HashSet<string> Unreadable, List<ChangedPdf> Changes);

    // Expected O(files + rows + examined bytes) time and O(files + rows + largest parsed document) space.
    private RefreshResult ReconcileFiles(CancellationToken cancellationToken)
    {
        var previous = _catalog.ReadIndexedFiles();
        var warnings = new List<string>();
        var inventory = DiscoverChanges(previous, warnings, cancellationToken);
        var moves = MatchUniqueMoves(previous.Values, inventory);
        int indexed = 0;
        int relocated = 0;
        foreach (var file in inventory.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureCurrentFile(file.Path, file.Version);
                if (moves.TryGetValue(file.RelativePath, out var original))
                {
                    _catalog.Relocate(original.Id, file.RelativePath, Path.GetFileNameWithoutExtension(file.Path), file.Version);
                    relocated++;
                }
                else if (IndexIfChanged(file, cancellationToken))
                {
                    indexed++;
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                inventory.Unreadable.Add(file.RelativePath);
                warnings.Add($"{file.RelativePath} — {error.Message}");
            }
        }

        _catalog.MarkMissing(previous.Values
            .Where(file => file.Version.Present &&
                (!inventory.Observed.Contains(file.RelativePath) || inventory.Unreadable.Contains(file.RelativePath)))
            .Select(file => file.RelativePath));
        return new(indexed, warnings, relocated);
    }

    private DiskInventory DiscoverChanges(IReadOnlyDictionary<string, IndexedPdf> previous, List<string> warnings,
        CancellationToken cancellationToken)
    {
        var inventory = new DiskInventory(new(StringComparer.Ordinal), new(StringComparer.Ordinal), []);
        foreach (var path in EnumeratePdfPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = RelativePdfPath(path);
            inventory.Observed.Add(relativePath);
            var old = previous.GetValueOrDefault(relativePath);
            try
            {
                var version = FileVersion.Read(path);
                if (old is { Fingerprint: not null } && old.Version == version) continue;
                var fingerprint = FingerprintPdf(path, cancellationToken);
                EnsureCurrentFile(path, version);
                inventory.Changes.Add(new ChangedPdf(path, relativePath, version, fingerprint, old));
            }
            catch (Exception error) when (IsFileError(error))
            {
                inventory.Unreadable.Add(relativePath);
                warnings.Add($"{relativePath} — {error.Message}");
            }
        }
        return inventory;
    }

    private IEnumerable<string> EnumeratePdfPaths() => Directory.EnumerateFiles(PdfDirectory, "*.pdf", new EnumerationOptions
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        MatchCasing = MatchCasing.CaseInsensitive
    });

    private static Dictionary<string, IndexedPdf> MatchUniqueMoves(IEnumerable<IndexedPdf> previous, DiskInventory inventory)
    {
        var result = new Dictionary<string, IndexedPdf>(StringComparer.Ordinal);
        if (!inventory.Changes.Any(file => file.Previous is null) ||
            !previous.Any(file => file.Fingerprint is not null && !inventory.Observed.Contains(file.RelativePath)))
        {
            return result;
        }
        var arrivals = inventory.Changes.GroupBy(file => Convert.ToHexString(file.Fingerprint))
            .Where(group => group.Count() == 1 && group.Single().Previous is null)
            .ToDictionary(group => group.Key, group => group.Single().RelativePath, StringComparer.Ordinal);
        if (arrivals.Count == 0) return result;

        foreach (var originals in previous.Where(file => file.Fingerprint is not null)
                     .GroupBy(file => Convert.ToHexString(file.Fingerprint!)))
        {
            // A surviving twin or a second arrival makes the match ambiguous.
            if (originals.Count() != 1 || !arrivals.TryGetValue(originals.Key, out var destination)) continue;
            var original = originals.Single();
            if (!inventory.Observed.Contains(original.RelativePath)) result.Add(destination, original);
        }
        return result;
    }

    private bool IndexIfChanged(ChangedPdf file, CancellationToken cancellationToken)
    {
        if (file.Previous is { } old && (old.Version == file.Version ||
                (old.Fingerprint is { } saved && saved.AsSpan().SequenceEqual(file.Fingerprint))))
        {
            _catalog.UpdateFileStamp(old.Id, file.Version, file.Fingerprint);
            return false;
        }
        var text = DocumentProcessor.ReadPdf(file.Path, cancellationToken);
        EnsureCurrentFile(file.Path, file.Version);
        _catalog.Upsert(file.RelativePath, Path.GetFileNameWithoutExtension(file.Path), text, file.Version, file.Fingerprint);
        return true;
    }

    private static void EnsureCurrentFile(string path, FileVersion expected)
    {
        if (FileVersion.Read(path) != expected)
        {
            throw new IOException("The file changed while Plico read it. Refresh again after saving finishes.");
        }
    }

    private static byte[] FingerprintPdf(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            SnapshotBufferBytes, FileOptions.SequentialScan);
        var fingerprint = SHA256.HashData(file);
        cancellationToken.ThrowIfCancellationRequested();
        return fingerprint;
    }

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException;
}
