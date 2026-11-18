using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>A tag and its number of assignments within the captured selection.</summary>
internal sealed record BatchTagOption(string Label, int DocumentCount)
{
    public string CountLabel => DocumentCount == 1 ? "1 document" : DesktopText.Format($"{DocumentCount} documents");
}

/// <summary>Asks which tag to remove from the currently selected documents.</summary>
internal sealed partial class BatchTagRemovalDialog : Window
{
    internal BatchTagRemovalDialog(IReadOnlyList<BatchTagOption> options)
    {
        InitializeComponent();
        BatchTagChoices.ItemsSource = options;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        BatchTagChoices.Focus();
    }

    private void TagChoice_Changed(object? sender, SelectionChangedEventArgs e) =>
        ConfirmTagRemovalButton.IsEnabled = BatchTagChoices.SelectedItem is BatchTagOption;

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (BatchTagChoices.SelectedItem is BatchTagOption tag) Close(tag.Label);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
