using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task HouseholdBinderPreservesSelectionOrderAndSearchableText()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName);
        string destination = Directory.CreateDirectory(Path.Combine(temporary.Path, "Export")).FullName;
        var token = TestContext.Current.CancellationToken;
        var lease = await library.ImportAsync(Fixture(Letter), token);
        var mixed = await library.ImportAsync(Fixture(Mixed), token);
        library.SetTitle(lease.Id, "Bail signé");
        library.SetTitle(mixed.Id, "House repairs");
        byte[] originalLease = File.ReadAllBytes(library.ResolvePdf(lease));
        byte[] originalMixed = File.ReadAllBytes(library.ResolvePdf(mixed));
        string occupied = Path.Combine(destination, "Family records.pdf");
        byte[] existing = [10, 20, 30];
        await File.WriteAllBytesAsync(occupied, existing, token);

        string result = await library.ExportHouseholdBinderAsync(
            [mixed.Id, lease.Id, mixed.Id], "Family records", destination, cancellationToken: token);

        Assert.Equal(Path.Combine(destination, "Family records (2).pdf"), result);
        Assert.Equal(existing, File.ReadAllBytes(occupied));
        using var binder = PdfDocument.Open(result);
        Assert.Equal(4, binder.NumberOfPages);
        string contents = ContentOrderTextExtractor.GetText(binder.GetPage(1));
        Assert.Contains("Family records", contents);
        Assert.Matches(@"House repairs\s*2", contents);
        Assert.Matches(@"Bail signé\s*4", contents);
        Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(binder.GetPage(2)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(binder.GetPage(3)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(binder.GetPage(4)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalLease, File.ReadAllBytes(library.ResolvePdf(lease)));
        Assert.Equal(originalMixed, File.ReadAllBytes(library.ResolvePdf(mixed)));
        Assert.Equal(2, library.Find(new DocumentFilter(), token).Items.Count);
        Assert.Empty(Directory.EnumerateDirectories(destination));
    }

    [Fact]
    public async Task HouseholdBinderDoesNotPublishAfterCancellationOrAChangingSource()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName);
        string destination = Directory.CreateDirectory(Path.Combine(temporary.Path, "Export")).FullName;
        var token = TestContext.Current.CancellationToken;
        var lease = await library.ImportAsync(Fixture(Letter), token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ExportHouseholdBinderAsync(
            [lease.Id], "Cancelled", destination, new InlinePdfProgress(_ => cancellation.Cancel()), cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => library.ExportHouseholdBinderAsync(
            [lease.Id], "Invalid destination", library.PdfDirectory, cancellationToken: token));

        string source = library.ResolvePdf(lease);
        await Assert.ThrowsAsync<IOException>(() => library.ExportHouseholdBinderAsync(
            [lease.Id], "Changed", destination,
            new InlinePdfProgress(_ => File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddMinutes(1))), token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }
}
