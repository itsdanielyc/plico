using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NAPS2.Pdf;
using PdfSharp.Pdf.IO;

namespace Plico.Core;

/// <summary>The file format or additional data to include in a single PDF export.</summary>
public enum PdfExportFormat { Original, PdfA, PasswordProtected, WithMetadata }

/// <summary>The export format, its optional password, and the OCR language for archival copies.</summary>
public sealed record PdfExportOptions(PdfExportFormat Format, string? Password = null, OcrLanguage Language = OcrLanguage.English)
{
    private const int MaximumPasswordBytes = 127;

    /// <summary>Checks whether an export password fits the PDF 2.0 limits.</summary>
    public static bool IsValidPassword(string? password) => !string.IsNullOrWhiteSpace(password) &&
        !password.Any(char.IsControl) && Encoding.UTF8.GetByteCount(password) <= MaximumPasswordBytes;

    internal void Validate()
    {
        if (!Enum.IsDefined(Format)) throw new ArgumentOutOfRangeException(nameof(Format));
        if (Format == PdfExportFormat.PasswordProtected && !IsValidPassword(Password))
            throw new ArgumentException("Enter a PDF password of up to 127 UTF-8 bytes without control characters.");
    }

    public override string ToString() => Format.ToString();
}

public sealed partial class InboxLibrary
{
    private const int PdfVersion2 = 20;
    private const int OwnerPasswordBytes = 32;

    private async Task WritePdfExportAsync(string source, string output, DocumentSummary document, PdfExportOptions options,
        CancellationToken token)
    {
        if (options.Format == PdfExportFormat.PdfA)
        {
            var settings = new PdfExportParams
            {
                Compat = PdfCompat.PdfA2B,
                Metadata = new PdfMetadata { Creator = "Plico" }
            };
            var archived = await _processor.ReprocessPdfAsync(source, output, options.Language, token, settings).ConfigureAwait(false);
            if (archived.PageCount != document.PageCount || (document.HasText && archived.Body.Length == 0))
                throw new InvalidDataException("The archival copy did not preserve the pages or searchable text. Choose the document’s OCR language and try again.");
            return;
        }

        token.ThrowIfCancellationRequested();
        if (options.Format == PdfExportFormat.WithMetadata)
        {
            WriteMetadataPdf(source, output, document);
            return;
        }

        using var pdf = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        pdf.Version = PdfVersion2;
        pdf.SecurityHandler.SetEncryptionToV5();
        pdf.SecuritySettings.UserPassword = options.Password!;
        pdf.SecuritySettings.OwnerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(OwnerPasswordBytes));
        token.ThrowIfCancellationRequested();
        pdf.Save(output);
    }

    private static void WriteMetadataPdf(string source, string output, DocumentSummary document)
    {
        using var pdf = PdfSharpCore.Pdf.IO.PdfReader.Open(source, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify);
        pdf.Info.Title = document.Title;
        pdf.Info.Keywords = string.Join(", ", document.Tags);
        XNamespace adobe = "adobe:ns:meta/";
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace pdfSchema = "http://ns.adobe.com/pdf/1.3/";
        var xml = new XElement(adobe + "xmpmeta", new XAttribute(XNamespace.Xmlns + "x", adobe),
            new XElement(rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", rdf),
                new XElement(rdf + "Description", new XAttribute(rdf + "about", ""),
                    new XAttribute(XNamespace.Xmlns + "dc", dc), new XAttribute(XNamespace.Xmlns + "pdf", pdfSchema),
                    new XElement(dc + "title", new XElement(rdf + "Alt",
                        new XElement(rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), document.Title))),
                    new XElement(dc + "subject", new XElement(rdf + "Bag", document.Tags.Select(tag => new XElement(rdf + "li", tag)))),
                    new XElement(pdfSchema + "Keywords", pdf.Info.Keywords))));
        var metadata = new PdfSharpCore.Pdf.PdfDictionary(pdf);
        metadata.Elements.SetName("/Type", "/Metadata");
        metadata.Elements.SetName("/Subtype", "/XML");
        metadata.CreateStream(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting)));
        pdf.Internals.AddObject(metadata);
        pdf.Internals.Catalog.Elements["/Metadata"] = metadata.Reference!;
        pdf.Version = Math.Max(pdf.Version, 14);
        pdf.Save(output);
    }
}
