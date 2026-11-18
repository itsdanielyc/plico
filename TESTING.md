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
2. Import image-only documents with the bundled OCR languages and verify accents in the embedded text. Select Spanish for `Spanish Receipt.png` and search for `pequeño`. Verify the saved language preference and check that the package contains `tessdata/spa.traineddata` and `tessdata/LICENSE` (L3, L4 and L6).
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
14. Jump from a search result to an OCR match on page two. Confirm that the preview renders that page and that terms spread across pages produce zero matching pages (L3, L4 and L6).
15. Refresh a library containing `Scanned Receipt.pdf`, the image-only page from the mixed PDF fixture. Make a searchable copy, search for its OCR text and tag, and compare the source bytes. Exercise repeat requests, a blank scan, a changed file, and cancellation (L3, L4 and L6).
16. Export selected PDFs and open the resulting folder. Compare bytes, include case-insensitive filename collisions, and exercise cancellation and stale source paths (L3, L4 and L6).
17. Combine two tag clauses and OCR text. Check exact tag matching, quoted labels, duplicate keys, incomplete input, and the matching-page jump (L3, L4 and L6).
18. Change a tag’s capitalization, rename one that belongs to a temporarily missing PDF, and merge into an existing tag. Check that a forced database error rolls back all links and that the dialog requires an explicit merge action (L3, L4 and L6).
19. Import `Scanner pages.TIFF`, which contains a Group 4 page and a rotated LZW page. Search both pages by embedded OCR text, check the second page’s physical size, and verify that a truncated source publishes no PDF. Check the decoder notices in the archives (L3, L6 and L7).
20. Type an accented tag prefix and choose a suggestion with the keyboard. Verify that the next Enter applies the stored label. Create a new tag, apply an existing tag to a selection, and check the suggestions again after switching libraries (L4, L6).
21. Save a scan between the watch inventory and subscription. Simulate lost filesystem notifications and save a new scan and an updated scan. Verify that both arrive, an unchanged older PDF stays outside intake, and the next filesystem notification works (L3, L6).
22. Search `Match Ledger.pdf` for `"ledger renewal"` and jump between pages two and four. Try both directions at the bounds, change the preview page manually, and check that a new search starts with its own first match (L3, L4 and L6).
23. Import `Sideways receipt.JPG` and `Mirrored receipt.jpeg`. Verify that both PDFs are upright and contain the receipt’s searchable text, that the anisotropic DPI translates to the same physical page dimensions, and that a repeat reuses the PDF (L3 and L6).

Run `dotnet test Plico.slnx -c Release` for the fast suite. Run the native harness from `keel.json` for installed-platform evidence. Visual review starts with per-OS captures and geometry checks. Add pixel baselines when repeatable runner captures justify the maintenance.

Use **Verify release candidate** (`archives.yml`) on the exact release commit. Each runtime runs the solution tests before packaging, then verifies the portable and installed copies. The publisher requires a successful run and all package files for that commit. Its smoke matrix downloads the newly published assets and runs the distribution checks again.

Budget three minutes for the local suite and 20 minutes for the normal matrix. Apply the Keel flaky-test policy and record evidence in VERIFICATION.md.
