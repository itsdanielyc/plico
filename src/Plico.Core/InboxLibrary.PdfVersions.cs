using System.Globalization;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string PdfVersionsFolderName = "versions";
    private const string VersionSourceChanged = "The current PDF changed. Refresh the index before saving a version.";
    private string PdfVersionsDirectory => Path.Combine(_metadataDirectory, PdfVersionsFolderName);

    /// <summary>Lists previous PDF versions, newest first, for the given catalog document.</summary>
    public IReadOnlyList<PdfVersion> GetPdfVersions(long documentId) =>
        _catalog.ReadPdfVersions(documentId).Select(file => file.Version).ToArray();

    /// <summary>Restores earlier bytes as the active PDF and archives the current active version.</summary>
    public Task<DocumentSummary> RestorePdfVersionAsync(long documentId, Guid versionKey, CancellationToken cancellationToken = default) =>
        ChangeActivePdfAsync(documentId, PdfRevisionKind.Restore, async (document, previous, output, token) =>
        {
            var earlier = _catalog.RequirePdfVersion(document.Id, versionKey);
            if (earlier.Version.IsEncrypted)
                throw new InvalidOperationException("Export this password-protected original to open it with its original password.");
            await CopyHistoryFileAsync(earlier, output, token).ConfigureAwait(false);
            return DocumentProcessor.ReadPdf(output, token);
        }, cancellationToken);

    /// <summary>Uses an external PDF as this document's new active version, preserving the external file.</summary>
    public Task<DocumentSummary> ReplacePdfVersionAsync(long documentId, string sourcePath,
        OcrLanguage language = OcrLanguage.English, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!DocumentProcessor.IsPdf(Path.GetExtension(sourcePath))) throw new ArgumentException("Choose a PDF.", nameof(sourcePath));
        return ChangeActivePdfAsync(documentId, PdfRevisionKind.Replace, async (document, previous, output, token) =>
        {
            string replacement = Path.Combine(Path.GetDirectoryName(output)!, "replacement.pdf");
            var initialVersion = FileVersion.Read(sourcePath);
            await CopyFileSnapshotAsync(sourcePath, replacement, initialVersion, VersionSourceChanged, token).ConfigureAwait(false);
            EnsureCurrentFile(sourcePath, initialVersion);
            return (await _processor.PrepareAsync(replacement, output, language, token).ConfigureAwait(false)).Text;
        }, cancellationToken);
    }

    /// <summary>Exports the verified archived PDF to a folder outside the library.</summary>
    public async Task<string> ExportPdfVersionAsync(long documentId, Guid versionKey, string destination,
        CancellationToken cancellationToken = default)
    {
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stage = null;
        try
        {
            string target = ResolveExternalTarget(destination);
            var document = _catalog.Get(documentId);
            var archived = _catalog.RequirePdfVersion(documentId, versionKey);
            stage = CreateCopyStage(target, ExportStagePrefix);
            string output = Path.Combine(stage, StagedPdfName);
            await CopyHistoryFileAsync(archived, output, cancellationToken).ConfigureAwait(false);
            if (Path.GetRelativePath(target, ResolveExternalTarget(target)) != ".")
                throw new IOException("The export folder changed during the copy. Choose it again.");
            return PublishExportFile(output, target, PortableStem($"{document.Title} (version)"), DocumentProcessor.PdfExtension,
                cancellationToken);
        }
        finally
        {
            if (stage is not null) TryRemoveStage(stage);
            _fileOperations.Release();
        }
    }

    // O(old bytes + new bytes + indexing work) time and one stream buffer beyond PDF processing.
    private Task<DocumentSummary> ChangeActivePdfAsync(long documentId, PdfRevisionKind revision,
        Func<DocumentSummary, string, string, CancellationToken, Task<PdfText>> prepare, CancellationToken cancellationToken) =>
        InStageAsync(async (stage, token) =>
        {
            RecoverPdfRevision(token);
            var previousMove = RecoverPendingPdfMove(token);
            if (previousMove.PendingDestination is not null) throw new IOException(previousMove.Notice);

            var document = _catalog.Get(documentId);
            var reference = _catalog.ReadSelectedFiles([documentId]).Single();
            var source = ResolveSelectedPdfSources([reference], token).Single();
            string snapshot = Path.Combine(stage, StagedSourceStem + DocumentProcessor.PdfExtension);
            byte[] oldFingerprint = await CopyAndFingerprintAsync(source.Path, snapshot, token).ConfigureAwait(false);
            if (_catalog.ReadCurrentPdfFingerprint(documentId) is { } indexed && !indexed.AsSpan().SequenceEqual(oldFingerprint))
                throw new IOException(VersionSourceChanged);
            string output = Path.Combine(stage, StagedPdfName);
            PdfText text = await prepare(document, snapshot, output, token).ConfigureAwait(false);
            byte[] nextFingerprint = FingerprintPdf(output, token);

            await _pdfFileAccess.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string active = PrepareRestoreDestination(ResolvePdfPath(reference.RelativePath), token);
                VerifySelectedPdfSource(new SelectedPdfSource(active, source.Version));
                if (oldFingerprint.AsSpan().SequenceEqual(nextFingerprint)) return _catalog.Get(documentId);
                var archived = new PdfVersionFile(documentId,
                    new PdfVersion(Guid.NewGuid(), DateTimeOffset.UtcNow, revision, document.PageCount), oldFingerprint);
                PublishPdfRevision(new PendingPdfRevision(reference.RelativePath, archived, nextFingerprint), active, output, text, token);
                return _catalog.Get(documentId);
            }
            finally
            {
                _pdfFileAccess.Release();
            }
        }, cancellationToken);

    private void PublishPdfRevision(PendingPdfRevision pending, string active, string output, PdfText text, CancellationToken token)
    {
        string archive = ResolvePdfVersionPath(pending.Archived, create: true);
        token.ThrowIfCancellationRequested();
        _catalog.BeginPdfRevision(pending);
        try
        {
            if (!_claimPdfPath(active, archive, allowHardLink: false))
            {
                _catalog.ClearPendingPdfRevision();
                throw new IOException("An existing file occupies the version archive path.");
            }
            if (CheckMovedPdf(archive, pending.Archived.Fingerprint, CancellationToken.None, PdfVersionsDirectory) is null)
                throw new IOException(VersionSourceChanged);
            if (!_claimPdfPath(output, active, allowHardLink: true))
                throw new IOException($"Another file occupies {active}. Move it aside, then reopen the library to recover the earlier PDF.");
            var version = CheckMovedPdf(active, pending.ActiveFingerprint, CancellationToken.None)
                ?? throw new IOException(VersionSourceChanged);
            _catalog.CommitPdfRevision(pending, version, text);
        }
        catch (Exception error) when (IsFileError(error))
        {
            try
            {
                if (_catalog.ReadPendingPdfRevision() is not null) RecoverPdfRevision(pending, CancellationToken.None);
            }
            catch (Exception recovery) when (IsFileError(recovery))
            {
                throw new IOException(recovery.Message, error);
            }
            throw;
        }
    }

    private string? RecoverPdfRevision(CancellationToken token)
    {
        if (_catalog.ReadPendingPdfRevision() is not { } pending) return null;
        _pdfFileAccess.Wait(token);
        try
        {
            return RecoverPdfRevision(pending, token);
        }
        finally
        {
            _pdfFileAccess.Release();
        }
    }

    private string RecoverPdfRevision(PendingPdfRevision pending, CancellationToken token)
    {
        string active = PrepareRestoreDestination(ResolvePdfPath(pending.RelativePath), token);
        string archived = ResolvePdfVersionPath(pending.Archived);
        var originalHash = pending.Archived.Fingerprint;
        if (ReadExistingAttributes(archived) is null && CheckMovedPdf(active, originalHash, token) is { } original)
        {
            FinishPdfRollback(pending.Archived, original);
            return "Kept the original PDF after an interrupted edit.";
        }
        if (CheckMovedPdf(archived, originalHash, token, PdfVersionsDirectory) is not null)
        {
            if (ReadExistingAttributes(active) is null && _claimPdfPath(archived, active, allowHardLink: false))
            {
                if (CheckMovedPdf(active, originalHash, CancellationToken.None) is { } restored)
                {
                    FinishPdfRollback(pending.Archived, restored);
                    return "Restored the original PDF after an interrupted edit.";
                }
            }
            else if (CheckMovedPdf(active, pending.ActiveFingerprint, token) is { } installed)
            {
                PdfText text = DocumentProcessor.ReadPdf(active, token);
                EnsureCurrentFile(active, installed);
                _catalog.CommitPdfRevision(pending, installed, text);
                return "Finished saving the previous PDF edit.";
            }
        }
        throw new IOException($"Plico could not recover the PDF edit at {active}. Keep the files at this path and {archived} until the conflict is resolved.");
    }

    private void FinishPdfRollback(PdfVersionFile original, FileVersion restored)
    {
        // A repaired archive can have a new timestamp. Keep the journal until the restored stamp is durable.
        _catalog.UpdateFileStamp(original.DocumentId, restored, original.Fingerprint);
        _catalog.ClearPendingPdfRevision();
    }

    private string ResolvePdfVersionPath(PdfVersionFile file, bool create = false)
    {
        string folder = Path.Combine(PdfVersionsDirectory, PdfVersionDocumentFolder(file.DocumentId));
        if (create)
        {
            EnsureLibraryDirectory(_metadataDirectory, PdfVersionsFolderName);
            EnsureLibraryDirectory(PdfVersionsDirectory, PdfVersionDocumentFolder(file.DocumentId));
        }
        else
        {
            if (ReadExistingAttributes(PdfVersionsDirectory) is { } root) RequireRestoreDirectory(root);
            if (ReadExistingAttributes(folder) is { } child) RequireRestoreDirectory(child);
        }
        return Path.Combine(folder, PdfVersionFilename(file.Version.Key));
    }

    private static string PdfVersionDocumentFolder(long documentId) => documentId.ToString(CultureInfo.InvariantCulture);

    private static string PdfVersionFilename(Guid key) => key.ToString("N") + DocumentProcessor.PdfExtension;

    private async Task CopyHistoryFileAsync(PdfVersionFile file, string destination, CancellationToken token)
    {
        string source = ResolvePdfVersionPath(file);
        if (ReadExistingAttributes(source) is not { } attributes || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("An archived PDF is unavailable.");
        var before = FileVersion.Read(source);
        byte[] fingerprint = await CopyAndFingerprintAsync(source, destination, token).ConfigureAwait(false);
        EnsureCurrentFile(source, before);
        if (!fingerprint.AsSpan().SequenceEqual(file.Fingerprint)) throw new IOException("An archived PDF differs from the library record.");
    }

    private string? VerifyPurgeVersion(PdfVersionFile file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string path = ResolvePdfVersionPath(file);
        if (ReadExistingAttributes(path) is null) return null;
        if (CheckMovedPdf(path, file.Fingerprint, token, PdfVersionsDirectory) is null)
            throw new IOException($"The archived PDF at {path} has changed. Plico kept it for review.");
        return path;
    }

    private void PurgePdfVersionFiles(IReadOnlyList<PdfVersionFile> files, CancellationToken token)
    {
        foreach (var file in files)
        {
            if (VerifyPurgeVersion(file, token) is { } path) File.Delete(path);
        }
        if (files.Count == 0) return;
        string directory = Path.GetDirectoryName(ResolvePdfVersionPath(files[0]))!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
}
