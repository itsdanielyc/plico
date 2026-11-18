using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void IndexRefreshReportsNewlyUnavailableFilesAndPreservesRecoveredRecords()
    {
        const string receipt = "Scanned Receipt.pdf";
        const string invalid = "Broken arrival.pdf";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string letterPath = Path.Combine(library.PdfDirectory, Letter);
        string receiptPath = Path.Combine(library.PdfDirectory, receipt);
        string invalidPath = Path.Combine(library.PdfDirectory, invalid);
        string heldLetter = Path.Combine(temporary.Path, "held letter.pdf");
        File.Copy(Fixture(Letter), letterPath);
        File.Copy(Fixture(receipt), receiptPath);
        Assert.Equal(2, library.Refresh(token).Indexed);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var letter = Assert.Single(documents, document => document.RelativePath == Letter);
        var scan = Assert.Single(documents, document => document.RelativePath == receipt);
        var date = new DateOnly(2024, 2, 29);
        library.AddTag(letter.Id, "Housing");
        library.SetDocumentDate(letter.Id, date);
        library.SetReviewed(letter.Id, true);
        library.AddTag(scan.Id, "Workshop");

        File.Move(letterPath, heldLetter);
        File.WriteAllText(receiptPath, "This PDF was damaged.");
        File.WriteAllText(invalidPath, "This arrival is unreadable.");
        var first = library.Refresh(token);

        Assert.Equal(new[] { Letter, receipt }, first.UnavailablePaths.OrderBy(path => path, StringComparer.Ordinal));
        Assert.Equal(2, first.Warnings.Count);
        Assert.Contains(first.Warnings, message => message.StartsWith($"{receipt} —", StringComparison.Ordinal));
        Assert.Contains(first.Warnings, message => message.StartsWith($"{invalid} —", StringComparison.Ordinal));
        Assert.Empty(library.Find(new DocumentFilter(), token).Items);
        var repeated = library.Refresh(token);
        Assert.Empty(repeated.UnavailablePaths);
        Assert.Equal(2, repeated.Warnings.Count);
        Assert.Equal(2, library.CountUnavailableDocuments());
        Assert.Equal(new[] { Letter, receipt }, library.ReadUnavailablePaths(token));
        Assert.Throws<OperationCanceledException>(() => library.ReadUnavailablePaths(new CancellationToken(canceled: true)));

        File.Move(heldLetter, letterPath);
        File.Copy(Fixture(receipt), receiptPath, overwrite: true);
        File.Delete(invalidPath);
        var recovered = library.Refresh(token);
        Assert.Empty(recovered.UnavailablePaths);
        Assert.Empty(recovered.Warnings);
        Assert.Equal(0, library.CountUnavailableDocuments());
        Assert.Empty(library.ReadUnavailablePaths(token));
        var restoredLetter = Assert.Single(library.Find(new DocumentFilter(Tag: "Housing"), token).Items);
        Assert.Equal(letter.Id, restoredLetter.Id);
        Assert.Equal(date, restoredLetter.DocumentDate);
        Assert.True(restoredLetter.Reviewed);
        Assert.Equal(scan.Id, Assert.Single(library.Find(new DocumentFilter(Tag: "Workshop"), token).Items).Id);
    }
}
