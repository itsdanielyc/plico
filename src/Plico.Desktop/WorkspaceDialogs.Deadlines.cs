using Plico.Core;

namespace Plico.Desktop;

internal partial interface IWorkspaceDialogs
{
    Task<IReadOnlyList<DocumentDeadline>?> EditDeadlinesAsync(DeadlineEditInput input);
    Task<ReminderChoice?> ReadRemindersAsync(IReadOnlyList<DueReminder> reminders);
}

internal sealed partial class WorkspaceDialogs
{
    public Task<IReadOnlyList<DocumentDeadline>?> EditDeadlinesAsync(DeadlineEditInput input) =>
        new DeadlineEditor(input).ShowDialog<IReadOnlyList<DocumentDeadline>?>(owner);

    public Task<ReminderChoice?> ReadRemindersAsync(IReadOnlyList<DueReminder> reminders) =>
        new RemindersDialog(reminders).ShowDialog<ReminderChoice?>(owner);
}

internal sealed partial class FixtureDialogs
{
    internal Func<DeadlineEditInput, Task<IReadOnlyList<DocumentDeadline>?>>? DeadlineChoice { get; set; }
    internal Func<IReadOnlyList<DueReminder>, Task<ReminderChoice?>>? RemindersChoice { get; set; }

    public Task<IReadOnlyList<DocumentDeadline>?> EditDeadlinesAsync(DeadlineEditInput input) =>
        DeadlineChoice?.Invoke(input) ?? Task.FromResult<IReadOnlyList<DocumentDeadline>?>(null);

    public Task<ReminderChoice?> ReadRemindersAsync(IReadOnlyList<DueReminder> reminders) =>
        RemindersChoice?.Invoke(reminders) ?? Task.FromResult<ReminderChoice?>(null);
}
