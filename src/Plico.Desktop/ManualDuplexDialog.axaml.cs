using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Lets the feeder operator finish or pause a saved first pass.</summary>
internal sealed partial class ManualDuplexDialog : Window
{
    internal ManualDuplexDialog(ManualDuplexPrompt prompt)
    {
        InitializeComponent();
        FrontSidesMessage.Text = prompt.FrontPageCount == 1 ? "1 front side saved" : $"{prompt.FrontPageCount} front sides saved";
    }

    internal async Task<ManualDuplexDecision> ShowDecisionAsync(Window owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = ShowDialog<ManualDuplexDecision?>(owner);
        try
        {
            return await decision.WaitAsync(cancellationToken) ?? ManualDuplexDecision.SaveForLater;
        }
        finally
        {
            if (IsVisible) Close(ManualDuplexDecision.SaveForLater);
        }
    }

    private void Discard_Click(object? sender, RoutedEventArgs e) => Close(ManualDuplexDecision.Discard);
    private void Later_Click(object? sender, RoutedEventArgs e) => Close(ManualDuplexDecision.SaveForLater);
    private void ScanBacks_Click(object? sender, RoutedEventArgs e) => Close(ManualDuplexDecision.ScanBacks);
}
