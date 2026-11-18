using Microsoft.Data.Sqlite;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Searches a backup folder and its ordinary subfolders for exact copies of unavailable PDFs.</summary>
    /// <param name="folder">The folder containing the backup copies.</param>
    /// <param name="progress">Reports the number of restored catalog records.</param>
    /// <param name="cancellationToken">Stops matching or further restores while retaining completed copies.</param>
    // Expected O(files + m log m + bytes examined) to match m unavailable records, plus the individual restores.
    // Additional space is O(m + issues).
    public async Task<RestoreFolderResult> RestoreUnavailablePdfsFromFolderAsync(string folder,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string backupFolder = ResolveDirectoryLinks(folder);
        var unavailable = _catalog.ReadUnavailablePdfs(cancellationToken);
        var issues = new List<RestoreFolderIssue>();
        int restored = 0;
        int unmatched = 0;
        try
        {
            var matches = FindMatchingRestoreCopies(backupFolder, unavailable, issues, cancellationToken);
            foreach (var original in unavailable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (original.Fingerprint is not { } fingerprint)
                {
                    issues.Add(new(original.RelativePath, RestoreFingerprintUnavailable));
                    continue;
                }
                if (!matches.TryGetValue(fingerprint, out string? copyPath))
                {
                    unmatched++;
                    continue;
                }

                try
                {
                    await RestoreUnavailablePdfAsync(original.RelativePath, copyPath, cancellationToken).ConfigureAwait(false);
                    restored++;
                }
                catch (Exception error) when (IsFileError(error) || error is SqliteException or InvalidOperationException)
                {
                    issues.Add(new(original.RelativePath, error.Message));
                    continue;
                }
                progress?.Report(restored);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(restored, unmatched, issues, Cancelled: true);
        }
        return new(restored, unmatched, issues);
    }

    private static Dictionary<byte[], string> FindMatchingRestoreCopies(string folder, IReadOnlyList<IndexedPdf> unavailable,
        List<RestoreFolderIssue> issues, CancellationToken cancellationToken)
    {
        var expectedLengths = new HashSet<long>();
        var expectedFingerprints = new HashSet<byte[]>(FingerprintComparer);
        foreach (var original in unavailable)
        {
            if (original.Fingerprint is not { } fingerprint) continue;
            expectedLengths.Add(original.Version.Length);
            expectedFingerprints.Add(fingerprint);
        }

        var matches = new Dictionary<byte[], string>(FingerprintComparer);
        if (expectedFingerprints.Count == 0) return matches;
        try
        {
            foreach (string path in EnumeratePdfPaths(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fingerprint = ReadRestoreCandidate(path, expectedLengths, issues, cancellationToken);
                if (fingerprint is null || !expectedFingerprints.Contains(fingerprint)) continue;
                matches.TryAdd(fingerprint, path);
                if (matches.Count == expectedFingerprints.Count) break;
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            issues.Add(new(folder, error.Message));
        }
        return matches;
    }

    private static byte[]? ReadRestoreCandidate(string path, HashSet<long> expectedLengths,
        List<RestoreFolderIssue> issues, CancellationToken cancellationToken)
    {
        try
        {
            return expectedLengths.Contains(new FileInfo(path).Length) ? FingerprintPdf(path, cancellationToken) : null;
        }
        catch (Exception error) when (IsFileError(error))
        {
            issues.Add(new(path, error.Message));
            return null;
        }
    }
}
