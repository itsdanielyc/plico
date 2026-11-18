using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using System.Globalization;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyDeadlineRemindersAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidence)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        dialogs.DeadlineChoice = async input =>
        {
            var editor = new DeadlineEditor(input);
            var result = editor.ShowDialog<IReadOnlyList<DocumentDeadline>?>(window);
            try
            {
                await UntilAsync(() => editor.GetVisualDescendants().OfType<TextBox>().Any(box => box.Name == "DeadlineDateDue"),
                    () => "Deadline editor did not open.");
                var date = editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "DeadlineDateDue");
                date.Text = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                editor.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Due date reminder").Text = "0";
                await CaptureAsync(editor, evidence, "document-deadlines.png");
                editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "SaveDeadlinesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return await result;
            }
            finally { if (editor.IsVisible) editor.Close(); }
        };
        try
        {
            var edit = (IAsyncRelayCommand)Control<Button>(window, "EditDeadlinesButton").Command!;
            await edit.ExecuteAsync(null);
            Require(model.DueReminderCount == 1, "The saved deadline did not appear as a reminder.");
            dialogs.RemindersChoice = reminders =>
            {
                Require(reminders.Single().Deadline.Date == today, "The reminder has the wrong date.");
                return Task.FromResult<ReminderChoice?>(new(ReminderAction.Dismiss, reminders.Single()));
            };
            await model.RemindersCommand.ExecuteAsync(null);
            Require(model.DueReminderCount == 0, "The reminder remained active after dismissal.");
        }
        finally
        {
            dialogs.DeadlineChoice = null;
            dialogs.RemindersChoice = null;
        }
    }
}
