using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// The French interface reads as French wherever the branch redrew it: names in toasts in French quotation marks, the
/// recognition languages and the catch-up in Settings in French, and the welcome page whole at the smallest window.
/// The count line keeps each number with its noun in either language.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task FrenchToastsQuoteNamesTheFrenchWay()
    {
        DesktopText.SetLanguage(InterfaceLanguage.French);
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            ClickAt(window, Assert.IsType<AutoCompleteBox>(window.FindControl<AutoCompleteBox>("TagBox")));
            TypeText(window, "Impôts");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(("Étiquette ajoutée", "«\u202FImpôts\u202F»"), (model.ToastTitle, model.ToastBody));

            TypeText(window, "impôts");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal("«\u202FImpôts\u202F»", model.ToastBody);

            // A click on the heading opens the title, which typing replaces.
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("EditTitleButton")));
            TypeText(window, "Bail d’avril");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal("«\u202FBail d’avril\u202F»", model.ToastBody);
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task FrenchSettingsNamesTheRecognitionLanguagesAndTheCatchUpInFrench()
    {
        DesktopText.SetLanguage(InterfaceLanguage.French);
        using var temporary = new TemporaryLibrary();
        using var watched = new TemporaryLibrary();
        using var settingsFolder = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var dialogs = new FixtureDialogs { FolderPath = watched.Path };
        using var model = new InboxViewModel(dialogs, watchPreference: new WatchFolderPreference(settingsFolder.Path));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var settings = OpenSettingsAt(window, "RecognitionGroup");
            var languages = Assert.IsType<ComboBox>(settings.FindControl<ComboBox>("OcrLanguageBox"));
            string[] shown = VisibleTexts(languages);
            Assert.True(shown.Contains("Anglais"), $"The recognition language reads “{string.Join(" / ", shown)}” in French Settings.");

            // The list names every language in French, and choosing one names it the same way on the status card.
            ClickAt(settings, languages);
            Assert.True(languages.IsDropDownOpen);
            var german = Assert.IsType<ComboBoxItem>(languages.ContainerFromItem(OcrLanguage.German));
            Assert.Contains("Allemand", VisibleTexts(german));
            ClickAt(settings, german);
            Assert.Equal(OcrLanguage.German, model.RecognitionLanguage);
            Assert.Contains("Allemand", VisibleTexts(languages));
            Assert.StartsWith("Allemand ·", model.StatusCardDetail, StringComparison.Ordinal);

            // With a watch running, the catch-up reads in French too.
            settings.ShowSection("IntakeGroup");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            ClickAt(settings, Assert.IsType<Button>(settings.FindControl<Button>("StartFolderWatchButton")));
            await WaitForAsync(() => !model.IsBusy && model.IsWatchingFolder);
            settings.UpdateLayout();
            var catchUp = Assert.IsType<Button>(settings.FindControl<Button>("ImportWatchFolderFilesButton"));
            Assert.Equal("Importer les fichiers existants", catchUp.Content);
            settings.Close();
        }
        finally
        {
            await model.PrepareCloseAsync();
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public void TheFrenchWelcomePageFitsTheMinimumWindow()
    {
        DesktopText.SetLanguage(InterfaceLanguage.French);
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            Assert.True(model.ShowWelcome);
            window.UpdateLayout();
            var page = Assert.IsType<StackPanel>(window.FindControl<StackPanel>("WelcomePage"));
            var scroll = Assert.IsType<ScrollViewer>(page.Parent);
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1, $"The French welcome page needs {scroll.Extent} in {scroll.Viewport}.");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);

            // Off Windows the privacy line says "cet ordinateur" rather than "ce PC"; that wording fits as well.
            var privacy = page.GetVisualDescendants().OfType<TextBlock>().SelectMany(text => text.Inlines ?? [])
                .OfType<Avalonia.Controls.Documents.Run>().Single(run => run.Text == PlatformText.PrivacyLine);
            foreach (string wording in new[] { DesktopText.ByKey("PrivacyShortPc"), DesktopText.ByKey("PrivacyShortComputer") })
            {
                privacy.Text = wording;
                window.UpdateLayout();
                Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1, $"With “{wording}” the French welcome page needs {scroll.Extent} in {scroll.Viewport}.");
            }
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task TheCountLineKeepsEachNumberWithItsNoun()
    {
        using var temporary = new TemporaryLibrary();
        SeedReviewQueue(temporary.Path, InboxLibrary.DefaultResultPageSize + 1, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1180, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("AllDocumentsButton")));
            await WaitForAsync(() => model.HasMore);
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            var header = Assert.IsType<Border>(window.FindControl<Border>("QueueHeader"));
            var count = header.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == model.QueueCountLabel);
            Assert.StartsWith("Showing 60", count.Text, StringComparison.Ordinal);
            string text = count.Text!;
            string[] lines = count.TextLayout.TextLines.Select(line => text.Substring(line.FirstTextSourceIndex, Math.Min(line.Length, text.Length - line.FirstTextSourceIndex)).Trim()).ToArray();
            Assert.False(lines.Any(line => line.EndsWith("60", StringComparison.Ordinal)),
                $"The count line breaks as “{string.Join(" | ", lines)}”, parting the number from its noun.");
        }
        finally
        {
            window.Close();
        }
    }
}
