using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>An accepted date edit. A null date clears the stored value.</summary>
internal sealed record DocumentDateEdit(DateOnly? Date);

/// <summary>
/// Edits the recorded calendar date for one or more documents. The date is shown and typed as the interface writes it
/// ("29 Feb 2024"), and 2024-02-29 is accepted too, like the filing card's date field.
/// </summary>
internal sealed partial class DocumentDateDialog : Window
{
    private readonly bool _isBatchEdit;
    private readonly string _example = DisplayDates.Day(DateOnly.FromDateTime(DateTime.Today));

    internal DocumentDateDialog(DateOnly? currentDate, int documentCount = 1)
    {
        _isBatchEdit = documentCount > 1;
        InitializeComponent();
        DocumentDateBox.Text = currentDate is { } date ? DisplayDates.Day(date) : "";
        DocumentDateBox.PlaceholderText = _example;
        DocumentDateHint.Text = DesktopText.Format($"Use a date like {_example}. Leave it empty to clear the date.");
        if (!_isBatchEdit) return;

        DocumentDatePrompt.Text = DesktopText.Format($"Document date for {documentCount} documents");
        DocumentDateHint.Text = DesktopText.Format($"Use a date like {_example}.");
        ClearDocumentDatesButton.IsVisible = true;
        ConfirmDocumentDateButton.IsEnabled = currentDate.HasValue;
        DocumentDateBox.TextChanging += (_, _) =>
            ConfirmDocumentDateButton.IsEnabled = !string.IsNullOrWhiteSpace(DocumentDateBox.Text);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DocumentDateBox.Focus();
        DocumentDateBox.SelectAll();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBatchEdit) Close(new DocumentDateEdit(null));
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        string value = DocumentDateBox.Text?.Trim() ?? "";
        if (value.Length == 0)
        {
            if (!_isBatchEdit) Close(new DocumentDateEdit(null));
            return;
        }
        if (DisplayDates.TryParseDay(value, out var date))
        {
            Close(new DocumentDateEdit(date));
            return;
        }
        DateValidation.Text = DesktopText.Format($"Use a date like {_example}.");
        DateValidation.IsVisible = true;
        DocumentDateBox.Focus();
    }
}
