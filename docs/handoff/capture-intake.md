# Capture and intake handoff

**Date** 2026-11-18 · **Branches** `build/capture-intake` and `build/metadata-reminders`

## #16 Phone inbox

The capture lane can edit the watch controls in `MainWindow.axaml`, the watch lifecycle calls in `InboxViewModel.cs` and `MainWindow.axaml.cs`, and the journey call in `NativeVerification.cs`. Keep implementation and tests in feature-specific files where practical. Append the slice and its evidence to `PLAN.md` and `VERIFICATION.md` on the feature branch.

Store the phone path separately per library on this machine. Each watch has its own resume, catch-up, and stop actions. Use the existing watched intake routing and serialized operation queue for both. On library switch or app close, stop and await both watchers. Reject each resolved folder that aliases the other watch or points back into library-managed storage.

The release scope for this slice is file delivery through a watched folder. Document the phone sender options from the brief. An in-app listener is a separate opt-in proposal.

Verify overlapping arrivals, an empty file that later receives content, independent stop and resume, and a library switch with both watches active. Run the native journey on all six hosted targets.

## #12 Archive serial numbers

Use one ASN namespace per library. Expose an optional positive `long` and store assigned values in `document_asns`, keyed by document ID with a unique constraint on the ASN and cascade deletion on purge. A document retains its ASN across moves, backups, temporary unavailability, and Trash. New document identities from copying or splitting start unassigned unless a new ASN is explicitly supplied.

Reapplying the same number to the same document succeeds. A different document claiming the number receives a conflict with the prior assignment intact. Check the conflict before publication where possible and preserve the capture or import source for recovery. Provide exact lookup and include the ASN in portable metadata.

The coordinator owns registration in `LibraryCatalog.Migrations` and the shared import, search, and export projections. #10 uses schema v13. Register the ASN migration after v13 when the feature integrates. Capture owns its new partials and can build manual duplex, separator sheets, label layout, and fixtures independently of migration registration.

## #18 Migration metadata

Adapter parsers can target the following proposed Core types. The coordinator will land the import seam with structured metadata. The capture lane owns vendor format readers, source identity extraction, conversion, and the migration report.

```csharp
internal sealed record MigrationSourceKey(string Format, string LibraryKey, string RecordKey);
internal sealed record ImportedTag(IReadOnlyList<string> Path, string? ColorHex);
internal sealed record ImportedCorrespondent(string Name, IReadOnlyList<string> Aliases);
internal sealed record ImportedCustomField(string Name, ImportedFieldValue Value);
internal sealed record ImportedDocumentMetadata(
    string? Title, DateOnly? DocumentDate, bool? Reviewed,
    IReadOnlyList<ImportedTag> Tags, IReadOnlyList<ImportedCorrespondent> Correspondents,
    string? DocumentType, string? Notes, IReadOnlyList<ImportedCustomField> Fields,
    long? ArchiveSerialNumber);

// InboxLibrary target. The result carries the document, whether it was added, and per-field conflicts.
internal Task<MigrationImportResult> ImportMigratedFileAsync(
    string sourcePath, MigrationSourceKey sourceKey, ImportedDocumentMetadata metadata,
    CancellationToken cancellationToken = default, OcrLanguage language = OcrLanguage.English,
    string? destinationFolder = null);
```

`ImportedFieldValue` represents text, Boolean, `DateOnly`, URL, `long` integer, `decimal`, money with an explicit currency when supplied, choice labels, and document links keyed by `MigrationSourceKey`. Preserve unresolved links for a second pass after the documents have IDs. Keep an unambiguous original value and its type in the report when a field has no lossless mapping.

- Import source title, document date, and explicit review state into their corresponding fields. Missing values use the library defaults for a new document. Preserve the source's intake timestamp in provenance or the report when supplied. Plico's date added records the local import.
- Map flat tags to one path segment. Preserve explicit hierarchy and colour. Map Mariner Organization to correspondent, preserve supplied correspondent aliases, and map types and notes directly.
- Map Paperless ASNs through the #12 constraint. Every conflict is reported against the source record, including an ASN occupied by an unavailable or trashed PDF.
- Resolve names against the target library. A repeated source record resolves to its existing Plico ID. An exact duplicate can fill missing optional fields and add tags. Preserve explicit local values and review state, and report differences. Record applied metadata and migration provenance together so retries can finish the same import even if a converter produces different bytes.

Keep source files intact. Exercise duplicate content with different source metadata, a retry after interruption, conflicting field definitions or ASNs, and forward references between migrated records. Coordinate shared changes in `LibraryCatalog.cs`, `InboxLibrary.cs`, `InboxLibrary.Import.cs`, `DocumentModels.cs`, and `LibraryCatalog.Portability.cs` with the coordinator. Integration and release tagging belong to the coordinating thread.
