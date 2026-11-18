using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Theory]
    [InlineData("Paperwork.TXT", "PAYMENT470\nAmount £42.80\nÜberweisung")]
    [InlineData("Paperwork.MD", "# PAYMENT470\n\nAmount **£42.80**\n\n- Überweisung")]
    public async Task ImportsTextAndMarkdownAsSearchablePdfWithExactDuplicateReuse(string filename, string body)
    {
        using var temporary = new TemporaryLibrary();
        string source = Path.Combine(temporary.Path, filename);
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(source, body, token);
        using var library = InboxLibrary.Open(temporary.Path);

        Assert.True(InboxLibrary.SupportsImport(source));
        var first = await library.ImportFileAsync(source, token);
        var duplicate = await library.ImportFileAsync(source, token);
        Assert.True(first.Added && !duplicate.Added);
        Assert.Equal(first.Document.Id, duplicate.Document.Id);
        var text = library.ReadIndexedText(first.Document.Id);
        Assert.Contains("PAYMENT470", text);
        Assert.Contains("£42.80", text);
        Assert.Contains("Überweisung", text);
        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.Equal(body, await File.ReadAllTextAsync(source, token));
    }

    [Fact]
    public async Task ImportsEmailBodyAndReadableAttachmentAsOnePdf()
    {
        using var temporary = new TemporaryLibrary();
        string source = Path.Combine(temporary.Path, "Statement.eml");
        var token = TestContext.Current.CancellationToken;
        const string mail = """
            From: Finance <finance@example.test>
            To: Customer <recipient@example.test>
            Subject: Your statement
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="fixture"

            --fixture
            Content-Type: text/html; charset=utf-8

            <html><head><style>HIDDEN470</style></head><body><p>STATEMENT470 is ready.</p></body></html>
            --fixture
            Content-Type: text/plain; charset=utf-8
            Content-Disposition: attachment; filename="details.txt"

            ATTACHMENT470 details.
            --fixture--
            """;
        await File.WriteAllTextAsync(source, mail.ReplaceLineEndings("\r\n") + "\r\n", token);
        using var library = InboxLibrary.Open(temporary.Path);

        var result = await library.ImportFileAsync(source, token);
        Assert.Equal(2, result.Document.PageCount);
        string text = library.ReadIndexedText(result.Document.Id);
        Assert.Contains("STATEMENT470", text);
        Assert.Contains("ATTACHMENT470", text);
        Assert.DoesNotContain("HIDDEN470", text, StringComparison.Ordinal);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task RecognizesAnHeicPhoneScanAndReadsAnOutlookMessage()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;

        var image = await library.ImportFileAsync(Fixture("Phone Receipt.heic"), token);
        Assert.Equal(1, image.Document.PageCount);
        Assert.Contains("WRENCH470", library.ReadIndexedText(image.Document.Id), StringComparison.OrdinalIgnoreCase);
        var email = await library.ImportFileAsync(Fixture("Outlook import fixture.msg"), token);
        Assert.Contains("OUTLOOKFORM470", library.ReadIndexedText(email.Document.Id));
    }

    [Fact]
    public async Task AnOfficeImportUsesTheLocalHelperOrRetainsTheSourceForRetry()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        string source = Path.Combine(temporary.Path, "Office record.rtf");
        const string content = @"{\rtf1\ansi Office test document OFFICEPART470}";
        await File.WriteAllTextAsync(source, content, token);

        if (OfficePdfConverter.FindExecutable() is null)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => library.ImportFileAsync(source, token));
            Assert.Contains("Install LibreOffice", error.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf"));
        }
        else
        {
            var result = await library.ImportFileAsync(source, token);
            Assert.Contains("OFFICEPART470", library.ReadIndexedText(result.Document.Id));
        }
        Assert.Equal(content, await File.ReadAllTextAsync(source, token));
    }
}
