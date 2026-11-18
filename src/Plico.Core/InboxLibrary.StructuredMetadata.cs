namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Reads the reusable contacts, document types and field definitions.</summary>
    public MetadataDefinitions GetMetadataDefinitions() => _catalog.ReadMetadataDefinitions();

    /// <summary>Reads one document's assignments by its stable catalog identity.</summary>
    public DocumentMetadata ReadDocumentMetadata(long documentId) => _catalog.ReadDocumentMetadata(documentId);

    /// <summary>Validates and replaces all structured assignments for a present document in one transaction.</summary>
    public void SaveDocumentMetadata(long documentId, DocumentMetadata metadata, CancellationToken cancellationToken = default) =>
        _catalog.SaveDocumentMetadata(documentId, metadata, cancellationToken);

    /// <summary>Creates or edits a correspondent and its aliases. Names and aliases are unique in the library.</summary>
    public Correspondent SaveCorrespondent(long? id, string name, IReadOnlyList<string> aliases) =>
        _catalog.SaveCorrespondent(id, name, aliases);

    /// <summary>Creates or edits a reusable document type.</summary>
    public CatalogDocumentType SaveDocumentType(long? id, string name) => _catalog.SaveDocumentType(id, name);

    /// <summary>Creates or edits a typed field, preserving existing values across definition changes.</summary>
    public CustomFieldDefinition SaveCustomField(long? id, string name, CustomFieldKind kind, IReadOnlyList<string>? choices = null) =>
        _catalog.SaveCustomField(id, name, kind, choices ?? []);

    /// <summary>Suggests known correspondents whose names or aliases occur in the document.</summary>
    public IReadOnlyList<Correspondent> SuggestCorrespondents(long documentId, CancellationToken cancellationToken = default) =>
        _catalog.SuggestCorrespondents(documentId, GetMetadataDefinitions().Correspondents, cancellationToken);

    /// <summary>Lists the library's tags and their parent and colour assignments.</summary>
    public IReadOnlyList<TagAppearance> GetTagAppearances() => _catalog.ReadTagAppearances();

    /// <summary>Changes a tag's parent and colour while checking for hierarchy cycles.</summary>
    public void SaveTagAppearance(string label, string? parentLabel, string? colorHex) =>
        _catalog.SaveTagAppearance(label, parentLabel, colorHex);

    /// <summary>Lists link targets by title, keeping existing links to missing or trashed documents resolvable.</summary>
    public IReadOnlyList<MetadataLinkTarget> GetMetadataLinkTargets(long exceptDocumentId, CancellationToken cancellationToken = default) =>
        _catalog.ReadMetadataLinkTargets(exceptDocumentId, cancellationToken);
}
