using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class PdfExportDialog : Window
{
    private sealed record FormatChoice(PdfExportFormat Format, string Label, string Hint)
    {
        public override string ToString() => Label;
    }

    private readonly OcrLanguage _language;

    internal PdfExportDialog(OcrLanguage language)
    {
        _language = language;
        InitializeComponent();
        PdfExportFormatBox.ItemsSource = new FormatChoice[]
        {
            new(PdfExportFormat.Original, "Original PDF", "Save a copy of the PDF as it is in the library."),
            new(PdfExportFormat.PdfA, "Archival PDF/A-2b", $"Render the pages and rebuild searchable text using {language} OCR."),
            new(PdfExportFormat.PasswordProtected, "Password-protected PDF", "The copy requires a password to open."),
            new(PdfExportFormat.WithMetadata, "PDF with library metadata", "Put the library title and tags in the copy’s metadata.")
        };
        PdfExportFormatBox.SelectedIndex = 0;
    }

    private void Format_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (PdfExportFormatBox.SelectedItem is not FormatChoice choice) return;
        PdfExportHint.Text = choice.Hint;
        PdfPasswordFields.IsVisible = choice.Format == PdfExportFormat.PasswordProtected;
        PdfExportError.Text = null;
        if (PdfPasswordFields.IsVisible) return;
        PdfPassword.Text = ConfirmPdfPassword.Text = null;
    }

    private void Password_Changed(object? sender, TextChangedEventArgs e) => PdfExportError.Text = null;

    private void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (PdfExportFormatBox.SelectedItem is not FormatChoice choice) return;
        string? password = PdfPassword.Text;
        if (choice.Format == PdfExportFormat.PasswordProtected)
        {
            if (!PdfExportOptions.IsValidPassword(password))
            {
                PdfExportError.Text = "Enter a password of up to 127 UTF-8 bytes.";
                return;
            }
            if (!string.Equals(password, ConfirmPdfPassword.Text, StringComparison.Ordinal))
            {
                PdfExportError.Text = "The two passwords differ.";
                return;
            }
        }
        Close(new PdfExportOptions(choice.Format, password, _language));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
