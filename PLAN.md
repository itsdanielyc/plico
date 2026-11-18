# Plico implementation plan

| # | Slice | Done when | Keel levels | Status |
| --- | --- | --- | --- | --- |
| 0 | Avalonia scaffold | Build and native window smoke pass on all three runners | L0, L4, L6 | Done |
| 1 | Local PDF inbox | Digital and image imports yield plain searchable PDFs, with catalog and error recovery tests | L1–L4, L6 | Done |
| 2 | Review and retrieval | Tags, reviewed state and text search survive restart and a copied library; visible controls pass headless and native journeys | L1–L6 | Done |
| 3 | Scan into the inbox | WIA, SANE and eSCL adapters share the import path; synthetic capture and unavailable-driver paths exercised | L3, L4, L6 | Done |
| 4 | v0.1.0 | All three released archives pass the native journey from clean extraction on the exact tagged commit | L7 | Done |
| 5 | Faster intake | Dropped files use the normal import path; exact repeats reuse the existing PDF and v0.1.0 catalogs retain tags and review state | L1, L3, L4, L6 | Done |
| 6 | Watch scanner outputs | New PDF and image files in a chosen folder settle before import; stopping ends the watch; existing files and the current view keep their state | L1, L3, L6 | Done |
| 7 | Windows installer | A per-user installer adds a Start menu entry, launches the native journey, and uninstalls while preserving the user's PDF library | L7 | Done |
| 8 | Scanner address | A compatible scanner can be connected by address and produce a searchable PDF through the actual network capture path | L1, L3, L4 | Done |
| 9 | Remember scanner addresses | Directly connected scanners reopen without discovery and can be removed from settings; unavailable settings leave capture usable | L1, L3, L4 | Done |
| 10 | Document titles | Edited titles and original filenames remain searchable; tags and review state survive refresh and reopen | L1, L3, L4, L6 | Done |
| 11 | macOS and Ubuntu packages | A macOS disk image mounts and copies its app, and Ubuntu installs from a Debian package. Both complete the native journey. Package removal preserves the library. | L7 | Done |
| 12 | Open a document's folder | Selecting the path opens the actual containing folder, including when a file lives in a subfolder of the library | L3, L4, L6 | Done |
| 13 | Follow moved PDFs | Refresh follows a unique byte-identical PDF into a new folder and preserves its catalog record. Ambiguous matches create separate records. | L3, L6 | Done |
| 14 | Batch review | Multiple chosen PDFs can be reviewed or returned to the inbox together. A missing record rolls back the selection update. | L3, L4, L6 | Done |
| 15 | More desktop architectures | Intel macOS and ARM64 Ubuntu pass native OCR, portable launch, and their installed-package checks on their own runners | L0–L7 | Done |
| 16 | Batch tagging | Apply a tag to the chosen documents in one transaction and keep the selection ready for another action | L3, L4, L6 | Done |
| 17 | Quoted text search | Quoted phrases require adjacent whole words, while ordinary search and partially typed queries remain usable | L3, L6 | Done |
| 18 | French and German OCR | The user can select a bundled language. New PDFs get searchable text in that language, and the choice is stored locally | L3, L4, L6 | Done |
| 19 | Scan resolution | The scanner picker starts at 300 dpi and sends the chosen resolution through the native capture request | L3, L4, L6 | Done |
| 20 | Remember scan options | Paper source, paper size, and resolution reopen from local settings. Invalid settings fall back to the regional defaults. | L3, L4, L6 | Done |
| 21 | Back up the library | A backup opens as a complete library with the same PDFs and catalog data. Cancellation leaves the chosen destination clear. | L3, L6 | Done |
| 22 | Inspect and retry imports | A batch keeps unresolved errors by file, and retry imports those files through the regular intake path | L3, L4, L6 | In progress |
| 23 | Resume a watched folder | The folder choice belongs to the current library and can be resumed explicitly after reopening Plico | L3, L4, L6 | Planned |

The initial release is followed by focused improvements and fresh evidence.
