using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task PageTextIsLimitedToTheRequestedDigitalOrRecognizedPage()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.ImportAsync(Fixture(Mixed), token);
        string path = library.ResolvePdf(document);
        byte[] original = File.ReadAllBytes(path);

        string letter = library.ReadPageText(document, 1, token);
        string receipt = library.ReadPageText(document, 2, token);

        Assert.Contains(LetterKeyword, letter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ReceiptKeyword, letter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ReceiptKeyword, receipt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LetterKeyword, receipt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Throws<ArgumentOutOfRangeException>(() => library.ReadPageText(document, 0, token));
        Assert.Throws<ArgumentOutOfRangeException>(() => library.ReadPageText(document, 3, token));
    }

    [Fact]
    public void PageTextHandlesCancellationBlankPagesAndAStalePageNumber()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string path = Path.Combine(library.PdfDirectory, Mixed);
        File.Copy(Fixture(Mixed), path);
        Assert.Equal(1, library.Refresh(token).Indexed);
        var document = Assert.Single(library.Find(new DocumentFilter(), token).Items);

        Assert.Throws<OperationCanceledException>(() => library.ReadPageText(document, 1, new CancellationToken(canceled: true)));
        Assert.Equal("", library.ReadPageText(document, 2, token));
        File.Copy(Fixture(Letter), path, overwrite: true);
        var stale = Assert.Throws<InvalidDataException>(() => library.ReadPageText(document, 2, token));
        Assert.Contains("Refresh the index", stale.Message, StringComparison.Ordinal);

        library.Refresh(token);
        var refreshed = Assert.Single(library.Find(new DocumentFilter(), token).Items);
        Assert.Equal(1, refreshed.PageCount);
        Assert.Contains(LetterKeyword, library.ReadPageText(refreshed, 1, token), StringComparison.OrdinalIgnoreCase);
    }
}
