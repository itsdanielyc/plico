using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The paperwork inbox window.</summary>
public sealed partial class MainWindow : Window
{
    private bool _closeWhenIdle;
    private InboxViewModel? ViewModel => DataContext as InboxViewModel;

    public MainWindow()
    {
        InitializeComponent();
        PreviewPage.PropertyChanged += (_, change) =>
        {
            if (change.Property == Image.SourceProperty) PreviewScroll.Offset = default;
        };
        PageTextContent.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBlock.TextProperty) return;
            PageTextContent.SelectionStart = 0;
            PageTextContent.SelectionEnd = 0;
            PageTextScroll.Offset = default;
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is { } model) await model.CatchUpWhenWindowOpensAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers is not (KeyModifiers.Control or KeyModifiers.Meta)) return;
        if (e.Key == Key.F)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        if (e.Key == Key.O && ViewModel is { CanWork: true } model)
        {
            model.AddFilesCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (ViewModel is not { } model || (!model.IsBusy && !model.IsWatchingFolder)) return;
        e.Cancel = true;
        if (_closeWhenIdle) return;
        _closeWhenIdle = true;
        IsEnabled = false;
        await model.PrepareCloseAsync();
        Close();
    }

    private void FilterTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string tag }) ViewModel?.FilterByTag(tag);
    }

    private async void OpenSavedSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } model && sender is Button { DataContext: SavedSearch search })
        {
            await model.OpenSavedSearchAsync(search);
        }
    }

    private void RemoveTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string tag }) ViewModel?.RemoveTag(tag);
    }

    private void TagBox_Populating(object? sender, PopulatingEventArgs e)
    {
        if (sender is AutoCompleteBox field)
        {
            field.ItemsSource = ViewModel?.GetTagSuggestions(e.Parameter, ReferenceEquals(field, TagBox));
        }
    }

    private void TagBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ViewModel?.AddTagCommand.Execute(null);
        e.Handled = true;
    }

    private void Documents_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } model && sender is ListBox list)
        {
            model.SetBatchSelection(list.Selection.SelectedItems.OfType<DocumentSummary>().ToArray());
        }
    }

    private void SelectShown_Click(object? sender, RoutedEventArgs e)
    {
        if (DocumentList.Selection.Count > 1)
        {
            DocumentList.Selection.Clear();
        }
        else if (DocumentList.ItemCount > 1)
        {
            DocumentList.Selection.SelectRange(0, DocumentList.ItemCount - 1);
        }
    }

    private async void ApplyBatchTag_Click(object? sender, RoutedEventArgs e) => await ApplyBatchTagAsync();

    private async void RemoveBatchTag_Click(object? sender, RoutedEventArgs e) =>
        await UpdateSelectedDocumentsAsync(static model => model.RemoveSelectedTagAsync());

    private async void EditBatchDocumentDate_Click(object? sender, RoutedEventArgs e) =>
        await UpdateSelectedDocumentsAsync(static model => model.EditBatchDocumentDatesAsync());

    private async void BatchTag_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ApplyBatchTagAsync();
    }

    private Task ApplyBatchTagAsync() => UpdateSelectedDocumentsAsync(static model => model.ApplySelectedTagAsync());

    private async Task UpdateSelectedDocumentsAsync(Func<InboxViewModel, Task> update)
    {
        if (ViewModel is not { CanWork: true, HasBatchSelection: true } model) return;
        var originalDocuments = model.Documents;
        var selectedIds = DocumentList.Selection.SelectedItems.OfType<DocumentSummary>()
            .Select(document => document.Id).ToHashSet();
        await update(model);
        if (ReferenceEquals(ViewModel, model) && !ReferenceEquals(model.Documents, originalDocuments))
        {
            DocumentList.SelectedItems = model.Documents.Where(document => selectedIds.Contains(document.Id)).ToList();
        }
    }

    private void Files_DragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File)) return;
        var accept = ViewModel?.CanWork == true && e.DragEffects.HasFlag(DragDropEffects.Copy);
        DropTarget.IsVisible = accept;
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Files_DragLeave(object? sender, DragEventArgs e) => DropTarget.IsVisible = false;

    private async void Files_Dropped(object? sender, DragEventArgs e)
    {
        DropTarget.IsVisible = false;
        if (!e.DataTransfer.Formats.Contains(DataFormat.File)) return;
        e.Handled = true;
        if (ViewModel is not { CanWork: true } model || !e.DragEffects.HasFlag(DragDropEffects.Copy))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var files = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        bool accept = files.Any(InboxLibrary.SupportsImport);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        await model.ImportDroppedFilesAsync(files, accept);
    }
}
