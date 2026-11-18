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
    private readonly List<(DeadlineKind Kind, TextBox Date, TextBox Lead)> _editors = [];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    internal DeadlineEditor(DeadlineEditInput input)
    {
        Title = DesktopText.Translate("Document deadlines");
        Width = 530;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var fields = new StackPanel { Spacing = 12 };
        var dates = input.Dates.ToDictionary(item => item.Kind);
        foreach (var kind in Enum.GetValues<DeadlineKind>())
        {
            var previous = dates.GetValueOrDefault(kind);
            string label = Label(kind);
            var date = CreateTextBox(label, previous?.Date.ToString(DateFormat, CultureInfo.InvariantCulture), DateFormat);
            date.Name = $"DeadlineDate{kind}";
            var lead = CreateTextBox(DesktopText.Format($"{label} reminder"), previous?.LeadDays?.ToString(CultureInfo.CurrentCulture),
                DesktopText.Translate("Days before"));
            var row = new Grid { ColumnDefinitions = new("*,130,130"), ColumnSpacing = 10 };
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
            var button = new Button { Content = DesktopText.Format($"Use suggested due date {suggested:d}") };
            button.Click += (_, _) => _editors.First(item => item.Kind == DeadlineKind.Due).Date.Text =
                suggested.ToString(DateFormat, CultureInfo.InvariantCulture);
            fields.Children.Add(button);
        }
        var save = new Button { Name = "SaveDeadlinesButton", Content = DesktopText.Translate("Save"), IsDefault = true };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = DesktopText.Translate("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var panel = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        AddRow(panel, 0, new TextBlock { Text = input.Document.Title, FontWeight = FontWeight.SemiBold, FontSize = 17, TextWrapping = TextWrapping.Wrap });
        AddRow(panel, 1, new ScrollViewer { Content = fields });
        AddRow(panel, 2, _status);
        AddRow(panel, 3, new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal,
            Spacing = 10, Children = { cancel, save }
        });
        Content = panel;
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
        Width = 600;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox
        {
            ItemsSource = reminders.Select(reminder => new MetadataOption<DueReminder>(
                $"{reminder.Deadline.Date:d} · {DeadlineEditor.Label(reminder.Deadline.Kind)} · {reminder.Title}", reminder)).ToArray(),
            SelectedIndex = reminders.Count > 0 ? 0 : -1
        };
        var close = new Button { Content = DesktopText.Translate("Close"), IsCancel = true };
        close.Click += (_, _) => Close();
        var open = new Button { Content = DesktopText.Translate("Open PDF"), IsEnabled = reminders.Count > 0 };
        var dismiss = new Button { Content = DesktopText.Translate("Dismiss"), IsEnabled = reminders.Count > 0 };
        var calendar = new Button { Content = DesktopText.Translate("Export calendar…") };
        open.Click += (_, _) => Choose(ReminderAction.Open);
        dismiss.Click += (_, _) => Choose(ReminderAction.Dismiss);
        calendar.Click += (_, _) => Close(new ReminderChoice(ReminderAction.Export, null));
        list.SelectionChanged += (_, _) => open.IsEnabled = dismiss.IsEnabled = list.SelectedIndex >= 0;
        var grid = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 12, Margin = new Thickness(20) };
        AddRow(grid, 0, reminders.Count == 0 ? new TextBlock { Text = DesktopText.Translate("No reminders are due.") } : list);
        AddRow(grid, 1, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { open, dismiss, calendar, close }
        });
        Content = grid;
        void Choose(ReminderAction action)
        {
            if (list.SelectedItem is MetadataOption<DueReminder> item) Close(new ReminderChoice(action, item.Value));
        }
    }
}
