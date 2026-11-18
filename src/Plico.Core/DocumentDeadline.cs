namespace Plico.Core;

/// <summary>The purpose of a calendar date associated with a document.</summary>
public enum DeadlineKind { Due, Renewal, Expiry, KeepUntil }

/// <summary>A calendar date and the optional number of days before it that a reminder should become active.</summary>
public sealed record DocumentDeadline(DeadlineKind Kind, DateOnly Date, int? LeadDays)
{
    /// <summary>The largest supported lead time for one reminder.</summary>
    public const int MaximumLeadDays = 3_650;

    /// <summary>The first day on which an enabled reminder is due.</summary>
    public DateOnly? ReminderDate => LeadDays is { } lead ? DateOnly.FromDayNumber(Math.Max(0, Date.DayNumber - lead)) : null;
}

/// <summary>A document and deadline with an active, undismissed reminder.</summary>
public sealed record DueReminder(long DocumentId, string Title, DocumentDeadline Deadline);

internal sealed record CalendarDeadline(string Uid, string Title, DocumentDeadline Deadline);
