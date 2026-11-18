using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Plico.Desktop;

/// <summary>Composes the native window and installs the isolated release verification entry point.</summary>
public sealed class App : Application
{
    internal const string VerifySwitch = "--verify";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (desktop.Args is [VerifySwitch, var evidenceDirectory, var fixture])
            {
                var testLibrary = Directory.CreateTempSubdirectory("plico-verify-");
                var model = new InboxViewModel(new FixtureDialogs(fixture));
                model.InitializeLibrary(() => testLibrary.FullName);
                window.DataContext = model;
                desktop.Exit += (_, _) =>
                {
                    model.Dispose();
                    testLibrary.Delete(recursive: true);
                };
                window.Opened += (_, _) => Dispatcher.UIThread.Post(
                    () => _ = NativeVerification.RunAsync(desktop, window, model, evidenceDirectory, fixture),
                    DispatcherPriority.Background);
            }
            else
            {
                var model = new InboxViewModel(new WorkspaceDialogs(window), LibraryPreference.Save);
                model.InitializeLibrary(LibraryPreference.LoadOrCreateDefault);
                window.DataContext = model;
                desktop.Exit += (_, _) => model.Dispose();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
