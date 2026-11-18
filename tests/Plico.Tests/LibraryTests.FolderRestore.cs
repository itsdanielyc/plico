using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    private const string FirstFolderRestoreName = "A statement.pdf";
    private const string SecondFolderRestoreName = "B statement.pdf";

    [Fact]
    public async Task FolderRestoreMatchesRenamedNestedCopiesAndKeepsUnmatchedPaths()
    {
        const string receipt = "C receipt.pdf";
        const string unmatched = "D unmatched.pdf";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string first = CopyRestoreFixture(library, Letter, FirstFolderRestoreName);
        string second = CopyRestoreFixture(library, Letter, SecondFolderRestoreName);
        string scan = CopyRestoreFixture(library, "Scanned Receipt.pdf", receipt);
        string missing = CopyRestoreFixture(library, "Match Ledger.pdf", unmatched);
        Assert.Equal(4, library.Refresh(token).Indexed);
        var originals = library.Find(new DocumentFilter(), token).Items.ToDictionary(document => document.RelativePath);
        library.AddTag(originals[FirstFolderRestoreName].Id, "Housing");
        library.AddTag(originals[SecondFolderRestoreName].Id, "Shared");
        library.SetReviewed(originals[receipt].Id, true);

        string backup = Directory.CreateDirectory(Path.Combine(temporary.Path, "Backup")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(backup, "Old names")).FullName;
        string renamed = Path.Combine(nested, "Statement archive.PDF");
        File.Move(first, renamed);
        File.Move(scan, Path.Combine(backup, "Receipt archive.pdf"));
        File.Delete(second);
        byte[] changed = File.ReadAllBytes(missing);
        changed[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(backup, "Changed ledger.pdf"), changed);
        File.Delete(missing);
        Assert.Equal(4, library.Refresh(token).UnavailablePaths.Count);

        var result = await library.RestoreUnavailablePdfsFromFolderAsync(backup, cancellationToken: token);

        Assert.Equal(3, result.Restored);
        Assert.Equal(1, result.Unmatched);
        Assert.Empty(result.Issues);
        Assert.False(result.Cancelled);
        Assert.Equal(unmatched, Assert.Single(library.ReadUnavailablePaths(token)));
        Assert.Equal(File.ReadAllBytes(renamed), File.ReadAllBytes(first));
        Assert.Equal(File.ReadAllBytes(renamed), File.ReadAllBytes(second));
        Assert.Equal(originals[FirstFolderRestoreName].Id, Assert.Single(library.Find(new DocumentFilter(Tag: "Housing"), token).Items).Id);
        Assert.Equal(originals[SecondFolderRestoreName].Id, Assert.Single(library.Find(new DocumentFilter(Tag: "Shared"), token).Items).Id);
        var restoredReceipt = Assert.Single(library.Find(new DocumentFilter(), token).Items, document => document.RelativePath == receipt);
        Assert.Equal(originals[receipt].Id, restoredReceipt.Id);
        Assert.True(restoredReceipt.Reviewed);
    }

    [Fact]
    public async Task FolderRestoreContinuesPastAnOccupiedPath()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string first = CopyRestoreFixture(library, Letter, FirstFolderRestoreName);
        string second = CopyRestoreFixture(library, Letter, SecondFolderRestoreName);
        library.Refresh(token);
        string backup = Directory.CreateDirectory(Path.Combine(temporary.Path, "Backup")).FullName;
        File.Move(first, Path.Combine(backup, "Copy.pdf"));
        File.Delete(second);
        library.Refresh(token);
        const string occupant = "Existing file at the first path";
        File.WriteAllText(first, occupant);

        var result = await library.RestoreUnavailablePdfsFromFolderAsync(backup, cancellationToken: token);

        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Unmatched);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FirstFolderRestoreName, issue.Path);
        Assert.Contains("already occupies", issue.Reason, StringComparison.Ordinal);
        Assert.Equal(occupant, File.ReadAllText(first));
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(second));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FolderRestoreKeepsTheFirstPdfAcrossCancellationOrAChangedCopy(bool cancel)
    {
        var token = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string first = CopyRestoreFixture(library, Letter, FirstFolderRestoreName);
        string second = CopyRestoreFixture(library, Letter, SecondFolderRestoreName);
        library.Refresh(token);
        string backup = Directory.CreateDirectory(Path.Combine(temporary.Path, "Backup")).FullName;
        string copy = Path.Combine(backup, "Copy.pdf");
        File.Move(first, copy);
        File.Delete(second);
        library.Refresh(token);
        var progress = new InlineRestoreProgress(restored =>
        {
            if (restored != 1) return;
            if (cancel) cancellation.Cancel();
            else
            {
                byte[] bytes = File.ReadAllBytes(copy);
                bytes[^1] ^= 1;
                File.WriteAllBytes(copy, bytes);
            }
        });

        var result = await library.RestoreUnavailablePdfsFromFolderAsync(backup, progress, cancellation.Token);

        Assert.Equal(1, result.Restored);
        Assert.Equal(cancel, result.Cancelled);
        Assert.True(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal(SecondFolderRestoreName, Assert.Single(library.ReadUnavailablePaths(token)));
        if (cancel) Assert.Empty(result.Issues);
        else Assert.Contains("does not match", Assert.Single(result.Issues).Reason, StringComparison.Ordinal);
    }

    private static string CopyRestoreFixture(InboxLibrary library, string fixture, string name)
    {
        string path = Path.Combine(library.PdfDirectory, name);
        File.Copy(Fixture(fixture), path);
        return path;
    }

    private sealed class InlineRestoreProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
