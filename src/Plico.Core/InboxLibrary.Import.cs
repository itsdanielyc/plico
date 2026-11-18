using System.Buffers;
using System.Security.Cryptography;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string StagedPdfName = "ready.pdf";
    private const int SnapshotBufferBytes = 64 * 1024;
    private const FileOptions SequentialAsync = FileOptions.SequentialScan | FileOptions.Asynchronous;

    /// <summary>Checks whether a filename is one of the types accepted by the import pipeline.</summary>
    public static bool SupportsImport(string path) => DocumentProcessor.SupportsExtension(Path.GetExtension(path));

    /// <summary>Imports a file once and returns the existing PDF when the exact source has already been imported.</summary>
    public Task<FileImportResult> ImportFileAsync(string sourcePath, CancellationToken cancellationToken = default,
        OcrLanguage language = OcrLanguage.English)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!DocumentProcessor.SupportsExtension(extension))
        {
            return Task.FromException<FileImportResult>(new InvalidDataException("Choose a PDF, PNG, or JPEG file."));
        }

        string title = Path.GetFileNameWithoutExtension(sourcePath);
        return InStageAsync(async (stage, token) =>
        {
            var snapshot = Path.Combine(stage, $"source{extension}");
            var initialVersion = FileVersion.Read(sourcePath);
            var fingerprint = await CopyAndFingerprintAsync(sourcePath, snapshot, token).ConfigureAwait(false);
            if (FileVersion.Read(sourcePath) != initialVersion)
            {
                throw new IOException("The source changed while Plico copied it. Import it after it finishes saving.");
            }
            var existing = await FindPriorImportAsync(fingerprint,
                DocumentProcessor.IsPdf(extension) ? new FileInfo(snapshot).Length : null, token).ConfigureAwait(false);
            if (existing is not null) return new FileImportResult(existing, Added: false);

            string pdf = Path.Combine(stage, StagedPdfName);
            var prepared = await _processor.PrepareAsync(snapshot, pdf, language, token).ConfigureAwait(false);
            var document = CommitPdf(pdf, title, prepared.Text, token, fingerprint, prepared.CopiedSource ? fingerprint : null);
            return new FileImportResult(document, Added: true);
        }, cancellationToken);
    }

    private async Task<DocumentSummary?> FindPriorImportAsync(byte[] fingerprint, long? legacyPdfBytes, CancellationToken cancellationToken)
    {
        foreach (var candidate in _catalog.FindSource(fingerprint))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ResolveUnchangedPdf(candidate) is not null) return _catalog.Get(candidate.Id);
        }

        if (legacyPdfBytes is null) return null;
        foreach (var candidate in _catalog.FindLegacyPdfs(legacyPdfBytes.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolveUnchangedPdf(candidate);
            if (path is null) continue;
            using var pdf = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, SnapshotBufferBytes, SequentialAsync);
            var storedFingerprint = await SHA256.HashDataAsync(pdf, cancellationToken).ConfigureAwait(false);
            if (!storedFingerprint.AsSpan().SequenceEqual(fingerprint) || ResolveUnchangedPdf(candidate) is null) continue;

            _catalog.RecordSource(candidate.Id, fingerprint);
            return _catalog.Get(candidate.Id);
        }
        return null;
    }

    private string? ResolveUnchangedPdf(ImportCandidate candidate)
    {
        string path = ResolvePdfPath(candidate.RelativePath);
        try
        {
            return FileVersion.Read(path) == candidate.Version ? path : null;
        }
        catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    // O(bytes) time and constant memory. One read both snapshots the source and computes its identity.
    private static async Task<byte[]> CopyAndFingerprintAsync(string source, string snapshot, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 0, options: SequentialAsync);
        await using var output = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 0, options: SequentialAsync);
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(SnapshotBufferBytes);
        try
        {
            while (true)
            {
                int length = await input.ReadAsync(buffer.AsMemory(0, SnapshotBufferBytes), cancellationToken).ConfigureAwait(false);
                if (length == 0) return fingerprint.GetHashAndReset();
                var block = buffer.AsMemory(0, length);
                fingerprint.AppendData(block.Span);
                await output.WriteAsync(block, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
