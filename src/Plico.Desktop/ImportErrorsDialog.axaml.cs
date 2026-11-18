using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>Identifies which intake settings apply to a file import.</summary>
internal enum ImportOrigin { Manual = 0, FolderWatch = 1 }

/// <summary>Preserves the source, reason, and intake route for an unresolved import.</summary>
internal sealed record ImportFailure(string SourcePath, string Reason, ImportOrigin Origin = ImportOrigin.Manual)
{
    public string Filename => Path.GetFileName(SourcePath);
}

internal enum ImportErrorsAction { Close, Retry, Clear }

/// <summary>Lets the user inspect, retry, or clear the active library's import errors.</summary>
internal sealed partial class ImportErrorsDialog : Window
{
    internal ImportErrorsDialog(IReadOnlyList<ImportFailure> failures)
    {
        InitializeComponent();
        FailuresList.ItemsSource = failures;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close(ImportErrorsAction.Close);
    private void Retry_Click(object? sender, RoutedEventArgs e) => Close(ImportErrorsAction.Retry);
    private void Clear_Click(object? sender, RoutedEventArgs e) => Close(ImportErrorsAction.Clear);
}
