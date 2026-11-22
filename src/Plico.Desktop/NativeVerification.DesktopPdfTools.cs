using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PdfSharpCore.Pdf.IO;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private const double MinimumPreviewViewport = 96;
    private const double LayoutRoundingTolerance = 1;

    private static async Task VerifyDesktopPdfToolsAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string evidence, string fixture)
    {
        var actions = Control<ScrollViewer>(window, "DocumentActionsScroll");
        actions.Offset = default;
        await UntilAsync(() => IsInsideViewport(Control<Button>(window, "ReviewButton"), window) &&
            IsInsideViewport(Control<Button>(window, "TrashDocumentButton"), window),
            () => "Mark reviewed and Trash must stay on the filing card at the initial window size.");
        var searchableCopy = Control<Button>(window, "CreateSearchableCopyButton");
        searchableCopy.BringIntoView();
        await UntilAsync(() => IsInsideViewport(searchableCopy, actions), () => "The OCR copy action must scroll into the filing card.");
        actions.Offset = default;
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        await UntilAsync(() => window.Bounds.Width <= window.MinWidth + LayoutRoundingTolerance &&
            window.Bounds.Height <= window.MinHeight + LayoutRoundingTolerance,
            () => $"Minimum window requested at {window.MinWidth} × {window.MinHeight}. Actual {window.Bounds.Size}.");
        await UntilAsync(() => model.Preview is not null && model.PageLabel == "1 / 2", () => model.Status);
        foreach (string tag in new[] { "Archived household paperwork", "House maintenance", "Renewal and utility records", "Family binder" })
        {
            model.TagInput = tag;
            model.AddTagCommand.Execute(null);
        }
        Require(model.SelectedDocument is { Tags.Count: >= 4 }, "The compact-window fixture did not keep its tags.");
        var thumbnails = Control<ToggleButton>(window, "PageThumbnailsButton");
        thumbnails.Command!.Execute(thumbnails.CommandParameter);
        var thumbnailList = Control<ItemsControl>(window, "PageThumbnailList");
        Button? SecondThumbnail() => thumbnailList.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => button.DataContext is PdfPageThumbnail { PageNumber: 2 });
        await UntilAsync(() => model.PageThumbnails.Count == 2 && SecondThumbnail() is not null, () => model.Status);
        var pageTwo = SecondThumbnail()!;
        pageTwo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => model.Preview is not null && model.PageLabel == "2 / 2", () => model.Status);
        var previewScroll = Control<ScrollViewer>(window, "PreviewScroll");
        Control<Button>(window, "ZoomInButton").Command!.Execute(null);
        await UntilAsync(() => model.Preview is not null && model.PreviewZoomPercent == 100 &&
            previewScroll.Offset.X > 0 && previewScroll.Offset.Y > 0, () => model.Status);
        await CaptureAsync(window, evidence, "pdf-thumbnails-and-zoom.png");
        Require(previewScroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && previewScroll.Viewport.Height >= MinimumPreviewViewport,
            $"The zoomed PDF could not scroll or fit a usable viewport. Preview height {previewScroll.Viewport.Height}.");
        await RequireCompactDocumentActionsAsync(window);
        foreach (string actionName in new[] { "FindRelatedButton", "SuggestLocalFilingButton" })
        {
            var action = Control<Button>(window, actionName);
            action.BringIntoView();
            await UntilAsync(() => IsInsideViewport(action, actions), () => $"The document action {actionName} was outside the compact tray.");
        }
        var rotate = Control<Button>(window, "RotatePdfPagesButton");
        rotate.BringIntoView();
        await UntilAsync(() => actions.Offset.Y > 0 && IsInsideViewport(rotate, actions),
            () => "Scrolling the compact tray must reveal its lower PDF actions.");
        actions.Offset = default;
        model.TogglePreviewFitCommand.Execute(null);
        model.TogglePageThumbnailsCommand.Execute(null);

        dialogs.AppearanceChoice = async options =>
        {
            var picker = new AppearanceDialog(options);
            var selected = picker.ShowDialog<AppearanceOptions?>(window);
            Control<ComboBox>(picker, "ThemeBox").SelectedIndex = (int)AppearanceTheme.Dark;
            Control<ComboBox>(picker, "LanguageBox").SelectedIndex = (int)InterfaceLanguage.French;
            await CaptureAsync(picker, evidence, "desktop-appearance.png");
            Control<Button>(picker, "SaveAppearance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await selected;
        };
        try
        {
            await model.ShowAppearanceCommand.ExecuteAsync(null);
            // The window is the desk, so dark mode shows the family's dark Base, #0B0B0C (it was the old
            // workspace grey, #1C1C1F, before the miille tokens).
            await UntilAsync(() => model.Preview is not null && window.ActualThemeVariant == ThemeVariant.Dark &&
                window.Background is ISolidColorBrush { Color: { R: 11, G: 11, B: 12 } }, () => model.Status);
            await CaptureAsync(window, evidence, "desktop-dark.png");

            DesktopText.SetLanguage(InterfaceLanguage.French);
            var localizedLibrary = Directory.CreateTempSubdirectory("plico-verify-french-");
            var localizedModel = new InboxViewModel(dialogs);
            var localized = new MainWindow { DataContext = localizedModel, Width = window.MinWidth, Height = window.MinHeight };
            try
            {
                localizedModel.InitializeLibrary(() => localizedLibrary.FullName);
                localized.Show();
                string sample = Path.Combine(Path.GetDirectoryName(fixture)!, "Lease Renewal.pdf");
                await localizedModel.ImportAssociatedPdfsAsync([sample]);
                await UntilAsync(() => localizedModel.Preview is not null, () => localizedModel.Status);
                Require(Control<Button>(localized, "AddFilesButton").Content as string == "Ajouter des fichiers" &&
                    Control<TextBlock>(localized, "InboxHeading").Text == "Boîte de réception", "The French labels did not load.");
                await RequireCompactDocumentActionsAsync(localized);
                Require(Control<ScrollViewer>(localized, "PreviewScroll").Viewport.Height >= MinimumPreviewViewport,
                    "The French interface crowded the PDF viewport at the minimum window size.");
                await CaptureAsync(localized, evidence, "desktop-french.png");
            }
            finally
            {
                localized.Close();
                localizedModel.Dispose();
                DesktopText.SetLanguage(InterfaceLanguage.English);
                localizedLibrary.Delete(recursive: true);
            }
        }
        finally
        {
            dialogs.AppearanceChoice = null;
        }

        var external = Directory.CreateTempSubdirectory("plico-verify-open-with-");
        try
        {
            string incoming = Path.Combine(external.FullName, "Association example.pdf");
            using (var pdf = PdfReader.Open(Path.Combine(Path.GetDirectoryName(fixture)!, "Lease Renewal.pdf"), PdfDocumentOpenMode.Modify))
            {
                pdf.Info.Title = "PDF Open With sample";
                pdf.Save(incoming);
            }
            byte[] original = File.ReadAllBytes(incoming);
            await model.ImportAssociatedPdfsAsync(PdfFileActivation.ReadPaths([incoming]));
            Require(model.SelectedDocument is { Title: "Association example", HasText: true } &&
                original.AsSpan().SequenceEqual(File.ReadAllBytes(incoming)), $"The PDF association import failed. {model.Status}");
            await UntilAsync(() => model.Preview is not null, () => model.Status);
            await CaptureAsync(window, evidence, "pdf-open-with-import.png");
        }
        finally
        {
            external.Delete(recursive: true);
        }
    }

    /// <summary>At the minimum size, review and Trash stay pinned to the card and every PDF action scrolls into view.</summary>
    private static async Task RequireCompactDocumentActionsAsync(MainWindow window)
    {
        var card = Control<ScrollViewer>(window, "DocumentActionsScroll");
        Require(IsInsideViewport(Control<Button>(window, "ReviewButton"), window) &&
            IsInsideViewport(Control<Button>(window, "TrashDocumentButton"), window),
            "Mark reviewed and Trash must remain visible at the minimum window size.");
        foreach (string name in new[] { "PdfToolsButton", "ExportPdfButton" })
        {
            var action = Control<Button>(window, name);
            action.BringIntoView();
            await UntilAsync(() => IsInsideViewport(action, card), () => $"{name} could not scroll into the filing card at the minimum size.");
        }
        card.Offset = default;
    }

    /// <summary>Waits until row thumbnails, suggestions and page renders have released the library's files.</summary>
    private static Task SettleAsync(InboxViewModel model) =>
        UntilAsync(() => !model.IsReadingInBackground, () => "Thumbnails and suggestions were still reading the library.");

    private static bool IsInsideViewport(Control control, Control viewport) =>
        control.IsEffectivelyVisible && control.TranslatePoint(default, viewport) is { } origin &&
        origin.X >= -LayoutRoundingTolerance && origin.Y >= -LayoutRoundingTolerance &&
        origin.X + control.Bounds.Width <= viewport.Bounds.Width + LayoutRoundingTolerance &&
        origin.Y + control.Bounds.Height <= viewport.Bounds.Height + LayoutRoundingTolerance;
}
