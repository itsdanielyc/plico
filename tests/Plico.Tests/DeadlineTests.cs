using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class DeadlineTests
{
    [Fact]
    public async Task RemindersAppearOnTheirLocalDateAndCalendarIdsSurviveEdits()
    {
        using var domain = new TemporaryLibrary();
        using var exports = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        using var library = InboxLibrary.Open(domain.Path);
        var document = await library.ImportAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf"), token);
        library.SetTitle(document.Id, "Lease, Unit 7");
        var due = new DateOnly(2026, 11, 26);
        library.SaveDeadlines(document.Id, [new(DeadlineKind.Due, due, 3), new(DeadlineKind.KeepUntil, due.AddYears(2), null)], token);
        Assert.Empty(library.ReadDueReminders(due.AddDays(-4), token));
        var current = Assert.Single(library.ReadDueReminders(due.AddDays(-3), token));
        Assert.Equal(document.Id, current.DocumentId);

        string firstPath = await library.ExportDeadlinesAsync(exports.Path, [document.Id], token);
        string first = File.ReadAllText(firstPath);
        Assert.Contains("DTSTART;VALUE=DATE:20261126\r\n", first);
        Assert.Contains("TRIGGER:-P3D\r\n", first);
        Assert.Contains("Lease\\, Unit 7", first);

        library.DismissReminder(document.Id, DeadlineKind.Due);
        Assert.Empty(library.ReadDueReminders(due, token));
        library.SaveDeadlines(document.Id, [new(DeadlineKind.Due, due.AddDays(1), 3)], token);
        Assert.Single(library.ReadDueReminders(due, token));
        string changed = File.ReadAllText(await library.ExportDeadlinesAsync(exports.Path, [document.Id], token));
        Assert.Equal(first.Split("\r\n").First(line => line.StartsWith("UID:", StringComparison.Ordinal)),
            changed.Split("\r\n").Single(line => line.StartsWith("UID:", StringComparison.Ordinal)));
        Assert.Equal(2, Directory.GetFiles(exports.Path, "*.ics").Length);
    }
}
