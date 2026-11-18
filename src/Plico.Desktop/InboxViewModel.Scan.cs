using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task ScanAsync() => RunOperationAsync("Choose a scanner…", async token =>
    {
        var library = RequireLibrary();
        var language = RecognitionLanguage;
        var request = await _dialogs.PickScannerAsync();
        if (request is null)
        {
            Status = "No scan started.";
            return;
        }

        Status = $"Scanning from {request.Device.Name}…";
        var scan = await Task.Run(() => library.ScanWithRecoveryAsync(request,
            (interruption, cancellation) => Dispatcher.UIThread.InvokeAsync(() => ConfirmPartialScanAsync(interruption, cancellation)),
            token, language), token);
        if (scan.Document is not { } document)
        {
            Status = "Captured pages discarded.";
            return;
        }

        RevealNewInboxDocument(document.Id);
        string scanName = scan.Interruption is null ? "Scan" : "Partial scan";
        Status = document.HasText ? $"{scanName} added to inbox." : $"{scanName} added. No searchable text was recognized.";
    });

    private async Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool save = await _dialogs.ConfirmPartialScanAsync(interruption, cancellationToken);
        if (save) Status = "Recognizing captured pages…";
        return save;
    }
}
