using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifySingleExportAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidenceDirectory)
    {
        const string occupiedContents = "Existing export fixture";
        const string exportedReceiptEvidence = "exported-ocr-receipt.pdf";
        var document = model.SelectedDocument ?? throw new InvalidOperationException("The OCR receipt was not selected for export.");
        string original = Path.Combine(model.LibraryPath, "PDFs", document.RelativePath);
        var destination = Directory.CreateTempSubdirectory("plico-verify-single-export-");
        string? oldFolder = dialogs.FolderPath;
        try
        {
            string occupied = Path.Combine(destination.FullName, Path.GetFileName(document.RelativePath));
            File.WriteAllText(occupied, occupiedContents);
            dialogs.FolderPath = destination.FullName;
            var export = Control<Button>(window, "ExportPdfButton");
            Require(export.IsEffectivelyVisible && export.IsEnabled && export.Command is IAsyncRelayCommand,
                "The preview did not offer a single PDF export.");
            await ((IAsyncRelayCommand)export.Command!).ExecuteAsync(export.CommandParameter);

            string copied = Path.Combine(destination.FullName, $"{Path.GetFileNameWithoutExtension(occupied)} (2).pdf");
            Require(File.Exists(copied) && model.SelectedDocument?.Id == document.Id &&
                    Directory.EnumerateFileSystemEntries(destination.FullName).Count() == 2 &&
                    File.ReadAllText(occupied) == occupiedContents &&
                    File.ReadAllBytes(original).AsSpan().SequenceEqual(File.ReadAllBytes(copied)),
                $"The exported PDF did not preserve the copied bytes or choose a free filename. {model.Status}");
            var open = Control<Button>(window, "OpenExportFolderButton");
            Require(open.IsEffectivelyVisible && open.Command is not null, "The copy did not offer its destination folder.");
            open.Command!.Execute(null);
            Require(Path.GetFileName(dialogs.LastOpenedPath) == Path.GetFileName(destination.FullName),
                "The single export shortcut opened a different folder.");

            File.Copy(copied, Path.Combine(evidenceDirectory, exportedReceiptEvidence), overwrite: true);
            await CaptureAsync(window, evidenceDirectory, "single-pdf-export.png");
        }
        finally
        {
            model.LatestExportLocation = null;
            dialogs.FolderPath = oldFolder;
            destination.Delete(recursive: true);
        }
    }
}
