# Changelog

## [0.16.0] - 2026-11-18

- Added a native Windows ARM64 app in ZIP and per-user setup formats.
- Use **Open backup** from the status bar after saving a library backup.
- Sort the current view by newest added or title A–Z. Automatic order puts the best matches first during a text search.
- Choose **Fit width** in the preview to enlarge the page and scroll through it.
- Choose **Copy text** to copy the searchable text from every page of the selected PDF.
- Choose **Import existing files** from an active watch to process scans saved while Plico was closed.
- Typed searches run in the background and give the latest query priority. **Load more** follows the current search results.
- Choose **Go to match** on a multi-page text result to open the first page that matches the text terms.
- Choose **Create searchable copy** to run OCR on a scanned PDF in the library and add a new PDF with the source tags.
- Choose **Export PDFs…** to copy the current multi-selection into a dated folder.
- Type `tag:` filters in the search box to require multiple tags alongside text terms.
- Select a sidebar tag to rename it or merge its documents into an existing tag.
- Import TIFF scans as searchable PDFs, including multi-page files.
- Reuse existing tags with suggestions when tagging one PDF or a selection.
- Recover new and updated scans after watched-folder notifications are interrupted.
- Move between matching pages in a PDF with **Previous match** and **Next match**.
- Apply JPEG photo orientation before creating the PDF and running OCR.
- Choose Spanish OCR for scans, imports, and searchable copies. The Spanish model runs locally and comes bundled with Plico.
- Use **Create OCR copy** to recognize a PDF’s visible pages again in the selected language. The catalog tracks the source and language for repeat requests.
- Use filters such as `added:2026-11` to search by the displayed date added. Catalog version 6 indexes those dates.
- Combine selected PDFs into one searchable inbox document with a title, document order, and source tags.
- Move a PDF into a library subfolder from its preview. Catalog version 7 records interrupted moves for recovery.
- Use `folder:"Filed/Property"` or `folder:.` to scope a search to a folder branch or the top level of the library.
- File a selection with **Move PDFs…** and one folder choice. Plico reports completed moves if the batch pauses.
- Extract pages and ranges from a PDF into a separately titled inbox document with their embedded text and source tags.
- Rotate selected pages clockwise, counterclockwise, or 180 degrees in a copy that keeps every page, embedded text, bookmarks, and source tags.
- Save completed pages as a partial searchable PDF when a scanner fails during capture.
- Wait for Plico’s own PDF readers before filing a document, so an active preview can finish on Windows.
- Reduce download and installation size by keeping debugger symbols out of release packages.
- Save named searches with their sidebar tag and sort order. Catalog version 8 carries these shortcuts into library backups.
- Set, clear, sort, and filter by document date. Catalog version 9 stores it with the PDF’s library record.
- Carry document dates into new OCR, rotated, and extracted copies. A combined PDF inherits a date shared by every source.
- See the pending review count beside Inbox. Watched imports keep the current preview and loaded pages in view and offer a results refresh when needed.
- Search and save review queues with `status:inbox` and find completed reviews with `status:reviewed`.
- See how to fix incomplete, malformed, or conflicting search filters in the results pane.
- Read and select the current page’s text directly in the preview, including after jumping to a search match.
- Reopen a library on the same computer to find unresolved import errors and retry the files.

## [0.15.0] - 2026-11-18

- Plico remembers the watch folder for each library on this computer.
- Choose **Resume folder watch** after reopening or stopping a watch to start watching that folder again.

## [0.14.0] - 2026-11-18

- Open **Import errors** to see each unresolved file and its reason.
- **Retry files** processes the unresolved inputs through the regular import workflow. Successful retries leave the error list.
- **Clear list** resets the current library's error report.

## [0.13.0] - 2026-11-18

- Back up your open library into a new folder containing the PDFs and a catalog snapshot.
- Open the backup as a library to use its titles, tags, review state, and full-text search.
- Cancel a backup from the status bar to clear its staging copy.

## [0.12.0] - 2026-11-18

- Choose 150, 300, or 600 dpi when scanning. Plico sends the selected resolution with the paper source and size.
- The scan picker remembers paper source, paper size, and resolution locally.
- Unreadable scan options open with the regional paper default and 300 dpi.

## [0.10.0] - 2026-11-18

- Choose English, French, or German for OCR in new scans and imports.
- All three models are bundled. Plico remembers the selected language on this computer.

## [0.9.0] - 2026-11-18

- Search quoted phrases as adjacent whole words. Mix them with ordinary search terms in the same query.
- Straight and curly quotation marks both work. While you type an opening quote, the matching terms continue to work.

## [0.8.0] - 2026-11-18

