using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyStructuredMetadataAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidence)
    {
        const string note = "Renew each March.";
        dialogs.MetadataDefinitionsChoice = library =>
        {
            library.SaveDocumentType(null, "Agreement");
            library.SaveCorrespondent(null, "Northstar", []);
            return Task.CompletedTask;
        };
        try
        {
            await model.ManageMetadataCommand.ExecuteAsync(null);
            dialogs.MetadataChoice = async input =>
            {
                Require(input.Definitions.DocumentTypes.Any(type => type.Name == "Agreement"), "Document types did not reach the metadata editor.");
                var editor = new DocumentMetadataDialog(input);
                var result = editor.ShowDialog<Plico.Core.DocumentMetadata?>(window);
                try
                {
                    await UntilAsync(() => editor.GetVisualDescendants().OfType<TextBox>().Any(box => box.Name == "MetadataNotes"),
                        () => "Metadata notes input did not open.");
                    editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "MetadataNotes").Text = note;
                    await CaptureAsync(editor, evidence, "document-metadata.png");
                    editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "SaveDocumentMetadataButton")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return await result;
                }
                finally
                {
                    if (editor.IsVisible) editor.Close();
                }
            };
            var edit = (IAsyncRelayCommand)Control<Button>(window, "EditMetadataButton").Command!;
            await edit.ExecuteAsync(null);
            Require(model.Status == "Document metadata saved.", model.Status);
            bool verified = false;
            dialogs.MetadataChoice = input =>
            {
                verified = input.Metadata.Notes == note;
                return Task.FromResult<Plico.Core.DocumentMetadata?>(null);
            };
            await edit.ExecuteAsync(null);
            Require(verified, "The saved metadata was absent when reopening the editor.");
        }
        finally
        {
            dialogs.MetadataChoice = null;
            dialogs.MetadataDefinitionsChoice = null;
        }
    }
}
