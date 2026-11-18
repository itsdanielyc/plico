# Plico verification strategy

| Level | Harness | Evidence |
| --- | --- | --- |
| L0 | Release build with nullable analysis and warnings as errors | Compilation on each supported runner |
| L1 | xUnit | Literal search input, names, tags, and invalid data |
| L2/L3 | xUnit and temporary disk libraries | Import and catalog contract, FTS query, persistence and recovery |
| L4/L5 | Avalonia Headless and native screenshots | Input and selection behaviour, control presence and usable geometry, review screenshots |
| L6 | Native window plus a fixture picker | App launches, performs import, searches persisted OCR and changes review state |
| L7 | Verify each portable archive and its platform's installed copy. | Native journey from Windows setup, the copied macOS disk-image app, and the Ubuntu package. Windows and Ubuntu removal preserve the library. |

## Critical journeys

1. Import a digital PDF with selectable text and find it after reopening the library. Inspect damaged inputs and retry repaired files through the desktop (L3, L4 and L6).
2. Import image-only documents with the bundled OCR languages and verify accents in the embedded text. Check quoted search and the language preference (L3, L4 and L6).
3. Tag and filter a document. Tag a selection and mark it reviewed or return it to the inbox. An unavailable document cancels the batch (L3, L4 and L6).
4. Start each published bundle and make a portable backup. Open its folder and reopen its catalog. Cancellation clears staging, and switching libraries clears the backup shortcut (L3, L4, L6 and L7).
5. Discover scanners from the real shell. Physical feed, duplex and flatbed checks require a compatible device (manual).
6. Start watching a folder from the native window. Import a completed file while preserving the current search. Stop and explicitly resume the watch from its saved library setting (L3, L4 and L6).
7. Capture and search pages from a loopback eSCL scanner at the selectable resolutions. Verify address validation, saved scanner addresses and capture choices, and defaults when settings are unreadable (L1, L3, L4 and L6 layout).
8. Edit a catalog title and find it by title and filename. Move a PDF on disk, refresh, and check its metadata and containing folder. Confirm ambiguous copies have distinct records (L3, L4 and L6).
9. Sort filtered search results by newest added. Sort edited titles across pages, including case-insensitive ties. Retain the visible PDF selection when choosing a new order (L3, L4 and L6).
10. Fit a portrait preview to width and scroll to its bottom. Turn the page and confirm scrolling starts at the top. Return to the full-page view (L4 and L6).
11. Copy text from the selected PDF through the preview. Check that a PDF without searchable text hides the action and a missing catalog entry rejects the read. Include accented OCR text in the native journey (L3, L4 and L6).
12. Queue an existing scan through an active watch. Exercise cancellation, unsupported files and subfolder entries. Confirm a repeat uses the existing PDF and keeps the current search (L3 and L6).
13. Edit a query while the previous read is in progress. Confirm that only the latest results appear and pagination waits for the new first page. Check that the catalog remains usable after cancellation (L3, L4 and L6).

Run `dotnet test Plico.slnx -c Release` for the fast suite. Run the native harness from `keel.json` for installed-platform evidence. Visual review starts with per-OS captures and geometry checks. Add pixel baselines when repeatable runner captures justify the maintenance.

Budget three minutes for the local suite and 20 minutes for the normal matrix. Apply the Keel flaky-test policy and record evidence in VERIFICATION.md.