- Apply a tag to several selected documents at once. Existing spellings of matching tags are reused.
- The selection stays available after tagging so you can apply another tag or mark the documents reviewed.

## [0.7.0] - 2026-11-18

- Added Intel macOS downloads and ARM64 Ubuntu downloads.
- Intel Macs receive an app ZIP and a disk image. ARM64 Ubuntu receives a tar.gz and a Debian package.
- Both architectures run the same native paperwork and installed-package checks as the existing targets.

## [0.6.0] - 2026-11-18

- Select several documents and mark them reviewed together. A selection of reviewed documents can return to the inbox in one action.
- **Select shown** selects the current list. A watched import keeps the selection available until you finish the batch.

## [0.5.0] - 2026-11-18

- **Refresh index** can follow an indexed PDF into a new folder or filename. When the match is unique, it carries over that PDF's title, tags, review state, and duplicate detection.
- Refresh builds the required fingerprints for older libraries. New imports receive them automatically.
- If multiple copies make a match ambiguous, refresh creates separate catalog entries for the new paths.

## [0.4.1] - 2026-11-18

- Select a PDF's filename in the preview to open its containing folder. This works for documents you organize into subfolders.

## [0.4.0] - 2026-11-18

- Open the macOS disk image and drag Plico into Applications.
- Install the Ubuntu package and find Plico in the desktop menu. Package removal preserves the library.
- The release includes Windows setup, macOS disk image, and Ubuntu package downloads alongside the portable archives.

## [0.3.2] - 2026-11-18

- Edit a document's title from its preview. Search recognizes the edited title, PDF filename, and document text.
- Catalog titles, tags, and review state survive reindexing the PDF.

## [0.3.1] - 2026-11-18

- Plico remembers the addresses of scanners you connect to and offers them the next time you open Scan.
- Use **Forget** to remove a saved address from this computer.
- A settings error leaves a newly connected scanner available for the current scan.

## [0.3.0] - 2026-11-18

- Connect an eSCL scanner by hostname, IP address, or its full scanner URL from the Scan dialog.
- The dialog reads the scanner's model before enabling capture and keeps connected scanners available when you refresh discovery.

Downloads include Windows setup and ZIP, a macOS app ZIP, and a Linux tar.gz. See the README for platform and first-launch details.

## [0.2.1] - 2026-11-18

- Added a per-user Windows setup executable with a Start menu shortcut and an Installed apps entry.
- Uninstall removes the application files and preserves the PDF library.
- Portable downloads include a Windows ZIP, macOS app ZIP, and Linux tar.gz.

The Windows setup and executable are unsigned. The macOS app is ad-hoc signed. See the README for first-launch and scanner setup.

## [0.2.0] - 2026-11-18

- Watch a scanner output folder while Plico is open. New and updated PDFs, PNGs, and JPEGs enter the same OCR and duplicate detection workflow.
- Stop watching from the library sidebar. Automatic imports preserve the current search and the original files.
- File import checks that a source stays unchanged while it is copied.

Self-contained archives are available for Windows 11 x64, macOS 26 Apple silicon, and Ubuntu 24.04 x64. The Windows executable is unsigned. The macOS app is ad-hoc signed. See the README for first-launch and scanner setup.

## [0.1.1] - 2026-11-18

- Drop PDFs and images directly into the Plico window.
- Exact repeat imports reuse the existing PDF and preserve its tags and review state, including byte-identical digital PDFs from v0.1.0 libraries.
- Search excerpts put the matching text closer to the beginning of each result.
- New imports keep their own metadata when a different document previously used the same filename.

Self-contained archives are available for Windows 11 x64, macOS 26 Apple silicon, and Ubuntu 24.04 x64. The Windows executable is unsigned. The macOS app is ad-hoc signed. See the README for first-launch and scanner setup.

## [0.1.0] - 2026-11-18

- Import PDFs, PNGs, and JPEGs. Bundled English OCR embeds searchable text in image-only pages. Imports preserve the supplied files.
- Scan with Windows WIA, Linux SANE, or a local network eSCL scanner on any supported platform. One capture job produces one PDF.
- Preview each page, tag documents, mark them reviewed, and search document names and recognized text across the library.
- Keep PDFs in an ordinary folder and open them in the OS PDF viewer. Copy the whole library to carry its catalog and tags to another computer.

### Downloads

Self-contained archives are available for Windows 11 x64, macOS 26 Apple silicon, and Ubuntu 24.04 x64. Extract the archive and run Plico. See the repository README for first-run and scanner setup.

The Windows executable is unsigned and SmartScreen may display “Windows protected your PC.” The macOS app is ad-hoc signed and has no Apple notarization. Physical scanner interoperability remains unverified. [Microsoft SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) · [Apple Gatekeeper](https://support.apple.com/en-us/102445)
