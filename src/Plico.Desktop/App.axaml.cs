using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Plico.Desktop;

/// <summary>Composes the native window and installs the isolated release verification entry point.</summary>
public sealed partial class App : Application
{
    internal const string VerifySwitch = "--verify";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        WindowChrome.Register();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool verifying = desktop.Args is [VerifySwitch, _, _];
            var preference = new AppearancePreference(ApplicationSettings.DirectoryPath);
            var appearance = verifying ? new AppearanceOptions(AppearanceTheme.Light, InterfaceLanguage.English) : preference.Load();
            RequestedThemeVariant = appearance.RequestedTheme;
            DesktopText.SetLanguage(appearance.Language);
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
                var testAppearance = new AppearancePreference(settingsDirectory);
                model.InitializeAppearance(appearance, chosen => ApplyAppearance(testAppearance, chosen));
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
                model.InitializeAppearance(appearance, chosen => ApplyAppearance(preference, chosen));
                model.InitializeLibrary(LibraryPreference.LoadOrCreateDefault);
                var recognition = new OcrLanguagePreference(settingsDirectory);
                model.InitializeRecognitionLanguage(recognition.Load, recognition.Save);
                model.InitializeLocalModels(new LocalModelPreference(settingsDirectory));
                model.InitializeOcrProcessing(new OcrProcessingPreference(settingsDirectory));
                window.DataContext = model;
                AttachPdfActivation(desktop, window, model);
                desktop.Exit += (_, _) => model.Dispose();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyAppearance(AppearancePreference preference, AppearanceOptions options)
    {
        preference.Save(options);
        RequestedThemeVariant = options.RequestedTheme;
    }
}
