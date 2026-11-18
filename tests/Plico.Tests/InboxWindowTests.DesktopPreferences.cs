using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [Fact]
    public void DesktopPreferencesRoundTripAndRecoverAnInvalidSetting()
    {
        using var temporary = new TemporaryLibrary();
        var store = new AppearancePreference(temporary.Path);
        Assert.Equal(new AppearanceOptions(AppearanceTheme.System, InterfaceLanguage.System), store.Load());
        store.Save(new AppearanceOptions(AppearanceTheme.Dark, InterfaceLanguage.French));
        Assert.Equal(new AppearanceOptions(AppearanceTheme.Dark, InterfaceLanguage.French), store.Load());
        Assert.Equal("Ajouter des fichiers", DesktopText.ByKey("AddFiles", InterfaceLanguage.French));
        Assert.Equal("Add files", DesktopText.ByKey("AddFiles", InterfaceLanguage.English));
        File.WriteAllText(Path.Combine(temporary.Path, "appearance.json"), "{interrupted");
        Assert.Equal(new AppearanceOptions(AppearanceTheme.System, InterfaceLanguage.System), store.Load());
    }

    [AvaloniaFact]
    public async Task PreviewThumbnailsNavigateAndZoomRendersAtAVisibleScale()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
            await library.ImportAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "Mixed Pages.pdf"), TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            model.TogglePageThumbnailsCommand.Execute(null);
            await WaitForAsync(() => model.PageThumbnails.Count == 2);
            Assert.Equal(new[] { 1, 2 }, model.PageThumbnails.Select(thumb => thumb.PageNumber));
            model.ShowThumbnail(2);
            await WaitForPreviewAsync(model);
            Assert.Equal("2 / 2", model.PageLabel);
            model.ZoomInCommand.Execute(null);
            await WaitForPreviewAsync(model);
            Assert.Equal(100, model.PreviewZoomPercent);
            Assert.True(model.PreviewDisplayWidth > 100);
            model.TogglePreviewFitCommand.Execute(null);
            await WaitForPreviewAsync(model);
            Assert.Equal(0, model.PreviewZoomPercent);
            Assert.True(double.IsNaN(model.PreviewDisplayWidth));
        }
        finally
        {
            window.Close();
        }
    }
}
