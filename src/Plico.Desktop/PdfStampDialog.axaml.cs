using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed record PdfStampRequest(PdfPageSelection Pages, PdfStamp Stamp);

internal sealed partial class PdfStampDialog : Window
{
    private readonly int _pageCount;

    internal PdfStampDialog(int pageCount, int currentPage)
    {
        InitializeComponent();
        _pageCount = pageCount;
        StampPages.Text = currentPage.ToString(CultureInfo.InvariantCulture);
        StampText.MaxLength = PdfStamp.MaximumLength;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        StampText.Focus();
        StampText.SelectAll();
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string text = StampText.Text?.Trim() ?? "";
            if (text.Length == 0 || text.Any(char.IsControl)) throw new ArgumentException(DesktopText.Translate("Enter printable stamp text."));
            var pages = PdfPageSelection.Parse((StampPages.Text ?? "").Replace('–', '-'), _pageCount);
            Close(new PdfStampRequest(pages, new PdfStamp(text, (PdfStampPosition)StampPosition.SelectedIndex)));
        }
        catch (ArgumentException error)
        {
            ValidationNotice.Text = error.Message;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
