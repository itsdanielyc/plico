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
        string? destinationFolder = IntakeDestination(library);
        var request = await _dialogs.PickScannerAsync();
        if (request is null)
        {
            Status = "No scan started.";
            return;
        }

        Status = $"Scanning from {request.Device.Name}…";
        if (request.RequiresBatchCapture)
        {
            await ScanBatchAsync(library, request, language, destinationFolder, token);
            return;
        }
        var scan = await Task.Run(() => library.ScanWithRecoveryAsync(request,
            (interruption, cancellation) => Dispatcher.UIThread.InvokeAsync(() => ConfirmPartialScanAsync(interruption, cancellation)),
            token, language, destinationFolder), token);
        if (scan.Document is not { } document)
        {
            Status = "Captured pages discarded.";
            return;
        }

        RevealNewInboxDocument(document.Id);
        string scanName = scan.Interruption is null ? "Scan" : "Partial scan";
        Status = document.HasText ? $"{scanName} added to inbox." : $"{scanName} added. No searchable text was recognized.";
    });

    private async Task ScanBatchAsync(InboxLibrary library, ScanRequest request, OcrLanguage language,
        string? destinationFolder, CancellationToken cancellationToken)
    {
        var batch = await Task.Run(() => library.ScanBatchAsync(request,
            (interruption, token) => Dispatcher.UIThread.InvokeAsync(() => ConfirmPartialScanAsync(interruption, token)),
            (prompt, token) => Dispatcher.UIThread.InvokeAsync(() => _dialogs.ChooseDuplexActionAsync(prompt, token)),
            cancellationToken, language, destinationFolder), cancellationToken);
        if (batch.Documents.Count == 0)
        {
            Status = batch.HasPendingDuplex ? "Front sides saved. Choose manual duplex again to scan the backs." : "Captured pages discarded.";
            return;
        }
        RevealNewInboxDocument(batch.Documents[^1].Id);
        int withoutText = batch.Documents.Count(document => !document.HasText);
        Status = $"{DocumentCount(batch.Documents.Count)} from the scan added to the inbox.";
        if (withoutText != 0) Status += $" {DocumentCount(withoutText)} without searchable text.";
    }

    private async Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool save = await _dialogs.ConfirmPartialScanAsync(interruption, cancellationToken);
        if (save) Status = "Recognizing captured pages…";
        return save;
    }
}
