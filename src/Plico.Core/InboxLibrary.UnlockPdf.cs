using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Imports a PDF opened with the supplied password, retaining the encrypted source as its first version.</summary>
    /// <param name="sourcePath">The password-protected PDF selected by the user.</param>
    /// <param name="password">A password that opens the PDF. Plico uses it only for this operation.</param>
    /// <param name="language">The language for OCR on pages that need text.</param>
    /// <param name="cancellationToken">Cancels preparation before either copy is published.</param>
    public Task<FileImportResult> UnlockAndImportPdfAsync(string sourcePath, string password, OcrLanguage language = OcrLanguage.English,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(password);
        if (!DocumentProcessor.IsPdf(Path.GetExtension(sourcePath))) throw new ArgumentException("Choose a PDF.", nameof(sourcePath));
        return InStageAsync(async (stage, token) =>
        {
            var fileVersion = FileVersion.Read(sourcePath);
            string encrypted = Path.Combine(stage, "protected.pdf");
            var fingerprint = await CopyAndFingerprintAsync(sourcePath, encrypted, token).ConfigureAwait(false);
            EnsureCurrentFile(sourcePath, fileVersion);
            string decrypted = Path.Combine(stage, "unlocked.pdf");
            int expectedPages = WriteUnlockedPdf(encrypted, decrypted, password, token);
            var previous = FindUnchangedCandidate(_catalog.FindSource(fingerprint), token);
            if (previous is not null)
            {
                SaveEncryptedOriginal(previous.Id, encrypted, fingerprint, expectedPages, token);
                return new FileImportResult(previous, Added: false);
            }

            string output = Path.Combine(stage, StagedPdfName);
            var prepared = await _processor.PrepareAsync(decrypted, output, language, token).ConfigureAwait(false);
            if (prepared.Text.PageCount != expectedPages) throw new InvalidDataException("The unlocked PDF has an unexpected page count.");
            var document = CommitPdf(output, Path.GetFileNameWithoutExtension(sourcePath), prepared.Text, token,
                sourceFingerprint: fingerprint, recognizedLanguage: prepared.CopiedSource ? null : language);
            // The source fingerprint lets a retry finish recording the same encrypted original after publication.
            SaveEncryptedOriginal(document.Id, encrypted, fingerprint, expectedPages, CancellationToken.None);
            return new FileImportResult(document, Added: true);
        }, cancellationToken);
    }

    private static int WriteUnlockedPdf(string source, string output, string password, CancellationToken token)
    {
        try
        {
            using (var inspect = UglyToad.PdfPig.PdfDocument.Open(source, new UglyToad.PdfPig.ParsingOptions { Password = password }))
            {
                if (!inspect.IsEncrypted) throw new InvalidDataException("This PDF already opens without a password.");
            }
            using var encrypted = PdfReader.Open(source, password, PdfDocumentOpenMode.Import);
            int pageCount = encrypted.PageCount;
            if (pageCount == 0) throw new InvalidDataException("This PDF has no pages.");
            using var unlocked = new PdfDocument { Version = encrypted.Version };
            unlocked.Info.Title = encrypted.Info.Title;
            unlocked.Info.Creator = "Plico";
            for (int index = 0; index < pageCount; index++)
            {
                token.ThrowIfCancellationRequested();
                unlocked.AddPage(encrypted.Pages[index]);
            }
            token.ThrowIfCancellationRequested();
            using var stream = File.Open(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            unlocked.Save(stream);
            return pageCount;
        }
        catch (Exception error) when (error is PdfReaderException or PdfDocumentEncryptedException or PdfDocumentFormatException)
        {
            throw new InvalidDataException("Plico could not unlock this PDF. Check the password and try again.");
        }
    }

    private void SaveEncryptedOriginal(long documentId, string encrypted, byte[] fingerprint, int pageCount, CancellationToken token)
    {
        var key = new Guid(fingerprint.AsSpan(0, 16));
        var version = new PdfVersionFile(documentId, new PdfVersion(key, DateTimeOffset.UtcNow, PdfRevisionKind.Unlock, pageCount), fingerprint);
        string archive = ResolvePdfVersionPath(version, create: true);
        token.ThrowIfCancellationRequested();
        if (!_claimPdfPath(encrypted, archive, allowHardLink: true) &&
            CheckMovedPdf(archive, fingerprint, token, PdfVersionsDirectory) is null)
            throw new IOException("A different file occupies the original PDF's archive path.");
        _catalog.SaveInitialPdfVersion(version);
    }
}
