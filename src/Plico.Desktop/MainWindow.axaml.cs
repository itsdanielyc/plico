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

    public MainWindow() => InitializeComponent();

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
        if (ViewModel is not { IsBusy: true } model) return;
        e.Cancel = true;
        if (_closeWhenIdle) return;
        _closeWhenIdle = true;
        await model.CancelAndWaitAsync();
        Close();
    }

    private void FilterTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string tag }) ViewModel?.FilterByTag(tag);
    }

    private void RemoveTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string tag }) ViewModel?.RemoveTag(tag);
    }

    private void TagBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ViewModel?.AddTagCommand.Execute(null);
        e.Handled = true;
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
