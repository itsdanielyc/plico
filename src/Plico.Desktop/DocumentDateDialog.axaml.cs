using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>An accepted date edit. A null date clears the stored value.</summary>
internal sealed record DocumentDateEdit(DateOnly? Date);

/// <summary>Edits the recorded calendar date for one or more documents.</summary>
internal sealed partial class DocumentDateDialog : Window
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly bool _isBatchEdit;

    internal DocumentDateDialog(DateOnly? currentDate, int documentCount = 1)
    {
        _isBatchEdit = documentCount > 1;
        InitializeComponent();
        DocumentDateBox.Text = currentDate?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";
        if (!_isBatchEdit) return;

        DocumentDatePrompt.Text = $"Document date for {documentCount} PDFs";
        DocumentDateHint.Text = "Use YYYY-MM-DD.";
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
