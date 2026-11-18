# Plico architecture

| Module | Responsibility |
| --- | --- |
| `Plico.Core` | Library state, file placement, SQLite migrations and search, SDK-backed OCR and scanner capture |
| `Plico.Desktop` | Avalonia views, observable state, native file pickers, opening files, and per-user library preference |
| `Plico.Tests` | xUnit and Avalonia Headless tests, temporary libraries, and synthetic fixtures |
| `scripts` | Repeatable native launch checks, packaging, and release verification |

Desktop uses the Core library API. Core owns no UI controls or file pickers. The UI passes typed filters and source paths, and receives typed document summaries. Native paths and scanner/OCR quirks stay behind adapters. Tests inject file selections at the same seam as the native picker and use a real temporary library for persistence coverage.

## Library format

A library is a user-selected directory. `PDFs/` holds ordinary PDFs. `.plico/catalog.sqlite3` holds tags, reviewed state, paths relative to `PDFs/`, and indexed text. SQLite schema changes carry an explicit user version. Close the app before copying the library so the files and catalog represent the same point in time.

Import renders into private staging and checks the processed PDF before moving it into `PDFs/` without overwriting existing files. An interrupted index write leaves a readable PDF that can be picked up on refresh. Plico preserves the selected source files. Catalog updates and full-text index triggers run together in SQLite transactions.

Schema v2 indexes the SHA-256 digest of the source. Import computes it while copying a snapshot to staging. Repeating an unchanged source can reuse its existing PDF and avoid OCR. For a v1 entry, a PDF candidate with matching size is hashed on demand. Paths reserved by missing entries keep newly imported documents from inheriting unrelated metadata.

Schema v3 stores an optional display title. The FTS title field includes the display title and filename when a title has been edited. Existing FTS entries remain valid through migration. A refresh updates file text while preserving user-assigned titles.

Schema v4 stores the SHA-256 digest of the published PDF. Refresh pairs a unique absent catalog path with a unique new path when those PDF bytes match. It updates the path and filename search terms on the same catalog record. Matching duplicates are treated as separate files, and unchanged indexed files need only a file stamp check.

Search uses a bounded page of document records and SQLite FTS5. The UI never loads every document body to filter results. Tagged or reviewed documents remain in the same filesystem location.

The runtime contains the English Tesseract model. PDF parsing and text extraction use PDFium. Scanner discovery uses the supported local OS driver and eSCL on the local network. Platform requirements and support are versioned in the release notes.

Direct scanner addresses are stored in per-user settings. A short write lease and atomic replacement preserve entries when separate Plico instances save them. The scanner picker reconstructs saved entries locally and checks the device when it starts a scan.

Folder watch coalesces filesystem events by path and samples pending files until two consecutive observations agree. It stays idle without pending events. Ready files wait for the current desktop operation, then use the same snapshot and duplicate checks as manual import. Watching stops with the active window or when the library changes. [.NET FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher)

## Scope decisions

Keep one catalog per library. An additional database or search daemon would add setup and duplicate state. Each installer uses the same published application files as its portable archive. Windows setup generates an uninstall list from those files and removes directories only when empty. The macOS disk image carries the signed app and an Applications link. The Ubuntu package owns the application, launcher, and desktop entry. The library belongs to the user.
