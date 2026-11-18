using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Plico.Desktop;
using Plico.Core;
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

    [AvaloniaFact]
    public void ScannerPickerSurfacesMissingDriverAndDisablesCapture()
    {
        const string missingDriver = "SANE is unavailable.";
        var dialog = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [missingDriver])));
        dialog.Show();
        try
        {
            var capture = Assert.IsType<Button>(dialog.FindControl<Button>("StartButton"));
            var refresh = Assert.IsType<Button>(dialog.FindControl<Button>("RefreshButton"));
            var status = Assert.IsType<TextBlock>(dialog.FindControl<TextBlock>("ScannerStatus"));
            Assert.Equal(missingDriver, status.Text);
            Assert.False(capture.IsEnabled);
            Assert.True(refresh.IsEnabled);
        }
        finally
        {
            dialog.Close();
        }
    }
}
