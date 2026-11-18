using Plico.Core;

namespace Plico.Desktop;

internal partial interface IWorkspaceDialogs
{
    Task<DocumentMetadata?> EditStructuredMetadataAsync(MetadataEditInput input);
    Task ManageMetadataDefinitionsAsync(InboxLibrary library);
}

internal sealed partial class WorkspaceDialogs
{
    public Task<DocumentMetadata?> EditStructuredMetadataAsync(MetadataEditInput input) =>
        new DocumentMetadataDialog(input).ShowDialog<DocumentMetadata?>(owner);

    public Task ManageMetadataDefinitionsAsync(InboxLibrary library) => new MetadataDefinitionsDialog(library).ShowDialog(owner);
}

internal sealed partial class FixtureDialogs
{
    internal Func<MetadataEditInput, Task<DocumentMetadata?>>? MetadataChoice { get; set; }
    internal Func<InboxLibrary, Task>? MetadataDefinitionsChoice { get; set; }
    public Task<DocumentMetadata?> EditStructuredMetadataAsync(MetadataEditInput input) =>
        MetadataChoice?.Invoke(input) ?? Task.FromResult<DocumentMetadata?>(null);

    public Task ManageMetadataDefinitionsAsync(InboxLibrary library) => MetadataDefinitionsChoice?.Invoke(library) ?? Task.CompletedTask;
}
