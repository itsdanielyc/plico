namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Watches a source outside the managed PDF and metadata folders and distinct from an active source.</summary>
    public FolderWatch StartIntakeWatch(string folder, Func<IReadOnlyList<string>, CancellationToken, Task> import,
        FolderWatch? otherWatch, CancellationToken cancellationToken = default) =>
        FolderWatch.Start(folder, PdfDirectory, import, static source => new FileSystemWatcher(source),
            cancellationToken, _metadataDirectory, otherWatch);
}
