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
                var incoming = Directory.CreateDirectory(Path.Combine(testLibrary.FullName, "Incoming"));
                var dialogs = new FixtureDialogs(fixture)
                {
                    FolderPath = incoming.FullName,
                    EditedTitle = NativeVerification.EditedDocumentTitle
                };
                string settingsDirectory = Path.Combine(testLibrary.FullName, "Settings");
                var model = new InboxViewModel(dialogs, watchPreference: new WatchFolderPreference(settingsDirectory),
                    importFailureStore: new ImportFailureStore(settingsDirectory),
                    intakeFolderPreference: new IntakeFolderPreference(settingsDirectory));
                model.InitializeLibrary(() => testLibrary.FullName);
                window.DataContext = model;
                desktop.Exit += (_, _) =>
                {
                    model.Dispose();
                    testLibrary.Delete(recursive: true);
                };
                window.Opened += (_, _) => Dispatcher.UIThread.Post(
                    () => _ = NativeVerification.RunAsync(desktop, window, model, dialogs, evidenceDirectory, fixture),
                    DispatcherPriority.Background);
            }
            else
            {
                string settingsDirectory = ApplicationSettings.DirectoryPath;
                var model = new InboxViewModel(new WorkspaceDialogs(window), LibraryPreference.Save, new WatchFolderPreference(settingsDirectory),
                    importFailureStore: new ImportFailureStore(settingsDirectory),
                    intakeFolderPreference: new IntakeFolderPreference(settingsDirectory));
                model.InitializeLibrary(LibraryPreference.LoadOrCreateDefault);
                var recognition = new OcrLanguagePreference(settingsDirectory);
                model.InitializeRecognitionLanguage(recognition.Load, recognition.Save);
                model.InitializeOcrProcessing(new OcrProcessingPreference(settingsDirectory));
                window.DataContext = model;
                desktop.Exit += (_, _) => model.Dispose();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
