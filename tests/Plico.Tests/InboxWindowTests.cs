using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Plico.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Plico.Tests.HeadlessApplication))]

namespace Plico.Tests;

public static class HeadlessApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();
}

public sealed class InboxWindowTests
{
    [AvaloniaFact]
    public void OpensWithVisibleInboxHeading()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var heading = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("InboxHeading"));
            Assert.Equal("Inbox", heading.Text);
            Assert.True(heading.IsVisible);
            Assert.True(heading.Bounds.Width > 0);
            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            var add = Assert.IsType<Button>(window.FindControl<Button>("AddFilesButton"));
            Assert.True(search.Bounds.Width > add.Bounds.Width);
            Assert.True(add.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}
