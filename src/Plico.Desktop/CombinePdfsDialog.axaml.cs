using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The title and document order confirmed for a new combined PDF.</summary>
internal sealed record CombinePdfsRequest(string Title, IReadOnlyList<long> DocumentIds);

/// <summary>Lets the reader confirm the order and title before Plico creates a PDF.</summary>
internal sealed partial class CombinePdfsDialog : Window
{
    private const string DefaultTitle = "Combined paperwork";
    private readonly ObservableCollection<DocumentSummary> _documents;

    internal CombinePdfsDialog(IReadOnlyList<DocumentSummary> documents)
    {
        InitializeComponent();
        _documents = new ObservableCollection<DocumentSummary>(documents);
        TitleBox.MaxLength = InboxLibrary.MaximumTitleLength;
        TitleBox.Text = DefaultTitle;
        SourceList.ItemsSource = _documents;
        SourceList.SelectionChanged += (_, _) => UpdateOrderButtons();
        SourceList.SelectedIndex = 0;
        UpdateOrderButtons();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void MoveUp_Click(object? sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDown_Click(object? sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int offset)
    {
        int index = SourceList.SelectedIndex;
        int target = index + offset;
        if (index < 0 || target < 0 || target >= _documents.Count) return;
        _documents.Move(index, target);
        SourceList.SelectedIndex = target;
        SourceList.ScrollIntoView(target);
        UpdateOrderButtons();
    }

    private void UpdateOrderButtons()
    {
        int index = SourceList.SelectedIndex;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < _documents.Count - 1;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Combine_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string title = InboxLibrary.NormalizeTitle(TitleBox.Text ?? "");
            Close(new CombinePdfsRequest(title, _documents.Select(document => document.Id).ToArray()));
        }
        catch (ArgumentException error)
        {
            ValidationMessage.Text = error.Message;
            ValidationMessage.IsVisible = true;
            TitleBox.Focus();
        }
    }
}
