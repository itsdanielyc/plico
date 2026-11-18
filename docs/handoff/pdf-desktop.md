# PDF and desktop handoff

**Date** 2026-11-18 · **Branches** build/pdf-desktop and build/metadata-reminders

## Desktop and binder

The PDF lane can add the binder and PDF Tools actions, preview changes, Appearance, and Open With through small hooks in App.axaml, App.axaml.cs, MainWindow.axaml, MainWindow.axaml.cs, InboxViewModel.cs, WorkspaceDialogs.cs, and NativeVerification.cs. Keep feature implementations and native journeys in their own files where practical. Begin with the searchable binder, editable document order, contents, and atomic export to the chosen PDF.

The coordinator owns the document metadata and date actions, metadata and model settings in Library tools, tag navigation, and related and semantic search controls. The capture lane owns watch and scan controls and the watch shutdown hooks. Coordinate edits that touch those controls. Use a Desktop resource lookup for localization, then translate the integrated UI strings before release.

## PDF versions

Keep one active ordinary PDF at its library path and immutable historical PDFs under .plico/versions. Retain the existing document ID, metadata, tags, ASN, and review state across page edits and restoration. Reindex active text after the edit.

Journal the expected old and new fingerprints under the existing library write lease. Recover incomplete replacements before refresh sees a new file version. Backups include the recorded history, Trash holds it with the document, and purge removes it. Coordinate the hooks into the catalog migration list, library open, backup, Trash, and purge when those changes are ready.

Structured metadata owns schema v13. The coordinator assigns version history a distinct migration after v13 alongside the ASN and reminder migrations. The version lane owns its feature tables, journal, and tests.

## File associations

The PDF lane owns the Open With registration and associated checks in Windows NSIS, macOS app metadata and activation, and the Ubuntu desktop entry and launcher. Present Plico as an available handler and preserve the system default. Route an external file through normal import with the source intact. The coordinator owns workflow, signing, and release changes.
