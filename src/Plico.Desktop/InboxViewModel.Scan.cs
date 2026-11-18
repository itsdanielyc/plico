using CommunityToolkit.Mvvm.Input;

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
        var document = await Task.Run(() => library.ScanAsync(request, token, language), token);
        Navigate(inboxOnly: true, preferredId: document.Id);
        Status = document.HasText ? "Scan added to inbox." : "Scan added. No searchable text was recognized.";
    });
}
