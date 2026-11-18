using Plico.Core;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task PasswordExportRequiresThePasswordAndKeepsTheOriginalSearchableText()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var original = await library.ImportAsync(Fixture(Letter), token);
        const string password = "Headless test passphrase 42";
        string export = await library.ExportPdfAsync(original.Id, destination.Path,
            new PdfExportOptions(PdfExportFormat.PasswordProtected, Password: password), token);

        Assert.Throws<InvalidDataException>(() => DocumentProcessor.ReadPdf(export, token));
        using var opened = PdfDocument.Open(export, new ParsingOptions { Password = password });
        Assert.Contains(LetterKeyword, DocumentProcessor.ReadPageText(opened.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(library.ResolvePdf(original)));
    }

    [Fact]
    public async Task MetadataCopyWritesTagsAndLeavesSourceBytesAlone()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var original = await library.ImportAsync(Fixture(Letter), token);
        library.AddTag(original.Id, "Property & repairs");
        library.SetTitle(original.Id, "Title <with markup>");

        string export = await library.ExportPdfAsync(original.Id, destination.Path, new PdfExportOptions(PdfExportFormat.WithMetadata), token);

        using var opened = PdfDocument.Open(export);
        Assert.True(opened.TryGetXmpMetadata(out var metadata));
        var xml = metadata!.GetXDocument();
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        Assert.Contains(xml.Descendants(rdf + "li"), item => item.Value == "Property & repairs");
        Assert.Contains(LetterKeyword, DocumentProcessor.ReadPdf(export, token).Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(library.ResolvePdf(original)));
    }
}
