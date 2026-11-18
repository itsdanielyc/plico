using Microsoft.Data.Sqlite;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string MoveFolderPrompt = "Choose a folder inside this library’s PDFs directory.";
    private const string PendingMoveUnmatched = "Plico could not match a previous PDF move to its destination. Refresh the index to see current files.";
    private const int MinimumBatchMoveCount = 2;
    private const int FirstNumberedName = 2;
    private readonly record struct MoveTarget(string PdfRoot, string Folder);

    /// <summary>Reports the result of recovering a move when the library opened.</summary>
    public string? MoveRecoveryNotice { get; private set; }

    /// <summary>Moves a PDF into an existing library folder and updates the same catalog record.</summary>
    /// <param name="documentId">The current catalog document ID.</param>
    /// <param name="folder">The destination inside the library’s PDFs directory.</param>
    /// <param name="cancellationToken">Cancels before the move is committed.</param>
    // O(file bytes) for the recovery fingerprint plus candidate-name lookups, with a bounded read buffer.
    public async Task<DocumentSummary> MovePdfAsync(long documentId, string folder, CancellationToken cancellationToken = default)
    {
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var target = PrepareMoveTarget(folder, cancellationToken);
            var files = _catalog.ReadSelectedFiles([documentId]);
            var source = ResolveSelectedPdfSources(files, cancellationToken, target.PdfRoot).Single();
            await MovePreparedPdfAsync(files.Single(), source, target, null, cancellationToken).ConfigureAwait(false);
            return _catalog.Get(documentId);
        }
        finally
        {
            _fileOperations.Release();
        }
    }

    /// <summary>Checks the entire selection and files the PDFs in order, committing each move separately.</summary>
    /// <param name="documentIds">The selected IDs. The first occurrence of each ID sets the move order.</param>
    /// <param name="folder">The destination inside the library’s PDFs directory.</param>
    /// <param name="progress">Reports the number of PDFs moved or already in the folder.</param>
    /// <param name="cancellationToken">Cancels before another move starts. Completed moves are returned in the result.</param>
    // O(n log c + total hashed bytes + filename lookups) time for n files in a c-row catalog.
    // O(n) selected file and suffix state, plus a bounded fingerprint buffer.
    public async Task<BatchMoveResult> MoveSelectedPdfsAsync(IReadOnlyList<long> documentIds, string folder,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        long[] selection = documentIds.ToArray();
        if (selection.Length < MinimumBatchMoveCount) throw new ArgumentException("Select at least two PDFs to file.", nameof(documentIds));
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var target = PrepareMoveTarget(folder, cancellationToken);
            var files = _catalog.ReadSelectedFiles(selection);
            if (files.Count < MinimumBatchMoveCount) throw new ArgumentException("Select at least two distinct PDFs to file.", nameof(documentIds));
            var sources = ResolveSelectedPdfSources(files, cancellationToken, target.PdfRoot);
            var nextSuffixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int moved = 0, alreadyInFolder = 0;
            for (int index = 0; index < files.Count; index++)
            {
                var file = files[index];
                try
                {
                    if (await MovePreparedPdfAsync(file, sources[index], target, nextSuffixes, cancellationToken).ConfigureAwait(false)) moved++;
                    else alreadyInFolder++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new BatchMoveResult(moved, alreadyInFolder, files.Count - index, Cancelled: true);
                }
                catch (Exception error) when (IsFileError(error) || error is SqliteException or InvalidOperationException)
                {
                    return new BatchMoveResult(moved, alreadyInFolder, files.Count - index,
                        Failure: new BatchMoveFailure(file.RelativePath, error.Message));
                }
                progress?.Report(index + 1);
            }
            return new BatchMoveResult(moved, alreadyInFolder, Remaining: 0);
        }
        finally
        {
            _fileOperations.Release();
        }
    }

    private MoveTarget PrepareMoveTarget(string folder, CancellationToken cancellationToken)
    {
        var recovery = RecoverPendingPdfMove(cancellationToken);
        if (recovery.PendingDestination is not null) throw new IOException(recovery.Notice);
        string pdfRoot = ResolveDirectoryLinks(PdfDirectory);
        string targetFolder = ResolveDirectoryLinks(folder);
        if (!IsAtOrWithinFolder(pdfRoot, targetFolder)) throw new ArgumentException(MoveFolderPrompt, nameof(folder));
        return new MoveTarget(pdfRoot, targetFolder);
    }

    private async Task<bool> MovePreparedPdfAsync(CatalogFileReference file, SelectedPdfSource source, MoveTarget target,
        Dictionary<string, int>? nextSuffixes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VerifySelectedPdfSource(source);
        string sourceFolder = Path.GetDirectoryName(source.Path)!;
        if (Path.GetRelativePath(sourceFolder, target.Folder) == ".") return false;

        var fingerprint = FingerprintPdf(source.Path, cancellationToken);
        await _pdfFileAccess.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MoveToAvailableName(file, source, target, fingerprint, nextSuffixes, cancellationToken);
        }
        finally
        {
            _pdfFileAccess.Release();
        }
        return true;
    }

    private bool TryCommitPdfMove(SelectedPdfSource source, string destination, PdfMoveIntent move)
    {
        // The committed intent identifies the exact catalog row if the process exits after the filesystem move.
        _catalog.RecordPdfMove(move);
        if (!_claimPdfPath(source.Path, destination, allowHardLink: false))
        {
            _catalog.ClearPendingPdfMove();
            return false;
        }
        if (ReadExistingAttributes(source.Path) is not null && (!InvolvesTrash(move) || FileVersion.Read(source.Path) == source.Version))
        {
            throw new IOException("The source PDF is still at its original location. Close any viewer and try the move again.");
        }
        var movedVersion = FileVersion.Read(destination);
        VerifySelectedPdfSource(new SelectedPdfSource(destination, movedVersion));
        if (movedVersion != source.Version && !move.Fingerprint.AsSpan().SequenceEqual(FingerprintPdf(destination, CancellationToken.None)))
        {
            throw new IOException("The PDF changed during the move. Refresh the index after saving finishes.");
        }
        CompleteJournaledPdfMove(move, destination, movedVersion);
        return true;
    }

    // O(k log n) time for k candidate paths among n catalog entries, with constant extra space per move.
    private void MoveToAvailableName(CatalogFileReference file, SelectedPdfSource source, MoveTarget target, byte[] fingerprint,
        Dictionary<string, int>? nextSuffixes, CancellationToken cancellationToken)
    {
        string filename = Path.GetFileName(source.Path);
        if (TryMoveToName(filename)) return;
        string stem = PortableStem(Path.GetFileNameWithoutExtension(filename));
        string extension = Path.GetExtension(filename);
        string numberedFamily = stem + extension;
        int first = nextSuffixes?.GetValueOrDefault(numberedFamily, FirstNumberedName) ?? FirstNumberedName;
        for (int sequence = first; ; sequence++)
        {
            string name = NumberedName(stem, sequence) + extension;
            if (!TryMoveToName(name)) continue;
            if (nextSuffixes is not null) nextSuffixes[numberedFamily] = sequence + 1;
            return;
        }

        bool TryMoveToName(string name)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.Combine(target.Folder, name);
            if (Path.Exists(destination)) return false;
            string relative = Path.GetRelativePath(target.PdfRoot, destination).Replace(Path.DirectorySeparatorChar, '/');
            if (_catalog.ReservesPath(relative)) return false;

            VerifySelectedPdfSource(source);
            cancellationToken.ThrowIfCancellationRequested();
            var move = new PdfMoveIntent(file.Id, file.RelativePath, relative, fingerprint);
            return TryCommitPdfMove(source, destination, move);
        }
    }

    private (string? Notice, string? PendingDestination) RecoverPendingPdfMove(CancellationToken cancellationToken)
    {
        if (_catalog.ReadPendingPdfMove() is not { } move) return (null, null);
        if (InvolvesTrash(move)) return RecoverTrashOperation(move, cancellationToken);
        string target = ResolvePdfPath(move.DestinationRelativePath);
        FileVersion? version;
        try
        {
            if (ReadExistingAttributes(ResolvePdfPath(move.SourceRelativePath)) is not null)
            {
                _catalog.ClearPendingPdfMove();
                return (null, null);
            }
            version = CheckMovedPdf(target, move.Fingerprint, cancellationToken);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return ($"Plico will retry finishing the previous move after the PDF is available. {error.Message}", move.DestinationRelativePath);
        }
        if (version is not { } verified)
        {
            _catalog.ClearPendingPdfMove();
            return (PendingMoveUnmatched, null);
        }

        _catalog.Relocate(move.DocumentId, move.DestinationRelativePath, Path.GetFileNameWithoutExtension(target), verified, move);
        return ("Finished the previous PDF move.", null);
    }

    private FileVersion? CheckMovedPdf(string path, byte[] expectedFingerprint, CancellationToken cancellationToken, string? allowedRoot = null)
    {
        if (ReadExistingAttributes(path) is not { } attributes ||
            (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return null;
        string parent = ResolveDirectoryLinks(Path.GetDirectoryName(path)!);
        if (!IsAtOrWithinFolder(ResolveDirectoryLinks(allowedRoot ?? PdfDirectory), parent)) return null;
        string target = Path.Combine(parent, Path.GetFileName(path));
        var version = FileVersion.Read(target);
        var actualFingerprint = FingerprintPdf(target, cancellationToken);
        EnsureCurrentFile(target, version);
        return actualFingerprint.AsSpan().SequenceEqual(expectedFingerprint) ? version : null;
    }

    private static FileAttributes? ReadExistingAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
