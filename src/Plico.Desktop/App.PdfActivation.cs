using System.ComponentModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Plico.Desktop;

public sealed partial class App
{
    private void AttachPdfActivation(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, InboxViewModel model)
    {
        var waiting = new Queue<string>();
        bool draining = false;
        bool opened = false;

        async Task DrainAsync()
        {
            if (draining || !opened) return;
            draining = true;
            try
            {
                while (waiting.Count != 0 && model.CanWork)
                {
                    string[] paths = waiting.ToArray();
                    waiting.Clear();
                    window.Activate();
                    await model.ImportAssociatedPdfsAsync(paths);
                }
            }
            finally
            {
                draining = false;
            }
        }

        void QueuePaths(string[] paths) => Dispatcher.UIThread.Post(() =>
        {
            foreach (string path in paths) waiting.Enqueue(path);
            _ = DrainAsync();
        });

        void OnActivation(object? sender, ActivatedEventArgs args)
        {
            if (args is FileActivatedEventArgs files)
                QueuePaths(PdfFileActivation.ReadPaths(files.Files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray()));
        }

        void OnModelChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(InboxViewModel.CanWork) && model.CanWork && waiting.Count != 0)
                Dispatcher.UIThread.Post(() => _ = DrainAsync());
        }

        var activatable = TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
        if (activatable is not null) activatable.Activated += OnActivation;
        var relay = OperatingSystem.IsMacOS() ? null : new PdfFileActivation(PdfFileActivation.UserPipeName, QueuePaths);
        QueuePaths(PdfFileActivation.ReadPaths(desktop.Args ?? []));
        model.PropertyChanged += OnModelChanged;
        window.Opened += (_, _) =>
        {
            opened = true;
            _ = DrainAsync();
        };
        desktop.Exit += (_, _) =>
        {
            if (activatable is not null) activatable.Activated -= OnActivation;
            model.PropertyChanged -= OnModelChanged;
            relay?.Dispose();
        };
    }
}
