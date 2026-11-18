using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
}
