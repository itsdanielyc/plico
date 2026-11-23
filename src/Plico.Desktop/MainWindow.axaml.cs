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
    private bool _closePrepared;
    private InboxViewModel? ViewModel => DataContext as InboxViewModel;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWorkspaceLayout(Width);
        WirePalette();
        WireShortcutKeys();
        WireToast();
        WireFieldEditing();
        WatchTheDay();
        PreviewPage.PropertyChanged += (_, change) =>
        {
            if (change.Property == Image.SourceProperty) UpdatePreviewScrollPosition();
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
        if (e.Key == Key.Escape && IsOverlayOpen)
        {
            CloseOverlays();
            e.Handled = true;
            return;
        }
        if (IsOverlayOpen) return;
        bool command = (e.KeyModifiers & ~KeyModifiers.Shift) is KeyModifiers.Control or KeyModifiers.Meta;
        if (!command || e.Handled || ViewModel is not { } model) return;
        if (e.Key == Key.F && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.O && (e.KeyModifiers & KeyModifiers.Shift) == 0 && model.CanWork)
        {
            model.AddFilesCommand.Execute(null);
            e.Handled = true;
        }
        else if (HandleCommandKey(e, model))
        {
            e.Handled = true;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closePrepared || ViewModel is not { } model ||
            (!model.IsBusy && !model.HasActiveWatches && !model.IsReadingInBackground)) return;
        e.Cancel = true;
        if (_closeWhenIdle) return;
        _closeWhenIdle = true;
        IsEnabled = false;
        await model.PrepareCloseAsync();
        await model.StopBackgroundReads().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        _closePrepared = true;
        Close();
    }

    private void FilterTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TagNavigationItem tag }) ViewModel?.FilterByTag(tag.Label);
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
        if (sender is Button { DataContext: TagChip chip }) ViewModel?.RemoveTag(chip.Label);
        else if (sender is Button { DataContext: string tag }) ViewModel?.RemoveTag(tag);
    }

    private void TagBox_Populating(object? sender, PopulatingEventArgs e)
    {
        if (sender is AutoCompleteBox field)
        {
            field.ItemsSource = ViewModel?.GetTagSuggestions(e.Parameter, ReferenceEquals(field, TagBox));
        }
    }

    /// <summary>
    /// One Enter adds the tag. The field handles the key first: with a suggestion chosen it fills in the catalog's
    /// spelling and closes the list, otherwise it only closes it, and then the tag in the field is added.
    /// </summary>
    private void TagBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        TagBox.IsDropDownOpen = false;
        ViewModel?.AddTagCommand.Execute(null);
        e.Handled = true;
    }

    private void Documents_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } model && sender is ListBox list)
        {
            _queueSelectionVersion++;
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

    private async void SuggestSelectedFiling_Click(object? sender, RoutedEventArgs e) =>
        await UpdateSelectedDocumentsAsync(static model => model.SuggestSelectedFilingAsync());

    private async void BatchTag_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ApplyBatchTagAsync();
    }

    private Task ApplyBatchTagAsync() => UpdateSelectedDocumentsAsync(static model => model.ApplySelectedTagAsync());

    internal async Task UpdateSelectedDocumentsAsync(Func<InboxViewModel, Task> update)
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
