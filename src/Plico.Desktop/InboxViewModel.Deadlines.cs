using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using System.Globalization;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private static readonly TimeSpan ReminderPollingInterval = TimeSpan.FromMinutes(1);
    private DispatcherTimer? _reminderTimer;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReminderMenuTitle))]
    private long _dueReminderCount;

    public string ReminderMenuTitle => DueReminderCount == 0 ? DesktopText.Translate("Upcoming") : DesktopText.Format($"Upcoming ({DueReminderCount})");

    private void PrepareReminderPolling()
    {
        RefreshDueReminderCount();
        if (_reminderTimer is not null) return;
        _reminderTimer = new DispatcherTimer { Interval = ReminderPollingInterval };
        _reminderTimer.Tick += (_, _) =>
        {
            if (IsBusy || _library is null) return;
            long previous = DueReminderCount;
            try
            {
                RefreshDueReminderCount();
                if (DueReminderCount > previous) Status = DesktopText.Format($"{DueReminderCount} document reminders are due.");
            }
            catch (Exception error) when (error is IOException or Microsoft.Data.Sqlite.SqliteException)
            {
                Status = error.Message;
            }
        };
        _reminderTimer.Start();
    }

    private void RefreshDueReminderCount() => DueReminderCount = _library?.CountDueReminders(LocalReminderDate()) ?? 0;

    private static DateOnly LocalReminderDate() => DateOnly.FromDateTime(DateTime.Today);

    [RelayCommand]
    private Task EditDeadlinesAsync() => RunOperationAsync(DesktopText.Translate("Edit document deadlines…"), async token =>
    {
        if (SelectedDocument is not { } document) return;
        var library = RequireLibrary();
        var dates = await Task.Run(() => library.ReadDeadlines(document.Id), token);
        IReadOnlyList<DateOnly> suggestions = dates.Any(item => item.Kind == DeadlineKind.Due)
            ? [] : await Task.Run(() => library.SuggestDueDates(document.Id, RecognitionLanguage, CultureInfo.CurrentCulture), token);
        var edited = await _dialogs.EditDeadlinesAsync(new(document, dates, suggestions));
        if (edited is null)
        {
            Status = DesktopText.Translate("Deadline editor closed.");
            return;
        }
        await Task.Run(() => library.SaveDeadlines(document.Id, edited, token), token);
        RetireUndo();
        RefreshDueReminderCount();
        Status = DesktopText.Translate("Reminder saved.");
    });

    [RelayCommand]
    private Task RemindersAsync() => RunOperationAsync(DesktopText.Translate("Checking document reminders…"), async token =>
    {
        var library = RequireLibrary();
        var reminders = await Task.Run(() => library.ReadDueReminders(LocalReminderDate(), token), token);
        var choice = await _dialogs.ReadRemindersAsync(reminders);
        switch (choice)
        {
            case { Action: ReminderAction.Dismiss, Reminder: { } dismissed }:
                library.DismissReminder(dismissed.DocumentId, dismissed.Deadline.Kind);
                // A reminder is part of a document, so Undo can no longer reach past this change, as after saving one.
                RetireUndo();
                RefreshDueReminderCount();
                Status = DesktopText.Translate("Reminder dismissed.");
                break;
            case { Action: ReminderAction.Open, Reminder: { } selected }:
                _dialogs.OpenPath(library.ResolveReminderPdf(selected.DocumentId));
                Status = DesktopText.Format($"Opened {selected.Title}.");
                break;
            case { Action: ReminderAction.Export }:
                var destination = await _dialogs.PickFolderAsync(DesktopText.Translate("Choose where to save the calendar"));
                if (destination is null) { Status = DesktopText.Translate("Calendar destination not chosen."); break; }
                string path = await Task.Run(() => library.ExportDeadlinesAsync(destination, cancellationToken: token), token);
                NoteExport(Path.GetDirectoryName(path));
                Status = DesktopText.Format($"Exported {Path.GetFileName(path)}.");
                break;
            default:
                Status = DesktopText.Translate("Reminders closed.");
                break;
        }
    });
}
