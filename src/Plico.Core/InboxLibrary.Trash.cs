using System.Globalization;

namespace Plico.Core;

/// <summary>A recoverable PDF in the library's Trash and the path where it can be restored.</summary>
public sealed record TrashedDocument(long DocumentId, string Title, string OriginalPath, DateTimeOffset TrashedAt);

public sealed partial class InboxLibrary
{
    private const string TrashFolderName = "trash";
    private const string TrashCatalogPrefix = "/trash/";
    private const string TrashFileChanged = "The PDF in Trash differs from its catalog record.";
    private const string TrashPathOccupied = "A file or folder already occupies the Trash destination.";
    private const string TrashRecoveryPaused = "Plico could not finish the previous Trash operation. Check that the files are available.";

    private string TrashDirectory => Path.Combine(_metadataDirectory, TrashFolderName);
    private static string TrashFilename(long documentId) =>
        documentId.ToString(CultureInfo.InvariantCulture) + DocumentProcessor.PdfExtension;
    private static string TrashCatalogPath(long documentId) => TrashCatalogPrefix + TrashFilename(documentId);

    /// <summary>Lists the PDFs in Trash, with the most recently trashed first.</summary>
    public IReadOnlyList<TrashedDocument> GetTrash() => _catalog.ReadTrash();

