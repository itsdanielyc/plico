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

Creating a searchable copy reads an indexed PDF into private staging under the file-operation lease. For a PDF with existing text, NAPS2 renders its visible pages for fresh OCR. A new copy must contain text and have the indexed PDF’s page count. Inserting the copy, its source and language, and its metadata uses one catalog transaction. [NAPS2 PDF exporter](https://github.com/cyanfish/naps2/blob/c6c762214293097c522fc1b7656f106fbb66a7f2/NAPS2.Sdk/Pdf/PdfExporter.cs)

TIFF intake decodes each directory with LibTiff.Net and draws its orientation and resolution into a staged image-only PDF. The existing PDF import and OCR pipeline processes that intermediate file. Page counts must agree before the final PDF is published. The converter limits each decoded raster to 160 million pixels and releases page buffers as it proceeds. [LibTiff.Net decoder API](https://bitmiracle.github.io/libtiff.net/help/api/BitMiracle.LibTiff.Classic.Tiff.html)

JPEG intake reads the image origin with Skia. For a rotation or reflection, Plico transforms the staged raster and resolution into a private JPEG at high quality. The existing image importer uses that upright JPEG for both the PDF and OCR. Exact-source deduplication hashes the original JPEG bytes. [Skia codec orientation](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec.encodedorigin)

Batch review and tagging update the selected IDs in one SQLite transaction. A row-count check preserves the selection's previous state if a document became unavailable. The desktop restores matching selections after the tag update.

Tag renaming updates the label for a capitalization change or moves the catalog links to the destination key. The transaction includes links on missing PDFs so they follow the new tag when the file returns. A merge keeps the destination’s label and uses its existing document links once.

The desktop builds a sorted index of the sidebar's tag keys when a tag field first requests suggestions. The same index serves both fields until the label list changes. Each edit starts at the matching prefix by binary search and reads at most eight eligible labels, with no catalog read for the edit.

Schema v2 indexes the SHA-256 digest of the source. Import computes it while copying a snapshot to staging. Repeating an unchanged source can reuse its existing PDF and avoid OCR. For a v1 entry, a PDF candidate with matching size is hashed on demand. Paths reserved by missing entries keep newly imported documents from inheriting unrelated metadata.

Schema v3 stores an optional display title. The FTS title field includes the display title and filename when a title has been edited. Existing FTS entries remain valid through migration. A refresh updates file text while preserving user-assigned titles.

Schema v4 stores the SHA-256 digest of the published PDF. Refresh pairs a unique absent catalog path with a unique new path when those PDF bytes match. It updates the path and filename search terms on the same catalog record. Matching duplicates are treated as separate files, and unchanged indexed files need only a file stamp check.

Schema v5 records the source document and language of each new OCR copy. The source ID, source fingerprint, and language identify an existing copy for reuse. A refresh that indexes changed PDF contents clears their previous import and OCR-copy associations.

Schema v6 indexes the added timestamps of present PDFs. An `added:` filter bounds the lookup with UTC timestamps, then checks the exact local date for candidates near the period edges. This handles clock changes across midnight using the same local dates shown in the list. [SQLite partial indexes](https://sqlite.org/partialindex.html), [.NET time-zone conversion](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.converttime)

Schema v7 records a pending move with the catalog ID, two relative paths, and the source SHA-256. After Plico moves the file, a SQLite transaction updates the catalog location and clears the move record. After an interruption, the pending move identifies the old catalog record even among identical PDFs. Recovery verifies the destination bytes before updating that record. [.NET file moves](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move)

Schema v8 stores saved query text, the selected sidebar tag key, and the result order. A normalized name index keeps names unique. Each shortcut reruns its query against current documents. The sidebar tag follows a rename or merge through the same transaction as the document links. The catalog snapshot carries saved searches into a backup.

Schema v9 stores the user-entered document date as a nullable day number on the catalog record. The present-document index supports the `dated:` bounds and document-date ordering. The date follows the record through filing and refresh and travels in a catalog backup. `added:` continues to match the local day of the intake timestamp.

Publishing a PDF copy reads its initial document date in the same catalog transaction that copies its tags. The date comes from a single source or a date shared by every source in a packet. The inserted copy has its own date field for future edits.

Batch filing checks the selected paths and file stamps before the first move. Under one library lease it commits each move through the same journal and retains the next numbered filename for each colliding name. Cancellation or an error stops the batch and reports completed moves and remaining work.

Plico waits for its preview and matching-page readers to release their file handles before journaling and moving a PDF. The preview consumes the rendering stream through completion so PDFium closes its input file. The move checks the source stamp again after waiting.

Backup copies the PDF tree and takes an online SQLite snapshot under the library's file-operation lease. It validates the file inventory and publishes the staged backup by an atomic directory move inside the selected destination. [Microsoft.Data.Sqlite online backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)

Selected export reads the chosen catalog paths and file stamps under the same lease. It stages the PDF bytes under the chosen destination, assigns distinct portable filenames, and checks the sources again before publishing the folder. [Directory.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.move?view=net-10.0)

Combining PDFs uses the selected-file checks and snapshots each source under the library lease. PDFsharp imports the pages, page annotations, and embedded text in the requested order. Plico verifies the output page count and source file stamps before publishing, then inserts the catalog record and unique source tags in one transaction. [NAPS2 PDFsharp page import](https://github.com/cyanfish/naps2-pdfsharp/blob/8d30f069e6cef9ee9b001372a94432de7f6a3146/PdfSharpCore/Pdf/PdfPages.cs)

Page extraction uses that page-copy pipeline with ordered, merged ranges. The catalog operation checks the selection against the staged PDF’s page count, copies the selected page content and embedded text, and verifies the output count and source stamp before publication. The source document holds its page annotations.

Page rotation updates the selected page angles in a complete staged PDF. It keeps the page content, bookmarks, and embedded text. It verifies the saved page count and source stamp before publishing the copy with the source tags.

The preview reads the selected page’s inherited rotation from the page tree. For a rotated page it creates an upright single-page snapshot in memory and applies the turn to the thumbnail. This accounts for the scan extractor returning raw pixels when the original and rotated page dimensions match. [NAPS2 image extraction](https://github.com/cyanfish/naps2/blob/c6c762214293097c522fc1b7656f106fbb66a7f2/NAPS2.Sdk/Pdf/PdfiumImageExtractor.cs)

Search uses a bounded page of document records and SQLite FTS5. The UI never loads every document body to filter results. Tagged or reviewed documents remain in the same filesystem location.

Result ordering runs in SQLite before the page limit. Newest added follows catalog insertion order. Alphabetical order compares the visible titles case-insensitively, with the most recently added record breaking ties.

Copy text reads the body from the selected catalog record and passes plain text to the operating system clipboard. The catalog checks that the record is present.

The page text view reads the library PDF through the file gate shared with matching-page navigation. It extracts the requested page using the same text reader as the index, then checks the file stamp. Changing page, document, or view mode cancels the previous request. The desktop displays a single page using a selectable, read-only text control. [Avalonia selectable text](https://docs.avaloniaui.net/controls/data-display/text-display/selectabletextblock)

Search input is compiled into a parameterized FTS5 expression. Ordinary words are prefix terms, and completed quoted phrases use adjacent whole tokens. [SQLite FTS5 query syntax](https://www.sqlite.org/fts5.html#full_text_query_syntax)

The query compiler extracts tag clauses and normalizes their keys with the same rule as catalog tags. SQLite joins the requested keys through the tag index and returns documents containing every key. Page matching uses the text expression from that same compiler.

A `folder:` branch uses the existing unique relative-path index to find the rows between its slash-delimited prefix and upper bound. The query joins those IDs to the rest of the search. For `folder:.`, it checks for paths with zero folder separators. [SQLite query plans](https://sqlite.org/eqp.html)

Typed search uses a single background reader for results. Editing the query cancels the previous read and gives the newest request control of the results. SQLite checks cancellation during the read so an abandoned query can release the reader slot. [SQLite progress callbacks](https://www.sqlite.org/c3ref/progress_handler.html)

Match navigation reads the selected PDF in the background. A scratch FTS5 index uses the catalog’s query compiler and tokenizer and holds the text index for one page. A matching-page step starts beside the current preview page and stops at the closest hit in the requested direction. Changing the search, selection, or preview page cancels a pending jump. [FTS5 contentless indexes](https://sqlite.org/fts5.html#the_delete_all_command)

The runtime carries English, French, German, and Spanish Tesseract models. The app captures the selected language for each import batch or scan. PDFium renders previews and PdfPig extracts embedded text. Scanner discovery uses the supported local OS driver and eSCL on the local network. Platform requirements and support are versioned in the release notes.

Direct scanner addresses are stored in per-user settings. A short write lease and atomic replacement preserve entries when separate Plico instances save them. The scanner picker reconstructs saved entries locally and checks the device when it starts a scan.

The desktop holds unresolved import errors by source path for the active library. A successful retry clears that entry. Retry passes those paths through the same snapshot, OCR, and duplicate checks as regular intake.

The picker saves the three typed capture choices together as one local settings file. A read with invalid values uses the regional defaults until the user selects a valid configuration.

When a scanner fails after delivering pages, the capture pipeline keeps those pages in staging. The desktop asks whether to save them before running OCR. Saving verifies the exported page count and publishes a clearly titled partial PDF. Discarding or cancelling releases the staged files. [NAPS2 capture and error handling](https://www.naps2.com/sdk/doc/api/NAPS2.Scan.ScanController.html)

Folder watch coalesces filesystem events by path and samples pending files until two consecutive observations agree. It stays idle without pending events. Ready files wait for the current desktop operation, then use the same snapshot and duplicate checks as manual import. Watching stops with the active window or when the library changes. [.NET FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher)

The desktop reads the pending review count through the inbox index after publishing documents, changing review state, or refreshing the catalog. Watched arrivals refresh an active query through the background search reader. Extra result pages and batch selections defer automatic list replacement. A first page refresh also defers when it would displace the open PDF.

The query compiler strips review-status clauses before building the text expression. The catalog binds the review state as an indexed predicate. An explicit Inbox view and a reviewed clause have an empty intersection. Saved searches store the clause as part of their existing query text.

When query parsing finds an invalid clause, the result page carries a typed issue and skips the catalog read. The desktop maps the issue to a correction for the active query. A valid empty page uses the ordinary empty-result message.

The watcher records top-level scan sizes and modification times before it subscribes. It reconciles once after subscription to catch arrivals during startup, and again when the operating system reports lost notifications. Only new or changed paths join the readiness queue. A backend that needs to restart is rearmed before reconciliation. A failed restart or unreadable folder is reported to the desktop. [.NET notification buffer overflow](https://learn.microsoft.com/en-us/dotnet/api/system.io.internalbufferoverflowexception?view=net-10.0)

The last watch folder is saved in local settings under the normalized library path. Reopening a library offers the saved folder as an explicit resume action.

The watch can also queue files already in its folder. It gathers supported top-level paths before adding them to the same readiness queue used by filesystem events. Cancellation during the directory scan leaves the queue untouched by that request.

## Scope decisions

Keep one catalog per library. An additional database or search daemon would add setup and duplicate state. Each installer uses the same published application files as its portable archive. Windows setup generates an uninstall list from those files and removes directories only when empty. The macOS disk image carries the signed app and an Applications link. The Ubuntu package owns the application, launcher, and desktop entry. The library belongs to the user.
