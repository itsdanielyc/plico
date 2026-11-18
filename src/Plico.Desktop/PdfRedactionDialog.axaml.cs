using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class PdfRedactionDialog : Window
{
    private readonly int _pageCount;
    private readonly Func<int, CancellationToken, Task<Bitmap>> _render;
    private readonly Dictionary<int, List<PdfRedactionRegion>> _marks = [];
    private int _pageNumber;
    private CancellationTokenSource? _previewRequest;
    private Bitmap? _page;

    internal PdfRedactionDialog(int pageCount, int currentPage, Func<int, CancellationToken, Task<Bitmap>> render)
    {
        InitializeComponent();
        _pageCount = pageCount;
        _pageNumber = currentPage;
        _render = render;
        PageCanvas.RegionDrawn += AddRegion;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await LoadPageAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewRequest?.Cancel();
        PageCanvas.ShowPage(null, []);
        _page?.Dispose();
        base.OnClosed(e);
    }

    private async Task LoadPageAsync()
    {
        _previewRequest?.Cancel();
        _page?.Dispose();
        _page = null;
        RefreshControls();
        PreviewNotice.Text = DesktopText.Translate("Preparing a page preview…");
        using var request = new CancellationTokenSource();
        _previewRequest = request;
        try
        {
            var bitmap = await _render(_pageNumber, request.Token);
            if (request.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }
            _page = bitmap;
            PreviewNotice.Text = "";
            RefreshControls();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested) PreviewNotice.Text = error.Message;
        }
        finally
        {
            if (_previewRequest == request) _previewRequest = null;
        }
    }

    internal void AddRegion(Rect bounds)
    {
        if (_page is null) return;
        if (!_marks.TryGetValue(_pageNumber, out var marks)) _marks.Add(_pageNumber, marks = []);
        marks.Add(new PdfRedactionRegion(_pageNumber, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
        RefreshControls();
    }

    private void RefreshControls()
    {
        IReadOnlyList<PdfRedactionRegion> pageMarks = _marks.GetValueOrDefault(_pageNumber) ?? [];
        PageCanvas.ShowPage(_page, pageMarks);
        PageLabel.Text = $"{_pageNumber} / {_pageCount}";
        PreviousPage.IsEnabled = _pageNumber > 1;
        NextPage.IsEnabled = _pageNumber < _pageCount;
        UndoRegion.IsEnabled = pageMarks.Count != 0;
        ConfirmRedactions.IsEnabled = _marks.Count != 0;
    }

    private async void Previous_Click(object? sender, RoutedEventArgs e)
    {
        if (_pageNumber <= 1) return;
        _pageNumber--;
        await LoadPageAsync();
    }

    private async void Next_Click(object? sender, RoutedEventArgs e)
    {
        if (_pageNumber >= _pageCount) return;
        _pageNumber++;
        await LoadPageAsync();
    }

    private void Undo_Click(object? sender, RoutedEventArgs e)
    {
        if (!_marks.TryGetValue(_pageNumber, out var pageMarks)) return;
        pageMarks.RemoveAt(pageMarks.Count - 1);
        if (pageMarks.Count == 0) _marks.Remove(_pageNumber);
        RefreshControls();
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) =>
        Close(_marks.Values.SelectMany(marks => marks).ToArray());

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
