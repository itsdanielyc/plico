using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

public partial class MainWindow
{
    private void PageThumbnail_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PdfPageThumbnail thumbnail }) ViewModel?.ShowThumbnail(thumbnail.PageNumber);
    }
}
