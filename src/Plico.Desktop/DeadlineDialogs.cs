using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Plico.Core;
using System.Globalization;
using static Plico.Desktop.DocumentMetadataDialog;

namespace Plico.Desktop;

internal sealed record DeadlineEditInput(DocumentSummary Document, IReadOnlyList<DocumentDeadline> Dates, IReadOnlyList<DateOnly> SuggestedDueDates);

internal sealed class DeadlineEditor : Window
{
    private const string DateFormat = "yyyy-MM-dd";
    /// <summary>The date field's hint, in the words a person reads rather than a format string.</summary>
    private const string DatePlaceholder = "YYYY-MM-DD";
    private readonly List<(DeadlineKind Kind, TextBox Date, TextBox Lead)> _editors = [];
    private readonly TextBlock _status = DialogLayout.Message();

    internal DeadlineEditor(DeadlineEditInput input)
    {
        Title = DesktopText.Translate("Document deadlines");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var fields = new StackPanel { Spacing = 12 };
        var dates = input.Dates.ToDictionary(item => item.Kind);
        foreach (var kind in Enum.GetValues<DeadlineKind>())
        {
            var previous = dates.GetValueOrDefault(kind);
            string label = Label(kind);
            var date = CreateTextBox(label, previous?.Date.ToString(DateFormat, CultureInfo.InvariantCulture), DatePlaceholder);
            date.Name = $"DeadlineDate{kind}";
            var lead = CreateTextBox(DesktopText.Format($"{label} reminder"), previous?.LeadDays?.ToString(CultureInfo.CurrentCulture),
                DesktopText.Translate("Days before"));
            // Labels sit beside their fields: a label column as wide as the longest label, then the date and the lead time.
            var row = new Grid { ColumnDefinitions = new("150,*,120"), ColumnSpacing = 10 };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(date, 1);
            Grid.SetColumn(lead, 2);
            row.Children.Add(date);
            row.Children.Add(lead);
            fields.Children.Add(row);
            _editors.Add((kind, date, lead));
        }
        if (input.SuggestedDueDates.Count > 0 && !dates.ContainsKey(DeadlineKind.Due))
        {
            var suggested = input.SuggestedDueDates[0];
            var button = DialogLayout.Secondary(DesktopText.Format($"Use suggested due date {DisplayDates.Day(suggested)}"));
            button.Click += (_, _) => _editors.First(item => item.Kind == DeadlineKind.Due).Date.Text =
                suggested.ToString(DateFormat, CultureInfo.InvariantCulture);
            fields.Children.Add(button);
        }
        var save = DialogLayout.Primary(DesktopText.Translate("Save"), isDefault: true);
        save.Name = "SaveDeadlinesButton";
        save.Click += (_, _) => Save();
        var cancel = DialogLayout.Secondary(DesktopText.Translate("Cancel"), isCancel: true);
        cancel.Click += (_, _) => Close();
        DialogLayout.Compose(this, DesktopText.Format($"Dates for “{input.Document.Title}”"),
            DesktopText.Translate("Plico reminds you the chosen number of days before each date."),
            new ScrollViewer { Content = fields }, _status, cancel, save);
    }

    private void Save()
    {
        var result = new List<DocumentDeadline>(_editors.Count);
        foreach (var (kind, date, lead) in _editors)
        {
            if (string.IsNullOrWhiteSpace(date.Text) && string.IsNullOrWhiteSpace(lead.Text)) continue;
            if (!DateOnly.TryParseExact(date.Text?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                _status.Text = DesktopText.Format($"Enter {Label(kind).ToLowerInvariant()} as YYYY-MM-DD.");
                date.Focus();
                return;
            }
            int? days = null;
            if (!string.IsNullOrWhiteSpace(lead.Text))
            {
                if (!int.TryParse(lead.Text, out int value) || value is < 0 or > DocumentDeadline.MaximumLeadDays)
                {
                    _status.Text = DesktopText.Format($"Enter a lead time from 0 to {DocumentDeadline.MaximumLeadDays}.");
                    lead.Focus();
                    return;
                }
                days = value;
            }
            result.Add(new(kind, day, days));
        }
        Close(result.ToArray());
    }

    internal static string Label(DeadlineKind kind) => kind switch
    {
        DeadlineKind.Due => DesktopText.Translate("Due date"),
        DeadlineKind.Renewal => DesktopText.Translate("Renewal"),
        DeadlineKind.Expiry => DesktopText.Translate("Expiry"),
        DeadlineKind.KeepUntil => DesktopText.Translate("Keep until"),
        _ => ""
    };
}

internal enum ReminderAction { Open, Dismiss, Export }
internal sealed record ReminderChoice(ReminderAction Action, DueReminder? Reminder);

internal sealed class RemindersDialog : Window
{
    internal RemindersDialog(IReadOnlyList<DueReminder> reminders)
    {
        Title = DesktopText.Translate("Reminders");
        Width = 620;
        Height = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox
        {
            Classes = { "plain", "framed" },
            ItemsSource = reminders.Select(reminder => new MetadataOption<DueReminder>(
                $"{DisplayDates.Day(reminder.Deadline.Date)} · {DeadlineEditor.Label(reminder.Deadline.Kind)} · {reminder.Title}", reminder)).ToArray(),
            SelectedIndex = reminders.Count > 0 ? 0 : -1
        };
        var close = DialogLayout.Secondary(DesktopText.Translate("Close"), isCancel: true);
        close.Click += (_, _) => Close();
        var open = DialogLayout.Secondary(DesktopText.Translate("Open PDF"));
        open.IsEnabled = reminders.Count > 0;
        var dismiss = DialogLayout.Secondary(DesktopText.Translate("Dismiss"));
        dismiss.IsEnabled = reminders.Count > 0;
        var calendar = DialogLayout.Secondary(DesktopText.Translate("Export calendar…"));
        open.Click += (_, _) => Choose(ReminderAction.Open);
        dismiss.Click += (_, _) => Choose(ReminderAction.Dismiss);
        calendar.Click += (_, _) => Close(new ReminderChoice(ReminderAction.Export, null));
        list.SelectionChanged += (_, _) => open.IsEnabled = dismiss.IsEnabled = list.SelectedIndex >= 0;
        Control body = reminders.Count == 0
            ? DialogLayout.EmptyState("Icon.Upcoming", DesktopText.Translate("Nothing upcoming"), DesktopText.Translate("No reminders are due."))
            : list;
        DialogLayout.Compose(this, Title, body, null, open, dismiss, calendar, close);
        void Choose(ReminderAction action)
        {
            if (list.SelectedItem is MetadataOption<DueReminder> item) Close(new ReminderChoice(action, item.Value));
        }
    }
}
