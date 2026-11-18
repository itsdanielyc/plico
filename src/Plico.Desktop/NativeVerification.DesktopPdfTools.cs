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
    private static async Task VerifyDesktopPdfToolsAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string evidence, string fixture)
    {
        await UntilAsync(() => model.Preview is not null && model.PageLabel == "1 / 2", () => model.Status);
        var thumbnails = Control<ToggleButton>(window, "PageThumbnailsButton");
        thumbnails.Command!.Execute(thumbnails.CommandParameter);
        var thumbnailList = Control<ItemsControl>(window, "PageThumbnailList");
        Button? SecondThumbnail() => thumbnailList.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => button.DataContext is PdfPageThumbnail { PageNumber: 2 });
        await UntilAsync(() => model.PageThumbnails.Count == 2 && SecondThumbnail() is not null, () => model.Status);
        var pageTwo = SecondThumbnail()!;
        pageTwo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => model.Preview is not null && model.PageLabel == "2 / 2", () => model.Status);
        Control<Button>(window, "ZoomInButton").Command!.Execute(null);
        await UntilAsync(() => model.Preview is not null && model.PreviewZoomPercent == 100 && model.PreviewDisplayWidth > 0, () => model.Status);
        Require(Control<ScrollViewer>(window, "PreviewScroll").HorizontalScrollBarVisibility ==
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, "The zoomed PDF could not scroll horizontally.");
        await CaptureAsync(window, evidence, "pdf-thumbnails-and-zoom.png");
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
            await UntilAsync(() => window.ActualThemeVariant == ThemeVariant.Dark &&
                window.Background is ISolidColorBrush { Color: { R: 23, G: 36, B: 29 } }, () => model.Status);
            await CaptureAsync(window, evidence, "desktop-dark.png");

            DesktopText.SetLanguage(InterfaceLanguage.French);
            using var localizedModel = new InboxViewModel(dialogs);
            var localized = new MainWindow { DataContext = localizedModel };
            try
            {
                localized.Show();
                Require(Control<Button>(localized, "AddFilesButton").Content as string == "Ajouter des fichiers" &&
                    Control<TextBlock>(localized, "InboxHeading").Text == "Boîte de réception", "The French labels did not load.");
                await CaptureAsync(localized, evidence, "desktop-french.png");
            }
            finally
            {
                localized.Close();
                DesktopText.SetLanguage(InterfaceLanguage.English);
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
}
