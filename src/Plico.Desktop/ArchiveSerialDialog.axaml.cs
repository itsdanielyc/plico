using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal enum ArchiveSerialAction { Edit, Find, Labels, Separator }
internal sealed record ArchiveSerialInput(long? Number, ScanPaper Paper, int LabelCount);

/// <summary>Collects catalog or print settings for an archive number.</summary>
internal sealed partial class ArchiveSerialDialog : Window
{
    private readonly ArchiveSerialAction _action;

    internal ArchiveSerialDialog(ArchiveSerialAction action, long? current)
    {
        _action = action;
        InitializeComponent();
        Title = action switch
        {
            ArchiveSerialAction.Edit => "Set archive serial number",
            ArchiveSerialAction.Find => "Find archive serial number",
            ArchiveSerialAction.Labels => "Save ASN label sheets",
            _ => "Save PATCH-T separator"
        };
        ArchiveSerialPrompt.Text = action switch
        {
            ArchiveSerialAction.Edit => "Enter a positive number for this document or clear the field to remove its number.",
            ArchiveSerialAction.Find => "Enter the number printed on the label.",
            ArchiveSerialAction.Labels => "Enter the first number to print. Print the saved PDF at actual size.",
            _ => "Place a printed separator between documents before feeding the batch."
        };
        SerialInput.IsVisible = action != ArchiveSerialAction.Separator;
        SerialInput.Text = current?.ToString(CultureInfo.InvariantCulture) ?? (action == ArchiveSerialAction.Labels ? "1" : "");
        PaperControls.IsVisible = action is ArchiveSerialAction.Labels or ArchiveSerialAction.Separator;
        PaperSize.ItemsSource = Enum.GetValues<ScanPaper>();
        PaperSize.SelectedItem = ScanPaper.Letter;
        LabelCountControls.IsVisible = action == ArchiveSerialAction.Labels;
        AcceptSerial.Content = action is ArchiveSerialAction.Labels or ArchiveSerialAction.Separator ? "Save PDF…" :
            action == ArchiveSerialAction.Find ? "Find document" : "Save number";
    }

    private void Accept_Click(object? sender, RoutedEventArgs e)
    {
        long? serial = null;
        string value = (SerialInput.Text ?? "").Trim();
        if (_action != ArchiveSerialAction.Separator && !(_action == ArchiveSerialAction.Edit && value.Length == 0))
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) || parsed <= 0)
            {
                SerialError.Text = "Enter a positive whole archive number.";
                return;
            }
            serial = parsed;
        }
        var paper = PaperSize.SelectedItem is ScanPaper chosen ? chosen : ScanPaper.Letter;
        int count = (int)(LabelCount.Value ?? 1);
        if (_action == ArchiveSerialAction.Labels && (count <= 0 || serial > long.MaxValue - (count - 1L)))
        {
            SerialError.Text = "The label range is too large for the starting number.";
            return;
        }
        Close(new ArchiveSerialInput(serial, paper, count));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
