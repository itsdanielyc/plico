using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyBatchTagRemovalAsync(Window window, InboxViewModel model, FixtureDialogs dialogs,
        string sharedTag, string evidenceDirectory)
    {
        const string removalTag = "Removal check";
        var list = Control<ListBox>(window, "DocumentList");
        list.SelectedItems = model.Documents.Take(1).ToList();
        Control<AutoCompleteBox>(window, "TagBox").SetCurrentValue(AutoCompleteBox.TextProperty, removalTag);
        Control<Button>(window, "AddTagButton").Command!.Execute(null);
        Require(model.Documents.Count(document => document.Tags.Contains(removalTag)) == 1,
            "The removal fixture must start with the tag on one selected PDF.");

        Control<Button>(window, "SelectShownButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var remove = Control<Button>(window, "RemoveBatchTagButton");
        Require(remove.IsEffectivelyVisible && remove.IsEnabled && model.HasBatchSelection,
            "The selected PDFs did not offer tag removal.");
        dialogs.TagRemovalChoice = options => SelectTagForRemovalAsync(window, options, removalTag, sharedTag, evidenceDirectory);
        try
        {
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => model.Status == "Tag removed from 1 document." && model.HasBatchSelection, () => model.Status);
            Require(model.Documents.All(document => !document.Tags.Contains(removalTag) && document.Tags.Contains(sharedTag)),
                "Tag removal changed an unrelated tag or left the chosen tag on a selected PDF.");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
            await CaptureAsync(window, evidenceDirectory, "batch-tag-removed.png");
        }
        finally
        {
            dialogs.TagRemovalChoice = null;
        }
    }

    private static async Task<string?> SelectTagForRemovalAsync(Window owner, IReadOnlyList<BatchTagOption> options,
        string removalTag, string sharedTag, string evidenceDirectory)
    {
        var option = options.Single(item => item.Label == removalTag);
        Require(option.DocumentCount == 1 && options.Any(item => item.Label == sharedTag && item.DocumentCount == 2),
            "The dialog tag counts differed from the selected PDFs.");
        var dialog = new BatchTagRemovalDialog(options);
        var completion = dialog.ShowDialog<string?>(owner);
        try
        {
            var confirm = Control<Button>(dialog, "ConfirmTagRemovalButton");
            Require(!confirm.IsEnabled, "Tag removal was enabled before choosing a tag.");
            Control<ListBox>(dialog, "BatchTagChoices").SelectedItem = option;
            Require(confirm.IsEnabled, "Choosing an existing tag did not enable the confirm action.");
            await CaptureAsync(dialog, evidenceDirectory, "batch-tag-removal.png");
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await completion;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await completion;
        }
    }
}
