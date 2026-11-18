namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    internal Task ImportAssociatedPdfsAsync(string[] paths)
    {
        if (!CanWork || paths.Length == 0) return Task.CompletedTask;
        // An Open With request for the PDF already displayed in this library needs only to activate the window.
        if (SelectedDocument is { } current && _library is { } library)
        {
            string active = library.ResolvePdf(current);
            paths = paths.Where(path => Path.GetRelativePath(active, path) != ".").ToArray();
        }
        if (paths.Length == 0) return Task.CompletedTask;
        return RunOperationAsync(DesktopText.Translate("Opening PDFs…"), token => ImportFilesAsync(paths, token));
    }
}
