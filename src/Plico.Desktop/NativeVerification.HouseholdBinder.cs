using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyHouseholdBinderAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string evidenceDirectory)
    {
        const string title = "Household records";
        const string receiptTitle = "Sideways receipt";
        var selected = model.Documents.Where(document => document.Title is "Paperwork packet" or receiptTitle).ToList();
        Require(selected.Count == 2, "The binder sources were unavailable.");
        Control<ListBox>(window, "DocumentList").SelectedItems = selected;
        var action = Control<Button>(window, "ExportHouseholdBinderButton");
        Require(action.IsEffectivelyVisible && action.IsEnabled, "The selected PDFs did not offer a binder export.");
        var destination = Directory.CreateTempSubdirectory("plico-verify-binder-");
        string? oldFolder = dialogs.FolderPath;
        int originalCount = model.Documents.Count;
        try
        {
            dialogs.FolderPath = destination.FullName;
            dialogs.BinderChoice = documents => ConfirmCombineOrderAsync(window, documents, title, evidenceDirectory, isBinder: true);
            await ((IAsyncRelayCommand)action.Command!).ExecuteAsync(action.CommandParameter);
            string exported = Path.Combine(destination.FullName, title + ".pdf");
            Require(File.Exists(exported) && model.HasBatchSelection && model.Documents.Count == originalCount,
                $"The binder did not export with the selection retained. {model.Status}");
            File.Copy(exported, Path.Combine(evidenceDirectory, "household-binder.pdf"), overwrite: true);
            using (var binder = PdfDocument.Open(exported))
            {
                string contents = ContentOrderTextExtractor.GetText(binder.GetPage(1));
                int firstTitle = contents.IndexOf(selected[1].Title, StringComparison.Ordinal);
                int secondTitle = contents.IndexOf(selected[0].Title, StringComparison.Ordinal);
                int receiptPage = selected[1].Title == receiptTitle ? 2 : selected[1].PageCount + 2;
                Require(binder.NumberOfPages == selected.Sum(document => document.PageCount) + 1 &&
                    contents.Contains(title, StringComparison.Ordinal) &&
                    firstTitle >= 0 && secondTitle > firstTitle &&
                    ContentOrderTextExtractor.GetText(binder.GetPage(receiptPage)).Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase),
                    "The binder did not retain the reordered titles and searchable receipt.");
            }
            await CaptureAsync(window, evidenceDirectory, "household-binder-exported.png");
        }
        finally
        {
            dialogs.BinderChoice = null;
            dialogs.FolderPath = oldFolder;
            model.LatestExportLocation = null;
            destination.Delete(recursive: true);
        }
    }
}
