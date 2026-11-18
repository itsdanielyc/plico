using Avalonia.Controls;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyPageTextViewAsync(Window window, InboxViewModel model, string directory)
    {
        var read = Control<Button>(window, "ReadPageTextButton");
        var text = Control<SelectableTextBlock>(window, "PageTextContent");
        var image = Control<ScrollViewer>(window, "PreviewScroll");
        var textScroll = Control<ScrollViewer>(window, "PageTextScroll");
        Require(read.IsVisible && read.IsEnabled && read.Content is "Read text", "The preview did not expose its page text.");
        read.Command!.Execute(null);
        await UntilAsync(() => text.Text?.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase) == true,
            () => model.PreviewMessage);
        Require(read.Content is "Show page" && textScroll.IsVisible && !image.IsVisible &&
                model.PageLabel == "1 / 1", "The OCR text did not replace the current PDF page in the preview.");
        await CaptureAsync(window, directory, "page-text.png");

        read.Command.Execute(null);
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        Require(read.Content is "Read text" && image.IsVisible && !textScroll.IsVisible,
            "The PDF did not return to its page preview.");
    }
}
