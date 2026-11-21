using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ArchiveNumberLookupKeepsItsSearchScopeBeyondTheFirstPage()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        SeedReviewQueue(temporary.Path, InboxLibrary.DefaultResultPageSize + 1, token);
        long oldestId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            oldestId = Assert.Single(library.Find(new(Offset: InboxLibrary.DefaultResultPageSize), token).Items).Id;
            library.SetArchiveSerialNumber(oldestId, 470);
        }
        var dialogs = new FixtureDialogs { ArchiveSerialChoice = new(470, ScanPaper.Letter, 1) };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        await WaitForPreviewAsync(model);
        await model.FindArchiveSerialCommand.ExecuteAsync(null);
        await WaitForPreviewAsync(model);
        Assert.Equal(oldestId, Assert.Single(model.Documents).Id);
        Assert.Equal("asn:470", model.Query);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        Assert.Equal(oldestId, Assert.Single(model.Documents).Id);
        await WaitForPreviewAsync(model);
    }

    [AvaloniaFact]
    public async Task EditingAnArchiveNumberRemovesTheDocumentFromItsOldSearch()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
            library.SetArchiveSerialNumber(library.Find(new(), token).Items[0].Id, 470);
        }
        var dialogs = new FixtureDialogs { ArchiveSerialChoice = new(471, ScanPaper.Letter, 1) };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        await WaitForPreviewAsync(model);
        model.Query = "asn:470";
        await WaitForAsync(() => model.Documents.Count == 1);
        await WaitForPreviewAsync(model);
        await model.EditArchiveSerialCommand.ExecuteAsync(null);
        Assert.Empty(model.Documents);
        model.Query = "asn:471";
        await WaitForAsync(() => model.Documents.Count == 1);
        await WaitForPreviewAsync(model);
    }
}
