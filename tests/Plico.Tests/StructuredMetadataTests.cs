using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class StructuredMetadataTests
{
    [Fact]
    public async Task StoresTypedMetadataAndTagAppearanceAcrossReopen()
    {
        using var directory = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        long documentId;
        DocumentMetadata expected;
        byte[] originalBytes;
        using (var library = InboxLibrary.Open(directory.Path))
        {
            var document = await library.ImportAsync(Fixture("Lease Renewal.pdf"), token);
            documentId = document.Id;
            originalBytes = File.ReadAllBytes(library.ResolvePdf(document));
            var contact = library.SaveCorrespondent(null, "Northstar Properties", ["Northstar"]);
            var type = library.SaveDocumentType(null, "Lease");
            var field = library.SaveCustomField(null, "Deposit", CustomFieldKind.Money);
            library.AddTag(documentId, "Tenancy");
            library.SaveTagAppearance("Tenancy", "Home", "#6B2ED0");
            expected = new(type.Id, [contact.Id], "Renew each March.", [new(field.Id, new CustomFieldValue.Money(350.25m, "GBP"))]);
            library.SaveDocumentMetadata(documentId, expected, token);
        }

        using var reopened = InboxLibrary.Open(directory.Path);
        var persisted = reopened.ReadDocumentMetadata(documentId);
        Assert.Equal(expected.DocumentTypeId, persisted.DocumentTypeId);
        Assert.Equal(expected.CorrespondentIds, persisted.CorrespondentIds);
        Assert.Equal(expected.Notes, persisted.Notes);
        Assert.Equal(expected.Fields, persisted.Fields);
        Assert.Equal("Northstar Properties", Assert.Single(reopened.SuggestCorrespondents(documentId, token)).Name);
        Assert.Equal(new TagAppearance("Tenancy", "Home", "#6B2ED0"), reopened.GetTagAppearances().Single(tag => tag.Label == "Tenancy"));
        Assert.Equal(originalBytes, File.ReadAllBytes(reopened.ResolvePdf(Assert.Single(reopened.Find(new(), token).Items))));
    }

    [Fact]
    public async Task RejectsInvalidMetadataAsOneEditAndPreservesTagsWhenRenaming()
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        var token = TestContext.Current.CancellationToken;
        var document = await library.ImportAsync(Fixture("Lease Renewal.pdf"), token);
        var field = library.SaveCustomField(null, "Term", CustomFieldKind.Choice, ["Short", "Long"]);
        library.SaveDocumentMetadata(document.Id, new(null, [], "Original", [new(field.Id, new CustomFieldValue.Choice("Long"))]), token);
        Assert.Throws<ArgumentException>(() => library.SaveDocumentMetadata(document.Id,
            new(null, [], "Changed", [new(field.Id, new CustomFieldValue.Choice("Unknown"))]), token));
        Assert.Equal("Original", library.ReadDocumentMetadata(document.Id).Notes);
        library.AddTag(document.Id, "Lease");
        library.SaveTagAppearance("Lease", "Household", "#172A45");
        Assert.Throws<ArgumentException>(() => library.SaveTagAppearance("Household", "Lease", null));
        library.RenameTag("Lease", "Tenancy", cancellationToken: token);
        Assert.Contains(new TagAppearance("Tenancy", "Household", "#172A45"), library.GetTagAppearances());
    }

    [Fact]
    public async Task ArchiveSerialStaysReservedInTrashUntilPurge()
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        var token = TestContext.Current.CancellationToken;
        var lease = await library.ImportAsync(Fixture("Lease Renewal.pdf"), token);
        var other = await library.ImportAsync(Fixture("Match Ledger.pdf"), token);
        library.SetArchiveSerialNumber(lease.Id, 451);
        await library.TrashPdfAsync(lease.Id, token);
        Assert.Null(library.FindByArchiveSerialNumber(451));
        Assert.Equal(451L, library.ReadArchiveSerialNumber(lease.Id));
        Assert.Throws<ArgumentException>(() => library.SetArchiveSerialNumber(other.Id, 451));
        await library.PurgeTrashedPdfAsync(lease.Id, token);
        library.SetArchiveSerialNumber(other.Id, 451);
        Assert.Equal(other.Id, library.FindByArchiveSerialNumber(451)?.Id);
    }

    [Fact]
    public async Task ReplaysAnInterruptedMigrationBySourceKeyAndPreservesLocalEdits()
    {
        using var directory = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string original = Fixture("Lease Renewal.pdf");
        var key = new MigrationSourceKey("paperless-ngx", "export-1", "document-72");
        var imported = new ImportedDocumentMetadata("Imported lease", new(2026, 10, 7), true,
            [new(["Property", "Leases"], "#A36F41")], [new("Northstar", [])], "Tenancy", "Original export", [], 72);
        using (var interrupted = InboxLibrary.Open(directory.Path, (source, target, allowHardLink) =>
        {
            if (!PdfFilePublication.TryClaim(source, target, allowHardLink)) return false;
            throw new IOException("Synthetic process exit after publication.");
        }))
        {
            await Assert.ThrowsAsync<IOException>(() => interrupted.ImportMigratedFileAsync(original, key, imported, token));
        }

        using var library = InboxLibrary.Open(directory.Path);
        var recovered = await library.ImportMigratedFileAsync(Fixture("Match Ledger.pdf"), key, imported, token);
        Assert.True(recovered.Added);
        Assert.Empty(recovered.Conflicts);
        Assert.True(recovered.Document.Reviewed);
        Assert.Equal("Imported lease", recovered.Document.Title);
        Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(library.ResolvePdf(recovered.Document)));
        var assigned = library.ReadDocumentMetadata(recovered.Document.Id);
        library.SaveDocumentMetadata(recovered.Document.Id, assigned with { Notes = "Local edit" }, token);
        var repeated = await library.ImportMigratedFileAsync(original, key, imported, token);
        Assert.False(repeated.Added);
        Assert.Equal(recovered.Document.Id, repeated.Document.Id);
        Assert.Equal("Local edit", library.ReadDocumentMetadata(repeated.Document.Id).Notes);
        Assert.Equal("Notes", Assert.Single(repeated.Conflicts).Field);
        Assert.Single(library.Find(new(), token).Items);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);
}
