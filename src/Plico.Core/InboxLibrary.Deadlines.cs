using System.Globalization;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Reads the dates associated with the supplied catalog identity.</summary>
    public IReadOnlyList<DocumentDeadline> ReadDeadlines(long documentId) => _catalog.ReadDeadlines(documentId);

    /// <summary>Replaces the document's dates and preserves dismissal and calendar identity for unchanged entries.</summary>
    public void SaveDeadlines(long documentId, IReadOnlyList<DocumentDeadline> deadlines, CancellationToken cancellationToken = default) =>
        _catalog.SaveDeadlines(documentId, deadlines, cancellationToken);

    /// <summary>Lists present documents whose configured alarm has reached its first day.</summary>
    public IReadOnlyList<DueReminder> ReadDueReminders(DateOnly today, CancellationToken cancellationToken = default) =>
        _catalog.ReadDueReminders(today, cancellationToken);

    /// <summary>Counts active reminders without reading document titles or contents.</summary>
    public long CountDueReminders(DateOnly today) => _catalog.CountDueReminders(today);

    /// <summary>Marks a reminder as seen until its date or lead time changes.</summary>
    public bool DismissReminder(long documentId, DeadlineKind kind) => _catalog.DismissReminder(documentId, kind);

    /// <summary>Resolves an active reminder's PDF using its current location.</summary>
    public string ResolveReminderPdf(long documentId) => ResolvePdf(_catalog.Get(documentId));

    /// <summary>Finds possible due dates in the document's indexed text.</summary>
    public IReadOnlyList<DateOnly> SuggestDueDates(long documentId, OcrLanguage language, CultureInfo culture)
    {
        var input = _catalog.ReadFilingInputs([documentId], CancellationToken.None).Single();
        return FilingFieldExtractor.Read(input.Text, FilingFieldExtractor.DateCulture(input.Language ?? language, culture)).DueDates;
    }

    /// <summary>Writes selected dates and alarms as a standard iCalendar file. A null selection exports the entire present library.</summary>
    public Task<string> ExportDeadlinesAsync(string destination, IReadOnlyList<long>? documentIds = null,
        CancellationToken cancellationToken = default) =>
        ExportReportAsync(destination, "Plico reminders", ".ics",
            (file, token) => CalendarFile.Write(file, _catalog.ReadCalendarDeadlines(documentIds, token), DateTimeOffset.UtcNow, token),
            cancellationToken);
}
