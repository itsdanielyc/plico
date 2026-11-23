using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using System.Globalization;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task EditArchiveSerialAsync() => RunOperationAsync("Set the archive number…", async token =>
    {
        if (SelectedDocument is not { } selected) return;
        var library = RequireLibrary();
        var input = await _dialogs.PickArchiveSerialAsync(ArchiveSerialAction.Edit, library.ReadArchiveSerialNumber(selected.Id));
        if (input is null) return;
        token.ThrowIfCancellationRequested();
        await Task.Run(() => library.SetArchiveSerialNumber(selected.Id, input.Number), token);
        RetireUndo();
        LoadResults(preferredId: selected.Id, retainLoadedResults: true);
        Status = input.Number is long number ? $"Assigned archive serial {number}." : "Archive serial removed.";
    });

    [RelayCommand]
    private Task FindArchiveSerialAsync() => RunOperationAsync("Find an archive number…", async token =>
    {
        var input = await _dialogs.PickArchiveSerialAsync(ArchiveSerialAction.Find);
        if (input?.Number is not long serial) return;
        var library = RequireLibrary();
        var document = await Task.Run(() => library.FindByArchiveSerialNumber(serial), token);
        if (document is null)
        {
            Status = $"No available document has archive serial {serial}.";
            return;
        }
        Navigate(inboxOnly: false, preferredId: document.Id, query: $"asn:{serial.ToString(CultureInfo.InvariantCulture)}");
        Status = $"Found archive serial {serial}.";
    });

    [RelayCommand] private Task PrintArchiveLabelsAsync() => WriteArchiveSheetAsync(ArchiveSerialAction.Labels);
    [RelayCommand] private Task PrintSeparatorSheetAsync() => WriteArchiveSheetAsync(ArchiveSerialAction.Separator);

    private Task WriteArchiveSheetAsync(ArchiveSerialAction action) => RunOperationAsync("Create a printable sheet…", async token =>
    {
        var input = await _dialogs.PickArchiveSerialAsync(action);
        if (input is null) return;
        bool separator = action == ArchiveSerialAction.Separator;
        string? path = await _dialogs.PickArchiveSheetPathAsync(separator ? "Plico separator.pdf" : "Plico ASN labels.pdf");
        if (path is null) return;
        await Task.Run(() =>
        {
            if (separator) ArchiveLabels.WriteSeparatorSheet(path, input.Paper, token);
            else ArchiveLabels.WriteSerialSheet(path, input.Paper, input.Number!.Value, input.LabelCount, token);
        }, token);
        Status = "Printable PDF saved.";
    });
}
