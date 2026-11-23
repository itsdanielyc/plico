using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal enum PdfVersionAction { Replace, Restore, Export }
internal sealed record PdfVersionDecision(PdfVersionAction Action, Guid Key = default);

internal sealed record PdfVersionRow(PdfVersion Version)
{
    public string Label => Version.Change switch
    {
        PdfRevisionKind.Reorder => DesktopText.Translate("Before reordering pages"),
        PdfRevisionKind.DeletePages => DesktopText.Translate("Before deleting pages"),
        PdfRevisionKind.Rotate => DesktopText.Translate("Before rotating pages"),
        PdfRevisionKind.Replace => DesktopText.Translate("Before adding a revised PDF"),
        PdfRevisionKind.Restore => DesktopText.Translate("Before restoring an earlier PDF"),
        PdfRevisionKind.Unlock => DesktopText.Translate("Password-protected original"),
        _ => DesktopText.Translate("Earlier PDF")
    };
    public string Detail => $"{DisplayDates.DayAndTime(Version.ArchivedAt)} · {DisplayDates.Pages(Version.PageCount)}";
}

internal sealed partial class PdfVersionHistoryDialog : Window
{
    internal PdfVersionHistoryDialog(DocumentSummary document, IReadOnlyList<PdfVersion> versions)
    {
        InitializeComponent();
        DocumentTitle.Text = document.Title;
        EmptyHistory.IsVisible = versions.Count == 0;
        Versions.ItemsSource = versions.Select(version => new PdfVersionRow(version)).ToArray();
        Versions.SelectionChanged += (_, _) => UpdateActions();
        Versions.SelectedIndex = versions.Count > 0 ? 0 : -1;
        UpdateActions();
    }

    private void UpdateActions()
    {
        ExportVersion.IsEnabled = Versions.SelectedItem is PdfVersionRow;
        RestoreVersion.IsEnabled = Versions.SelectedItem is PdfVersionRow { Version.IsEncrypted: false };
    }

    private void Replace_Click(object? sender, RoutedEventArgs e) => Close(new PdfVersionDecision(PdfVersionAction.Replace));

    private void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (Versions.SelectedItem is PdfVersionRow { Version.IsEncrypted: false } row)
            Close(new PdfVersionDecision(PdfVersionAction.Restore, row.Version.Key));
    }

    private void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (Versions.SelectedItem is PdfVersionRow row) Close(new PdfVersionDecision(PdfVersionAction.Export, row.Version.Key));
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
