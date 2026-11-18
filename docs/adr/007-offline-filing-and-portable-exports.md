# ADR 007 • Offline filing and portable exports

Accepted 2026-11-18

The library keeps ordinary PDFs in `PDFs/`. Recoverable deletion moves bytes into `.plico/trash/` and records the original relative path on the same catalog identity. The existing move journal records trash, restore and purge intents. A backup carries the trash files with its SQLite snapshot. A monotonic catalog ID prevents a purged ID from referring to a later import.

Search compiles text clauses into parameterized FTS5 expressions and combines metadata predicates in SQLite. Metadata negation includes documents with an unset field. Match ranges come from FTS5 and page rectangles come from the page’s PDF word boxes. [SQLite FTS5](https://www.sqlite.org/fts5.html)

Suggestions require confirmation before writing a date or tag. Tag rules query the index and statistical suggestions train locally on reviewed PDFs using bounded word and word-pair counts. The catalog records the OCR model for newly recognized text. Candidates for amounts, IBANs and due dates are displayed together for review and copying.

An archival export rasterizes the source pages and regenerates their text with the bundled NAPS2 OCR pipeline. Password copies use AES-256 through the managed MIT-licensed PDFsharp 6.2 series. Plain copies can carry titles and tags through standard XMP fields. JSON metadata and CSV search exports omit extracted document text. [PDFsharp encryption](https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Encryption.html) · [NAPS2 PDF export parameters](https://www.naps2.com/sdk/doc/api/NAPS2.Pdf.PdfExportParams.html)
