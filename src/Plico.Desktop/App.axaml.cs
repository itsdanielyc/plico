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
            if (desktop.Args is [VerifySwitch, var evidenceDirectory])
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(
                    () => NativeVerification.Run(desktop, window, evidenceDirectory),
                    DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
