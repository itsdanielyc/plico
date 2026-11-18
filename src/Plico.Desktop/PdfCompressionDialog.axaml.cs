using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class PdfCompressionDialog : Window
{
    internal PdfCompressionDialog() => InitializeComponent();

    private void Confirm_Click(object? sender, RoutedEventArgs e) =>
        Close(SmallerScansOption.IsChecked == true ? PdfCompressionMode.SmallerScans : PdfCompressionMode.Lossless);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
