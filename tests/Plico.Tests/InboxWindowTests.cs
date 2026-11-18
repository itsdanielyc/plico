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
        var window = new MainWindow();
        window.Show();
        try
        {
            var heading = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("InboxHeading"));
            Assert.Equal("Inbox", heading.Text);
            Assert.True(heading.IsVisible);
            Assert.True(heading.Bounds.Width > 0);
        }
        finally
        {
            window.Close();
        }
    }
}
