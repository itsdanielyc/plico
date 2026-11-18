using System.Security.Cryptography;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private static readonly IEqualityComparer<byte[]> FingerprintComparer = EqualityComparer<byte[]>.Create(
        static (first, second) => first.AsSpan().SequenceEqual(second.AsSpan()),
        static bytes =>
        {
            var hash = new HashCode();
            hash.AddBytes(bytes);
            return hash.ToHashCode();
        });

    private sealed record ChangedPdf(string Path, string RelativePath, FileVersion Version,
        byte[] Fingerprint, IndexedPdf? Previous);

    private sealed record DiskInventory(HashSet<string> Observed, HashSet<string> Unreadable, List<ChangedPdf> Changes);

    // Expected O(files + rows + examined bytes) time and O(files + rows + largest parsed document) space.
    private RefreshResult ReconcileFiles(CancellationToken cancellationToken, string? pendingMoveDestination)
    {
        var previous = _catalog.ReadIndexedFiles();
        var warnings = new List<string>();
        var inventory = DiscoverChanges(previous, warnings, pendingMoveDestination, cancellationToken);
        var moves = MatchUniqueMoves(previous, inventory);
        var movedSources = RelocateMatchedPdfs(moves, inventory, warnings, cancellationToken);
        int indexed = 0;
        foreach (var file in inventory.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (moves.ContainsKey(file.RelativePath)) continue;
            try
            {
                EnsureCurrentFile(file.Path, file.Version);
                var arrival = movedSources.Contains(file.RelativePath) ? file with { Previous = null } : file;
                if (IndexIfChanged(arrival, cancellationToken)) indexed++;
            }
            catch (Exception error) when (IsFileError(error))
            {
                inventory.Unreadable.Add(file.RelativePath);
                warnings.Add($"{file.RelativePath} — {error.Message}");
            }
        }

        var unavailable = _catalog.MarkMissing(previous.Values
            .Where(file => file.Version.Present && !movedSources.Contains(file.RelativePath) &&
                (!inventory.Observed.Contains(file.RelativePath) || inventory.Unreadable.Contains(file.RelativePath)))
            .Select(file => file.RelativePath));
        return new(indexed, warnings, movedSources.Count) { UnavailablePaths = unavailable };
    }

    private HashSet<string> RelocateMatchedPdfs(IReadOnlyDictionary<string, IndexedPdf> moves, DiskInventory inventory,
        List<string> warnings, CancellationToken cancellationToken)
    {
        if (moves.Count == 0) return new(StringComparer.Ordinal);
        var verified = new List<CatalogRelocation>(moves.Count);
        foreach (var file in inventory.Changes)
        {
            if (!moves.TryGetValue(file.RelativePath, out var original)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureCurrentFile(file.Path, file.Version);
            }
            catch (Exception error) when (IsFileError(error))
            {
                // A changed destination can invalidate a whole chain of moves.
                inventory.Unreadable.UnionWith(moves.Keys);
                warnings.Add($"PDF moves paused at {file.RelativePath} — {error.Message}");
                return new(StringComparer.Ordinal);
            }

            verified.Add(new(original.Id, original.RelativePath, file.RelativePath,
                Path.GetFileNameWithoutExtension(file.Path), file.Version));
        }
        _catalog.RelocateBatch(verified, cancellationToken);
        return verified.Select(move => move.SourceRelativePath).ToHashSet(StringComparer.Ordinal);
    }

    private DiskInventory DiscoverChanges(IReadOnlyDictionary<string, IndexedPdf> previous, List<string> warnings,
        string? pendingMoveDestination, CancellationToken cancellationToken)
    {
        var inventory = new DiskInventory(new(StringComparer.Ordinal), new(StringComparer.Ordinal), []);
        foreach (var path in EnumeratePdfPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = RelativePdfPath(path);
            inventory.Observed.Add(relativePath);
            if (relativePath == pendingMoveDestination) continue;
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

    // Expected O(rows + changes) time and O(changes) additional space. Each blocked move is removed once.
    private static Dictionary<string, IndexedPdf> MatchUniqueMoves(IReadOnlyDictionary<string, IndexedPdf> previous, DiskInventory inventory)
    {
        var moves = new Dictionary<string, IndexedPdf>(StringComparer.Ordinal);
        if (inventory.Changes.Count == 0 || previous.Count == 0) return moves;
        var arrivals = inventory.Changes.GroupBy(file => file.Fingerprint, FingerprintComparer)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().RelativePath, FingerprintComparer);
        if (arrivals.Count == 0) return moves;
        var changedPaths = inventory.Changes.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);

        foreach (var originals in previous.Values
                     .Where(file => file.Fingerprint is { } fingerprint && arrivals.ContainsKey(fingerprint))
                     .GroupBy(file => file.Fingerprint!, FingerprintComparer))
        {
            // A surviving twin or a second arrival makes the match ambiguous.
            if (originals.Count() != 1) continue;
            var original = originals.Single();
            string destination = arrivals[originals.Key];
            if (destination != original.RelativePath && HasLeftIndexedPath(original, inventory, changedPaths))
            {
                moves.Add(destination, original);
            }
        }

        var departing = moves.Values.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        var blocked = new Queue<string>(moves.Keys.Where(path => previous.ContainsKey(path) && !departing.Contains(path)));
        while (blocked.TryDequeue(out var path))
        {
            // A reserved destination can only be used when its previous PDF is also moving.
            if (moves.Remove(path, out var rejected) && moves.ContainsKey(rejected.RelativePath))
            {
                blocked.Enqueue(rejected.RelativePath);
            }
        }
        return moves;
    }

    private static bool HasLeftIndexedPath(IndexedPdf original, DiskInventory inventory,
        IReadOnlyDictionary<string, ChangedPdf> changedPaths) =>
        !inventory.Observed.Contains(original.RelativePath) ||
        (changedPaths.TryGetValue(original.RelativePath, out var current) &&
            !original.Fingerprint.AsSpan().SequenceEqual(current.Fingerprint));

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
