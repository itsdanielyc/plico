using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed record TrashDecision(long DocumentId, bool Purge);

internal sealed partial class TrashDialog : Window
{
    private const string RestoreHint = "Select a PDF to restore it to its original folder.";
    private bool _confirmingPurge;

    internal TrashDialog(IReadOnlyList<TrashedDocument> documents)
    {
        InitializeComponent();
        TrashedFiles.ItemsSource = documents;
        TrashedFiles.IsVisible = documents.Count > 0;
        TrashMessage.Text = documents.Count == 0 ? DesktopText.Translate("Trash is empty.") : RestoreHint;
    }

    private void Selection_Changed(object? sender, SelectionChangedEventArgs e)
    {
        _confirmingPurge = false;
        RestoreTrashButton.IsEnabled = PurgeTrashButton.IsEnabled = TrashedFiles.SelectedItem is TrashedDocument;
        PurgeTrashButton.Content = DesktopText.Translate("Delete permanently…");
        TrashMessage.Text = RestoreHint;
    }

    private void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (TrashedFiles.SelectedItem is TrashedDocument selected) Close(new TrashDecision(selected.DocumentId, Purge: false));
    }

    private void Purge_Click(object? sender, RoutedEventArgs e)
    {
        if (TrashedFiles.SelectedItem is not TrashedDocument selected) return;
        if (_confirmingPurge)
        {
            Close(new TrashDecision(selected.DocumentId, Purge: true));
            return;
        }
        _confirmingPurge = true;
        RestoreTrashButton.IsEnabled = false;
        TrashMessage.Text = $"Permanently delete “{selected.Title}” and its library metadata?";
        PurgeTrashButton.Content = DesktopText.Translate("Confirm deletion");
        CloseTrashButton.Focus();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
