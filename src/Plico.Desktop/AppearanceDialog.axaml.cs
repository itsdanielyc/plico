using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

internal sealed partial class AppearanceDialog : Window
{
    internal AppearanceDialog(AppearanceOptions options)
    {
        InitializeComponent();
        ThemeBox.SelectedIndex = (int)options.Theme;
        LanguageBox.SelectedIndex = (int)options.Language;
    }

    private void Save_Click(object? sender, RoutedEventArgs e) =>
        Close(new AppearanceOptions((AppearanceTheme)ThemeBox.SelectedIndex, (InterfaceLanguage)LanguageBox.SelectedIndex));

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
