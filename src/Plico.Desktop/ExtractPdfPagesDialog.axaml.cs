using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The validated pages and title confirmed for a PDF excerpt.</summary>
internal sealed record ExtractPdfPagesRequest(string Title, PdfPageSelection Selection);

internal sealed partial class ExtractPdfPagesDialog : Window
{
    private const string DefaultTitle = "Extracted pages";
    private readonly int _documentPageCount;

    internal ExtractPdfPagesDialog(int documentPageCount, int currentPage)
    {
        InitializeComponent();
        _documentPageCount = documentPageCount;
        PagesBox.Text = currentPage.ToString(CultureInfo.InvariantCulture);
        PageHint.Text = $"Choose pages 1 to {documentPageCount}. Use commas between entries and a hyphen for a range.";
        TitleBox.MaxLength = InboxLibrary.MaximumTitleLength;
        TitleBox.Text = DefaultTitle;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PagesBox.Focus();
        PagesBox.SelectAll();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Extract_Click(object? sender, RoutedEventArgs e)
    {
        PdfPageSelection selection;
        try
        {
            selection = PdfPageSelection.Parse(PagesBox.Text ?? "", _documentPageCount);
        }
        catch (ArgumentException error)
        {
            ShowValidation(error.Message, PagesBox);
            return;
        }

        try
        {
            string title = InboxLibrary.NormalizeTitle(TitleBox.Text ?? "");
            Close(new ExtractPdfPagesRequest(title, selection));
        }
        catch (ArgumentException error)
        {
            ShowValidation(error.Message, TitleBox);
        }
    }

    private void ShowValidation(string message, TextBox input)
    {
        ValidationMessage.Text = message;
        ValidationMessage.IsVisible = true;
        input.Focus();
    }
}
