using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>Library, intake, recognition and filing preferences, bound to the same view model as the main window.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Jump_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string target } && this.FindControl<Control>(target) is { } group) group.BringIntoView();
    }

    private void ShowShortcuts_Click(object? sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main)
        {
            Close();
            main.ShowShortcutSheet();
        }
    }
}