    /// <summary>Moves the current PDF to recoverable library Trash.</summary>
    // O(file bytes) time for hashing and constant file-buffer space.
    public Task TrashPdfAsync(long documentId, CancellationToken cancellationToken = default) =>
        WithTrashLeaseAsync(() =>
        {
            var file = _catalog.ReadSelectedFiles([documentId]).Single();
            var source = ResolveSelectedPdfSources([file], cancellationToken).Single();
            string trash = EnsureLibraryDirectory(_metadataDirectory, TrashFolderName);
            var fingerprint = FingerprintPdf(source.Path, cancellationToken);
            var move = new PdfMoveIntent(documentId, file.RelativePath, TrashCatalogPath(documentId), fingerprint);
            CommitTrashMove(source, Path.Combine(trash, TrashFilename(documentId)), move, TrashPathOccupied, cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>Returns the trashed PDF to its original path and restores access to its catalog record.</summary>
    // O(file bytes + folder depth) time and constant file-buffer space.
    public Task<DocumentSummary> RestoreTrashedPdfAsync(long documentId, CancellationToken cancellationToken = default) =>
        WithTrashLeaseAsync(() =>
        {
            var file = _catalog.ReadTrashFile(documentId);
            string destination = PrepareRestoreDestination(ResolvePdfPath(file.OriginalPath), cancellationToken);
            if (_catalog.ReservesPath(file.OriginalPath)) throw new IOException(RestoreOccupied);
            var source = VerifyTrashFile(file, cancellationToken);
            var move = new PdfMoveIntent(documentId, TrashCatalogPath(documentId), file.OriginalPath, file.Fingerprint);
            CommitTrashMove(source, destination, move, RestoreOccupied, cancellationToken);
            return _catalog.Get(documentId);
        }, cancellationToken);

    /// <summary>Permanently removes a PDF and its catalog record from Trash.</summary>
    // O(file bytes) time for hashing and constant file-buffer space.
    public Task PurgeTrashedPdfAsync(long documentId, CancellationToken cancellationToken = default) =>
        WithTrashLeaseAsync(() =>
        {
            var file = _catalog.ReadTrashFile(documentId);
            string path = ResolveTrashFile(documentId);
            bool present = ReadExistingAttributes(path) is not null;
            if (present) VerifyTrashFile(file, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _catalog.RecordPdfMove(new(documentId, TrashCatalogPath(documentId), "", file.Fingerprint));
            if (present) File.Delete(path);
            PurgePdfVersionFiles(documentId, CancellationToken.None);
            _catalog.CompletePurge(documentId);
            return true;
        }, cancellationToken);

    private async Task<TResult> WithTrashLeaseAsync<TResult>(Func<TResult> operation, CancellationToken token)
    {
        await _fileOperations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            RecoverPdfRevision(token);
            var recovery = RecoverPendingPdfMove(token);
            if (recovery.PendingDestination is not null) throw new IOException(recovery.Notice);
            await _pdfFileAccess.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return operation();
            }
            finally
            {
                _pdfFileAccess.Release();
            }
        }
        finally
        {
            _fileOperations.Release();
        }
    }

    private void CommitTrashMove(SelectedPdfSource source, string destination, PdfMoveIntent move,
        string occupiedMessage, CancellationToken token)
    {
        VerifySelectedPdfSource(source);
        token.ThrowIfCancellationRequested();
        if (ReadExistingAttributes(destination) is not null || !TryCommitPdfMove(source, destination, move))
            throw new IOException(occupiedMessage);
    }

    private string ResolveTrashFile(long documentId)
    {
        if (ReadExistingAttributes(TrashDirectory) is { } attributes) RequireRestoreDirectory(attributes);
        return Path.Combine(TrashDirectory, TrashFilename(documentId));
    }

    private SelectedPdfSource VerifyTrashFile(TrashFile file, CancellationToken token)
    {
        string path = ResolveTrashFile(file.DocumentId);
        var version = CheckMovedPdf(path, file.Fingerprint, token, TrashDirectory)
            ?? throw new IOException(TrashFileChanged);
        return new(path, version);
    }

    private static bool InvolvesTrash(PdfMoveIntent move)
    {
        string trashPath = TrashCatalogPath(move.DocumentId);
        return move.SourceRelativePath == trashPath || move.DestinationRelativePath == trashPath;
    }

    private void CompleteJournaledPdfMove(PdfMoveIntent move, string destination, FileVersion version)
    {
        string filename = Path.GetFileNameWithoutExtension(destination);
        if (InvolvesTrash(move))
        {
            _catalog.CommitTrashMove(move, version, filename, move.DestinationRelativePath == TrashCatalogPath(move.DocumentId));
            return;
        }
        _catalog.Relocate(move.DocumentId, move.DestinationRelativePath, filename, version, move);
    }

    private (string? Notice, string? PendingDestination) RecoverTrashOperation(PdfMoveIntent move, CancellationToken token)
    {
        try
        {
            string trashFile = ResolveTrashFile(move.DocumentId);
            if (move.DestinationRelativePath.Length == 0)
            {
                if (ReadExistingAttributes(trashFile) is not null) _catalog.ClearPendingPdfMove();
                else
                {
                    PurgePdfVersionFiles(move.DocumentId, token);
                    _catalog.CompletePurge(move.DocumentId);
                }
                return (null, null);
            }

            bool toTrash = move.DestinationRelativePath == TrashCatalogPath(move.DocumentId);
            string source = toTrash ? ResolvePdfPath(move.SourceRelativePath) : trashFile;
            string destination = toTrash ? trashFile : ResolvePdfPath(move.DestinationRelativePath);
            var destinationRoot = toTrash ? TrashDirectory : PdfDirectory;
            var version = CheckMovedPdf(destination, move.Fingerprint, token, destinationRoot);
            if (ReadExistingAttributes(source) is not null)
            {
                string sourceRoot = toTrash ? PdfDirectory : TrashDirectory;
                if (version is null || CheckMovedPdf(source, move.Fingerprint, token, sourceRoot) is not null)
                {
                    _catalog.ClearPendingPdfMove();
                    return (null, null);
                }
            }
            if (version is null) return (TrashRecoveryPaused, move.DestinationRelativePath);

            CompleteJournaledPdfMove(move, destination, version.Value);
            return ("Finished the previous Trash operation.", null);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return ($"{TrashRecoveryPaused} {error.Message}", move.DestinationRelativePath);
        }
    }
}
