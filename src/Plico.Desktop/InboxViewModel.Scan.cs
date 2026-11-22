using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task ScanAsync() => RunOperationAsync(DesktopText.Translate("Choose a scanner…"), async token =>
    {
        var library = RequireLibrary();
        var language = RecognitionLanguage;
        string? destinationFolder = IntakeDestination(library);
        var request = await _dialogs.PickScannerAsync();
        if (request is null)
        {
            Status = DesktopText.Translate("No scan started.");
            return;
        }

        Status = DesktopText.Format($"Scanning from {request.Device.Name}…");
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
            Status = DesktopText.Translate("Captured pages discarded.");
            return;
        }

        RevealNewInboxDocument(document.Id);
        // Each message is a whole sentence with its own catalog row: French names the scan with a noun of its own
        // ("Numérisation … ajoutée"), never the Scan button's verb.
        bool partial = scan.Interruption is not null;
        Status = DesktopText.Translate((partial, document.HasText) switch
        {
            (false, true) => "Scan added to the Inbox.",
            (true, true) => "Partial scan added to the Inbox.",
            (false, false) => "Scan added. No searchable text was recognised.",
            (true, false) => "Partial scan added. No searchable text was recognised."
        });
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
        foreach (var conflict in batch.SerialConflicts)
        {
            Status += $" Scan {conflict.DocumentNumber} filed unassigned. ASN {conflict.ArchiveSerialNumber} is in use.";
        }
    }

    private async Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool save = await _dialogs.ConfirmPartialScanAsync(interruption, cancellationToken);
        if (save) Status = DesktopText.Translate("Recognising captured pages…");
        return save;
    }
}
