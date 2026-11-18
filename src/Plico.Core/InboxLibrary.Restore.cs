namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string RestoreMismatch = "The selected PDF does not match the original.";
    private const string RestoreOccupied = "A file or folder already occupies the original PDF path. Check it and refresh the index.";
    private const string RestoreFingerprintUnavailable =
        "This record has no saved fingerprint. Return the original PDF to its library folder and refresh the index.";

    /// <summary>Copies a verified original back to an unavailable catalog record's path and makes that record present.</summary>
    /// <param name="relativePath">The unavailable path relative to the library's PDF directory.</param>
    /// <param name="copyPath">The source PDF whose bytes must match the catalog's fingerprint.</param>
    /// <param name="cancellationToken">Cancels staging or validation before publication.</param>
    /// <exception cref="InvalidDataException">The selected PDF differs from the cataloged original.</exception>
    /// <exception cref="IOException">The library destination is unavailable or occupied.</exception>
    /// <exception cref="InvalidOperationException">The catalog record is available or lacks a fingerprint.</exception>
    // O(file bytes + folder depth) time and constant file-buffer space.
    public Task<DocumentSummary> RestoreUnavailablePdfAsync(string relativePath, string copyPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(copyPath);
        return InStageAsync(async (stage, token) =>
        {
            var recovery = RecoverPendingPdfMove(token);
            if (recovery.PendingDestination is not null) throw new IOException(recovery.Notice);
            var original = _catalog.GetUnavailablePdf(relativePath);
            byte[] expectedFingerprint = original.Fingerprint ??
                throw new InvalidOperationException(RestoreFingerprintUnavailable);
            string destination = ResolvePdfPath(original.RelativePath);
            RequireRestoreDirectory(ReadExistingAttributes(PdfDirectory));
            if (ReadExistingAttributes(destination) is not null) throw new IOException(RestoreOccupied);
            if (new FileInfo(copyPath).Length != original.Version.Length) throw new InvalidDataException(RestoreMismatch);

            string snapshot = Path.Combine(stage, StagedPdfName);
            byte[] fingerprint = await CopyAndFingerprintAsync(copyPath, snapshot, token).ConfigureAwait(false);
            if (!fingerprint.AsSpan().SequenceEqual(expectedFingerprint)) throw new InvalidDataException(RestoreMismatch);

            await _pdfFileAccess.WaitAsync(token).ConfigureAwait(false);
            try
            {
                destination = PrepareRestoreDestination(destination, token);
                if (ReadExistingAttributes(destination) is not null) throw new IOException(RestoreOccupied);
                var stagedVersion = FileVersion.Read(snapshot);
                token.ThrowIfCancellationRequested();
                if (!_claimPdfPath(snapshot, destination, allowHardLink: true)) throw new IOException(RestoreOccupied);

                // An interruption after publication leaves the verified PDF for the next index refresh.
                var restoredVersion = FileVersion.Read(destination);
                if (restoredVersion != stagedVersion &&
                    !fingerprint.AsSpan().SequenceEqual(FingerprintPdf(destination, CancellationToken.None)))
                {
                    throw new IOException("The PDF changed during restoration. Refresh the index after saving finishes.");
                }
                EnsureCurrentFile(destination, restoredVersion);
                _catalog.UpdateFileStamp(original.Id, restoredVersion, fingerprint);
                return _catalog.Get(original.Id);
            }
            finally
            {
                _pdfFileAccess.Release();
            }
        }, cancellationToken);
    }

    private string PrepareRestoreDestination(string destination, CancellationToken cancellationToken)
    {
        RequireRestoreDirectory(ReadExistingAttributes(PdfDirectory));
        string current = ResolveDirectoryLinks(PdfDirectory);
        string relative = Path.GetRelativePath(PdfDirectory, destination);
        string parent = Path.GetDirectoryName(relative) ?? "";
        foreach (string part in parent.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = Path.Combine(current, part);
            var attributes = ReadExistingAttributes(current) ?? Directory.CreateDirectory(current).Attributes;
            RequireRestoreDirectory(attributes);
        }
        return Path.Combine(current, Path.GetFileName(destination));
    }

    private static void RequireRestoreDirectory(FileAttributes? attributes)
    {
        if (attributes is not { } value ||
            (value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
        {
            throw new IOException("The original PDF path requires an ordinary folder inside the library.");
        }
    }
}
