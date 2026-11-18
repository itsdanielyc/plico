using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

internal sealed partial class UnlockPdfDialog : Window
{
    internal UnlockPdfDialog(string sourcePath)
    {
        InitializeComponent();
        FileLabel.Text = Path.GetFileName(sourcePath);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PasswordBox.Focus();
    }

    private void Unlock_Click(object? sender, RoutedEventArgs e)
    {
        string password = PasswordBox.Text ?? "";
        PasswordBox.Text = "";
        Close(password);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        PasswordBox.Text = "";
        Close();
    }
}
