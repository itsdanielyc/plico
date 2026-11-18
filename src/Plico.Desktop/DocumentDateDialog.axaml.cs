using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>An accepted date edit. An empty dialog result means the editor was closed.</summary>
internal sealed record DocumentDateEdit(DateOnly? Date);

/// <summary>Edits the calendar date printed on a document.</summary>
internal sealed partial class DocumentDateDialog : Window
{
    private const string DateFormat = "yyyy-MM-dd";

    internal DocumentDateDialog(DateOnly? currentDate)
    {
        InitializeComponent();
        DocumentDateBox.Text = currentDate?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DocumentDateBox.Focus();
        DocumentDateBox.SelectAll();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        string value = DocumentDateBox.Text?.Trim() ?? "";
        if (value.Length == 0)
        {
            Close(new DocumentDateEdit(null));
            return;
        }
        if (DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Close(new DocumentDateEdit(date));
            return;
        }
        DateValidation.Text = "Enter a valid date in YYYY-MM-DD format.";
        DateValidation.IsVisible = true;
        DocumentDateBox.Focus();
    }
}
