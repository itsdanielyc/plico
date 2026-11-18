namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string MoveFolderPrompt = "Choose a folder inside this library’s PDFs directory.";
    private const string PendingMoveUnmatched = "Plico could not match a previous PDF move to its destination. Refresh the index to see current files.";

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
            var recovery = RecoverPendingPdfMove(cancellationToken);
            if (recovery.PendingDestination is not null) throw new IOException(recovery.Notice);
            string pdfRoot = ResolveDirectoryLinks(PdfDirectory);
            string targetFolder = ResolveDirectoryLinks(folder);
            if (!IsAtOrWithinFolder(pdfRoot, targetFolder)) throw new ArgumentException(MoveFolderPrompt, nameof(folder));
            var document = _catalog.Get(documentId);
            var source = ResolveSelectedPdfSources(_catalog.ReadSelectedFiles([documentId]), cancellationToken).Single();
            string sourceFolder = Path.GetDirectoryName(source.Path)!;
            if (Path.GetRelativePath(sourceFolder, targetFolder) == ".") return document;

            var destination = ChooseMoveName(pdfRoot, targetFolder, Path.GetFileName(source.Path), cancellationToken);
            var fingerprint = FingerprintPdf(source.Path, cancellationToken);
            VerifySelectedPdfSource(source);
            var move = new PdfMoveIntent(documentId, document.RelativePath, destination.RelativePath, fingerprint);
            cancellationToken.ThrowIfCancellationRequested();

            // The committed intent identifies the exact catalog row if the process exits after the filesystem move.
            _catalog.RecordPdfMove(move);
            File.Move(source.Path, destination.Path, overwrite: false);
            if (ReadMoveAttributes(source.Path) is not null)
            {
                throw new IOException("The source PDF is still at its original location. Close any viewer and try the move again.");
            }
            var movedVersion = FileVersion.Read(destination.Path);
            VerifySelectedPdfSource(new SelectedPdfSource(destination.Path, movedVersion));
            if (movedVersion != source.Version && !fingerprint.AsSpan().SequenceEqual(FingerprintPdf(destination.Path, CancellationToken.None)))
            {
                throw new IOException("The PDF changed during the move. Refresh the index after saving finishes.");
            }
            _catalog.Relocate(document.Id, destination.RelativePath, Path.GetFileNameWithoutExtension(destination.Path), movedVersion, move);
            return _catalog.Get(document.Id);
        }
        finally
        {
            _fileOperations.Release();
        }
    }

    private (string Path, string RelativePath) ChooseMoveName(string pdfRoot, string folder, string filename,
        CancellationToken cancellationToken)
    {
        string stem = PortableStem(Path.GetFileNameWithoutExtension(filename));
        string extension = Path.GetExtension(filename);
        for (int sequence = 1; ; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = sequence == 1 ? filename : NumberedName(stem, sequence) + extension;
            string destination = Path.Combine(folder, name);
            string relative = Path.GetRelativePath(pdfRoot, destination).Replace(Path.DirectorySeparatorChar, '/');
            if (!Path.Exists(destination) && !_catalog.ReservesPath(relative)) return (destination, relative);
        }
    }

    private (string? Notice, string? PendingDestination) RecoverPendingPdfMove(CancellationToken cancellationToken)
    {
        if (_catalog.ReadPendingPdfMove() is not { } move) return (null, null);
        string target = ResolvePdfPath(move.DestinationRelativePath);
        FileVersion? version;
        try
        {
            if (ReadMoveAttributes(ResolvePdfPath(move.SourceRelativePath)) is not null)
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

    private FileVersion? CheckMovedPdf(string path, byte[] expectedFingerprint, CancellationToken cancellationToken)
    {
        if (ReadMoveAttributes(path) is not { } attributes ||
            (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return null;
        string parent = ResolveDirectoryLinks(Path.GetDirectoryName(path)!);
        if (!IsAtOrWithinFolder(ResolveDirectoryLinks(PdfDirectory), parent)) return null;
        string target = Path.Combine(parent, Path.GetFileName(path));
        var version = FileVersion.Read(target);
        var actualFingerprint = FingerprintPdf(target, cancellationToken);
        EnsureCurrentFile(target, version);
        return actualFingerprint.AsSpan().SequenceEqual(expectedFingerprint) ? version : null;
    }

    private static FileAttributes? ReadMoveAttributes(string path)
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
