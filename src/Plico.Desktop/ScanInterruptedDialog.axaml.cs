using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Asks what to do with pages delivered before a scanner error.</summary>
internal sealed partial class ScanInterruptedDialog : Window
{
    internal ScanInterruptedDialog(ScanInterruption interruption)
    {
        InitializeComponent();
        CapturedPageMessage.Text = interruption.CapturedPages == 1
            ? "The scanner captured 1 page."
            : $"The scanner captured {interruption.CapturedPages} pages.";
        ScannerError.Text = interruption.Reason;
    }

    /// <summary>Returns true only after an explicit save. Cancelling the scan closes the dialog.</summary>
    internal async Task<bool> ShowDecisionAsync(Window owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = ShowDialog<bool?>(owner);
        try
        {
            return await decision.WaitAsync(cancellationToken) == true;
        }
        finally
        {
            if (IsVisible) Close(false);
        }
    }

    private void Discard_Click(object? sender, RoutedEventArgs e) => Close(false);
    private void Save_Click(object? sender, RoutedEventArgs e) => Close(true);
}
