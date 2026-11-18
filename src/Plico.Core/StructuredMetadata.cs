using System.Text.Json.Serialization;

namespace Plico.Core;

/// <summary>A known sender or organization and its alternate searchable names.</summary>
public sealed record Correspondent(long Id, string Name, IReadOnlyList<string> Aliases);

/// <summary>A reusable document classification.</summary>
public sealed record CatalogDocumentType(long Id, string Name);

/// <summary>The storage and validation type of a library-wide custom field.</summary>
public enum CustomFieldKind { Text, Boolean, Date, Url, Integer, Decimal, Money, DocumentLink, Choice }

/// <summary>A named field that can be filled on any document in the library.</summary>
public sealed record CustomFieldDefinition(long Id, string Name, CustomFieldKind Kind, IReadOnlyList<string> Choices);

/// <summary>A value with the type declared by its custom field definition.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CustomFieldValue.Text), "text")]
[JsonDerivedType(typeof(CustomFieldValue.Boolean), "boolean")]
[JsonDerivedType(typeof(CustomFieldValue.Date), "date")]
[JsonDerivedType(typeof(CustomFieldValue.Url), "url")]
[JsonDerivedType(typeof(CustomFieldValue.Integer), "integer")]
[JsonDerivedType(typeof(CustomFieldValue.Decimal), "decimal")]
[JsonDerivedType(typeof(CustomFieldValue.Money), "money")]
[JsonDerivedType(typeof(CustomFieldValue.DocumentLink), "documentLink")]
[JsonDerivedType(typeof(CustomFieldValue.Choice), "choice")]
public abstract record CustomFieldValue
{
    /// <summary>Plain text.</summary>
    public sealed record Text(string Value) : CustomFieldValue;
    /// <summary>A confirmed yes or no value.</summary>
    public sealed record Boolean(bool Value) : CustomFieldValue;
    /// <summary>A calendar date without a time or time zone.</summary>
    public sealed record Date(DateOnly Value) : CustomFieldValue;
    /// <summary>An absolute HTTP or HTTPS address.</summary>
    public sealed record Url(string Value) : CustomFieldValue;
    /// <summary>A signed whole number.</summary>
    public sealed record Integer(long Value) : CustomFieldValue;
    /// <summary>A base-ten number.</summary>
    public sealed record Decimal(decimal Value) : CustomFieldValue;
    /// <summary>A base-ten amount with an optional three-letter currency code.</summary>
    public sealed record Money(decimal Amount, string? Currency) : CustomFieldValue;
    /// <summary>The stable identity of another catalog document.</summary>
    public sealed record DocumentLink(long DocumentId) : CustomFieldValue;
    /// <summary>A label chosen from the field definition.</summary>
    public sealed record Choice(string Label) : CustomFieldValue;
}

/// <summary>A custom field and the value assigned to it.</summary>
public sealed record DocumentField(long FieldId, CustomFieldValue Value);

/// <summary>Structured metadata assigned to a single catalog document.</summary>
public sealed record DocumentMetadata(long? DocumentTypeId, IReadOnlyList<long> CorrespondentIds, string Notes,
    IReadOnlyList<DocumentField> Fields)
{
    /// <summary>The default metadata for a document without assignments.</summary>
    public static DocumentMetadata Empty { get; } = new(null, [], "", []);
}

/// <summary>Definitions used by metadata editors and portable exports.</summary>
public sealed record MetadataDefinitions(IReadOnlyList<Correspondent> Correspondents,
    IReadOnlyList<CatalogDocumentType> DocumentTypes, IReadOnlyList<CustomFieldDefinition> CustomFields);

/// <summary>A tag's label, optional parent label, and optional RGB colour.</summary>
public sealed record TagAppearance(string Label, string? ParentLabel, string? ColorHex);
