using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Edits the title shown in the library and validates it before returning it.</summary>
internal sealed partial class DocumentTitleDialog : Window
{
    internal DocumentTitleDialog(string currentTitle)
    {
        InitializeComponent();
        TitleBox.MaxLength = InboxLibrary.MaximumTitleLength;
        TitleBox.Text = currentTitle;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var title = InboxLibrary.NormalizeTitle(TitleBox.Text ?? "");
            Close(title);
        }
        catch (ArgumentException error)
        {
            ValidationMessage.Text = error.Message;
            ValidationMessage.IsVisible = true;
            TitleBox.Focus();
        }
    }
}
