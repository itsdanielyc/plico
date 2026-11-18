using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Plico.Core;

/// <summary>Reads ENEX a note at a time and writes printable notes with their supported resources.</summary>
internal static class EvernoteMigration
{
    private const string FormatName = "evernote-enex";
    private const string DatePattern = "yyyyMMdd'T'HHmmss'Z'";
    private static readonly IReadOnlyDictionary<string, string> MimeExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/tiff"] = ".tiff",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["text/plain"] = ".txt",
        ["text/markdown"] = ".md",
        ["message/rfc822"] = ".eml"
    };

    internal static IEnumerable<MigrationEntry> Read(string source, OcrLanguage language, CancellationToken cancellationToken)
    {
        using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        string libraryKey = MigrationFiles.StableKey(file);
        file.Position = 0;
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        reader.MoveToContent();
        if (reader.LocalName != "en-export") throw new InvalidDataException("Choose an Evernote ENEX export.");
        int ordinal = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1 || reader.LocalName != "note") continue;
            XElement note;
            using (var subtree = reader.ReadSubtree()) note = XElement.Load(subtree);
            ordinal++;
            string title = Element(note, "title") ?? $"Evernote note {ordinal}";
            string? created = Element(note, "created");
            DateOnly? documentDate = DateTimeOffset.TryParseExact(created, DatePattern, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var timestamp) ? DateOnly.FromDateTime(timestamp.UtcDateTime) : null;
            var tags = note.Elements("tag").Select(tag => tag.Value.Trim()).Where(label => label.Length > 0)
                .Select(label => new ImportedTag([label])).ToArray();
            string recordKey = Element(note, "guid") ?? ordinal.ToString(CultureInfo.InvariantCulture);
            var key = new MigrationSourceKey(FormatName, libraryKey, recordKey);
            var notices = note.Element("note-attributes")?.Elements()
                .Select(attribute => new LibraryMigrationNotice(title, attribute.Name.LocalName, "Evernote note attribute", attribute.Value))
                .ToList() ?? [];
            if (Element(note, "updated") is { } updated) notices.Add(new(title, "Source updated", "Evernote timestamp", updated));
            if (created is not null) notices.Add(new(title, "Source created", "Evernote timestamp", created));
            var metadata = new ImportedDocumentMetadata(title, documentDate, null, tags, [], null, null, [], null);
            yield return new MigrationEntry(key, metadata,
                (stage, token) => StageNoteAsync(note, title, stage, language, notices, token), notices);
        }
    }

    private static async Task<string> StageNoteAsync(XElement note, string title, string stage, OcrLanguage language,
        List<LibraryMigrationNotice> notices, CancellationToken cancellationToken)
    {
        var writer = new DocumentProcessor(Path.Combine(AppContext.BaseDirectory, "tessdata"));
        var resources = new List<string>();
        var labels = new List<string>();
        int number = 0;
        foreach (var resource in note.Elements("resource"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? mime = Element(resource, "mime");
            string label = resource.Element("resource-attributes")?.Element("file-name")?.Value ?? $"Resource {++number}";
            labels.Add(label);
            string extension = Path.GetExtension(label).ToLowerInvariant();
            if (!InboxLibrary.SupportsImport("resource" + extension) && !MimeExtensions.TryGetValue(mime ?? "", out extension!))
            {
                notices.Add(new(title, label, "Resource kept in the original ENEX", mime));
                continue;
            }
            try
            {
                string data = Element(resource, "data") ?? throw new InvalidDataException("An Evernote resource has no data.");
                string resourceStage = Directory.CreateDirectory(Path.Combine(stage, Path.GetRandomFileName())).FullName;
                string input = Path.Combine(resourceStage, "resource" + extension);
                string output = Path.Combine(resourceStage, "ready.pdf");
                await File.WriteAllBytesAsync(input, Convert.FromBase64String(data), cancellationToken).ConfigureAwait(false);
                await writer.PrepareAsync(input, output, language, cancellationToken).ConfigureAwait(false);
                resources.Add(output);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException)
            {
                notices.Add(new(title, label, error.Message, mime));
            }
        }

        string noteOutput = Path.Combine(stage, "note.pdf");
        string body = HtmlPaperText.Read(Element(note, "content") ?? "");
        TextPdfWriter.Write(noteOutput, NoteLines(title, body, labels), cancellationToken);
        if (resources.Count == 0) return noteOutput;

        using var packet = new PdfDocument();
        foreach (string path in resources.Prepend(noteOutput))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var part = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            packet.Pages.InsertRange(packet.PageCount, part, 0, part.PageCount, AnnotationCopyingType.DoNotCopy);
        }
        string result = Path.Combine(stage, "note-and-resources.pdf");
        packet.Save(result);
        return result;
    }

    private static IEnumerable<string> NoteLines(string title, string body, IReadOnlyList<string> resources)
    {
        yield return title;
        yield return "";
        using var noteText = new StringReader(body);
        while (noteText.ReadLine() is { } line) yield return line;
        if (resources.Count == 0) yield break;
        yield return "";
        yield return "Resources";
        foreach (string label in resources) yield return label;
    }

    private static string? Element(XElement source, string name) => source.Element(name)?.Value;
}
