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
        var scan = await Task.Run(() => library.ScanWithRecoveryAsync(request,
            (interruption, cancellation) => Dispatcher.UIThread.InvokeAsync(() => ConfirmPartialScanAsync(interruption, cancellation)),
            token, language, destinationFolder), token);
        if (scan.Document is not { } document)
        {
            Status = DesktopText.Translate("Captured pages discarded.");
            return;
        }

        RevealNewInboxDocument(document.Id);
        string scanName = scan.Interruption is null ? DesktopText.Translate("Scan") : "Partial scan";
        Status = document.HasText ? DesktopText.Format($"{scanName} added to inbox.") : DesktopText.Format($"{scanName} added. No searchable text was recognized.");
    });

    private async Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool save = await _dialogs.ConfirmPartialScanAsync(interruption, cancellationToken);
        if (save) Status = DesktopText.Translate("Recognizing captured pages…");
        return save;
    }
}
