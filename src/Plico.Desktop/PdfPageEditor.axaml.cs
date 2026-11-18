using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal abstract record PdfPageEditRequest;
internal sealed record ReorderPdfPages(int[] Order) : PdfPageEditRequest;
internal sealed record DeletePdfPages(PdfPageSelection Selection) : PdfPageEditRequest;
internal sealed record SplitPdfPages(int[] PartStarts) : PdfPageEditRequest;
internal sealed record RotatePdfPages(PdfPageSelection Selection, PdfPageRotation Rotation) : PdfPageEditRequest;

internal sealed partial class PdfPageEditor : Window
{
    private enum EditAction { Reorder, Delete, Split, Rotate }

    private static readonly PdfPageRotation[] Rotations =
        [PdfPageRotation.Clockwise, PdfPageRotation.HalfTurn, PdfPageRotation.Counterclockwise];

    private readonly int _pageCount;
    private readonly ObservableCollection<int> _pageOrder;

    internal PdfPageEditor(int pageCount, int currentPage)
    {
        InitializeComponent();
        _pageCount = pageCount;
        _pageOrder = new ObservableCollection<int>(Enumerable.Range(1, pageCount));
        PageOrder.ItemsSource = _pageOrder;
        PagesBox.Text = currentPage.ToString(CultureInfo.InvariantCulture);
        TurnBox.SelectedIndex = 0;
        PageOrder.SelectionChanged += (_, _) => UpdateReorderButtons();
        PageOrder.SelectedIndex = currentPage - 1;
        UpdateReorderButtons();
        ActionBox.SelectionChanged += (_, _) => UpdateFields();
        ActionBox.SelectedIndex = pageCount > 1 ? (int)EditAction.Reorder : (int)EditAction.Rotate;
        UpdateFields();
    }

    private EditAction Action => (EditAction)ActionBox.SelectedIndex;

    private void UpdateFields()
    {
        OrderPanel.IsVisible = Action == EditAction.Reorder;
        RangePanel.IsVisible = Action != EditAction.Reorder;
        TurnBox.IsVisible = Action == EditAction.Rotate;
        RangeHint.Text = Action == EditAction.Split
            ? "Enter the first page of each new part after page 1. For example, 3, 6."
            : "Enter page numbers or ranges. For example, 1, 3–5.";
        ResultHint.Text = Action == EditAction.Split ? "Plico keeps the original PDF and adds the parts to your inbox."
            : "Plico saves the previous PDF in version history.";
        ValidationMessage.IsVisible = false;
    }

    private void UpdateReorderButtons()
    {
        int index = PageOrder.SelectedIndex;
        MoveEarlier.IsEnabled = index > 0;
        MoveLater.IsEnabled = index >= 0 && index < _pageOrder.Count - 1;
    }

    private void MoveEarlier_Click(object? sender, RoutedEventArgs e) => MovePage(-1);
    private void MoveLater_Click(object? sender, RoutedEventArgs e) => MovePage(1);

    private void MovePage(int direction)
    {
        int index = PageOrder.SelectedIndex;
        int destination = index + direction;
        if (index < 0 || destination < 0 || destination >= _pageOrder.Count) return;
        _pageOrder.Move(index, destination);
        PageOrder.SelectedIndex = destination;
        PageOrder.ScrollIntoView(destination);
        UpdateReorderButtons();
    }

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var request = CreateRequest();
            Close(request);
        }
        catch (ArgumentException error)
        {
            ValidationMessage.Text = error.Message;
            ValidationMessage.IsVisible = true;
        }
    }

    private PdfPageEditRequest CreateRequest()
    {
        if (Action == EditAction.Reorder)
        {
            if (_pageOrder.SequenceEqual(Enumerable.Range(1, _pageCount)))
                throw new ArgumentException("Move at least one page to change the order.");
            return new ReorderPdfPages(_pageOrder.ToArray());
        }

        string input = PagesBox.Text ?? "";
        if (Action == EditAction.Split)
        {
            string[] entries = input.Split(',', StringSplitOptions.TrimEntries);
            var starts = new int[entries.Length];
            for (int index = 0; index < entries.Length; index++)
            {
                if (!int.TryParse(entries[index], NumberStyles.None, CultureInfo.InvariantCulture, out int page) || page < 2 || page > _pageCount)
                    throw new ArgumentException("Choose the start of a new part between page 2 and the end of the PDF.");
                starts[index] = page;
            }
            return new SplitPdfPages(starts);
        }
        var selection = PdfPageSelection.Parse(input.Replace('–', '-'), _pageCount);
        if (Action == EditAction.Rotate) return new RotatePdfPages(selection, Rotations[TurnBox.SelectedIndex]);
        if (Action == EditAction.Delete)
        {
            if (selection.SelectedPageCount == _pageCount) throw new ArgumentException("Keep at least one page in the PDF.");
            return new DeletePdfPages(selection);
        }
        throw new InvalidOperationException("Choose a page editing action.");
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
