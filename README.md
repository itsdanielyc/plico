# Plico

A local paperwork inbox for Windows, macOS, and Linux. Scan or import documents and find them with tags and full-text search. The library holds ordinary PDFs you can open in your usual viewer.

## Download

Get Plico from [GitHub Releases](https://github.com/itsdanielyc/plico/releases). The archives include the .NET runtime and English, French, German, and Spanish OCR models. Extract the entire archive before launching.

| Platform | Launch |
| --- | --- |
| Windows 11, x64 or ARM64 | Run the setup for your architecture and open **Plico** from Start. For the portable ZIP, open `Plico/Plico.exe` after extraction. |
| macOS 26, Apple silicon or Intel | Open the disk image and drag **Plico** into Applications. The ZIP contains the app for direct extraction. |
| Ubuntu 24.04, x64 or ARM64 | Install the `.deb` with your package manager and open **Plico** from the desktop menu. The tar.gz opens with `./Plico/Plico` after extraction. |

The Windows executable and setup are unsigned. Microsoft Defender SmartScreen may show “Windows protected your PC.” The Mac app is ad-hoc signed and Gatekeeper may require a per-app approval because the build has no notarization. [Microsoft SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) · [Apple Gatekeeper](https://support.apple.com/en-us/102445)

Windows setup installs for your user and adds **Plico** to the Start menu and **Installed apps**. Uninstall preserves the library folder and its contents.

On Linux, Plico uses X11 or XWayland. The Debian package declares its runtime libraries. On a minimal Ubuntu installation, install the same libraries to use the portable tar.gz. [Package dependencies](https://github.com/itsdanielyc/plico/blob/main/scripts/linux/control.in) · [.NET on Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)

## Use Plico

1. Drop PDFs, PNGs, JPEGs, or TIFF scans into Plico or select **Add files**. Select **Scan** to capture a new document.
2. Select a document to preview it and add tags. **Mark reviewed** takes it out of the Inbox.
3. Search by name or document text, including documents you already reviewed. Choose a tag to narrow the results.

Use **Intake folder…** in the Library sidebar to send added files, dropped files, and direct scans to an existing folder inside `PDFs/`. With a custom folder selected, turn on **Include watched scans** to file new arrivals and catch-up scans there too. Plico remembers both choices for each library on this computer. New PDFs still appear in the Inbox for review. Choose `PDFs/` to file at the top level again. If the folder has moved or been deleted, choose an available folder before retrying.

Select a language in **OCR** for scans, imports, and searchable copies. Plico remembers the choice and uses that bundled model for recognition.

Plico imports a multi-page TIFF as one searchable PDF. The pages follow the scan’s order, orientation, and resolution.

Plico uses a JPEG photo’s orientation metadata to turn it upright before making a searchable PDF.

When an import fails, **Import errors** lists the source files and reasons. The list reopens with that library on this computer. Fix the files and choose **Retry files**. Each retry follows the library's current intake setting for added files or watched scans. Entries from older reports use the setting for added files. Choose **Clear list** to dismiss the errors.

Put a phrase in quotation marks to search its adjacent whole words. For example, "lease renewal" north searches for the phrase together with a word beginning with north.

Use uppercase AND, OR and NOT to combine words, phrases and filters. `(invoice OR receipt) -tag:archive` finds either word and excludes the Archive tag. `NOT status:reviewed` finds the review queue. Search results highlight matching text, and the preview marks hits on the displayed PDF page. Automatic sort gives title hits more weight.

Use `tag:tax` to require an exact tag. Use `tag:tax tag:"Home insurance" renewal` to require both tags and match renewal in the document. Tags ignore case. Use a backslash to include the closing quote or a backslash inside a quoted tag.

Use `status:inbox` to find PDFs waiting for review and `status:reviewed` to find reviewed PDFs. Save a query such as `renewal tag:property status:inbox` to return to a review queue that updates as you mark documents reviewed.

When a review removes the open PDF from the current results, Plico opens the next match or the previous one at the end. Single and batch review keep the loaded result pages in view.

Choose **Trash & next** to remove a PDF from the results and continue reviewing. Use **Trash…** in the sidebar to restore it to its original path or confirm permanent deletion. If another file occupies the path, move that file before restoring. Library backups include Trash. When the results list has focus, press **R** to review, **T** to enter a tag, **X** to trash, **J** for next and **K** for previous.

Open **Filing suggestions…** for the previewed PDF or a selection. Choose a suggested document date and any suggested tags to apply. Payment details are selectable for copying. Dates are interpreted using the document’s OCR language when available and the current locale. Use **Library tools… → Tag rules…** to add rules that suggest tags from a phrase, any word, or all words. Suggestions also learn from reviewed PDFs in the library.

Choose **Without text** to find PDFs with no searchable text. Use `text:missing` or `text:present` with other filters. A query such as `text:missing tag:tax folder:Filed` finds scans you can select together for OCR.

Use `added:2026`, `added:2026-11`, or `added:2026-11-18` to filter by year, month, or day added. The date follows the local date shown beside each document. Combine filters and text with a query such as `receipt tag:tax added:2026-11`.

Choose **Set document date…** under a PDF’s filename to record the date on the paperwork. Enter it as `YYYY-MM-DD`, or save a blank field to clear it. Use `dated:2024`, `dated:2024-02`, or `dated:2024-02-29` to find PDFs by that date. The filter works with `added:`, tags, folders, and text.

Select several PDFs and choose **Document date…** to assign a shared date. Use **Clear dates** in that editor to clear the selected documents’ dates.

Use `folder:"Filed/Tax Forms"` to search that folder and its subfolders. Use `/` and the spelling and case shown in Plico’s file path. `folder:.` matches PDFs directly in `PDFs/`. Add text, tags, or dates to the same query.

Use **Folder…** beside search to choose a folder and apply the same filter. Switching folders keeps the other search terms and filters. Choosing the library’s `PDFs/` folder shows unfiled PDFs.

If a filter needs correction, **Check search** appears with an example in the results pane. Edit the same query to search again.

Choose **Save search…** to name the current query. The shortcut appears under **Saved searches** and restores the query, the selected sidebar tag, and the sort order. Open a shortcut and choose **Edit search…** to update it or remove it. The library backup includes saved searches.

Select a sidebar tag and choose **Rename…** to change its label across the library. Enter an existing tag name and choose **Merge tags** to put their documents under that tag.

Start typing in either tag field to reuse a tag from the current library. Use the Down Arrow and Enter to fill in a suggestion, then Enter again to apply it. You can also type a new label.

Use **Sort** to browse by newest added, title A–Z, or document date. **Document date** puts the newest recorded dates first, followed by undated documents. **Automatic** puts the best matches first during a text search and the most recent documents first while browsing.

Ctrl-click (Command-click on macOS) toggles individual rows, and Shift-click selects a range. Choose **Select shown** to select the loaded results. Use **Tag selected** to apply a tag to the whole selection. The batch control can mark the selection reviewed or return reviewed documents to the inbox.

Choose **Remove tag…** to remove one tag from the selected PDFs. The chooser shows how many of the selected PDFs carry each tag.

Batch tagging keeps the result pages you loaded and the selected PDFs that still match the current search.

Choose **Move PDFs…** on a selection to file all of them in one library folder. Plico checks the selection before starting and reports what finished if you cancel or it encounters a file error.

Choose **Export PDF…** from the preview to copy one PDF directly into a folder outside the library. Plico adds a number when the filename is occupied. With multiple PDFs selected, **Export PDFs…** creates a dated folder. Use **Open export** to see the result in your file manager.

Choose **Archival PDF/A-2b** to render a copy and rebuild its searchable text in the selected OCR language. Choose **Password-protected PDF** for a copy that opens with your password, or **PDF with library metadata** to include the catalog title and tags. For portable metadata files, use **Library tools…** to export the entire catalog as JSON or the current search results as CSV.

Use **Library tools… → Check library integrity…** to compare file checksums with the catalog and see missing, changed or unindexed PDFs. The check reads the library so you can decide whether to refresh the index or recover files from a backup.

Choose **Combine PDFs…** to make a single document from the selection. Set the title and use **Move up** or **Move down** to set the order. The new PDF opens in the inbox with the source pages, embedded text, and tags.

Choose **Extract pages…** in a multi-page preview and enter a selection such as `1,3-5`. The excerpt opens in the inbox with those pages in source order, embedded text, and source tags. Page annotations belong to the source PDF.

Choose **Rotate pages…** for sideways or upside-down scans. Select pages, choose a direction, and title the new PDF. The copy keeps every page, its searchable text, and the source tags.

New OCR, rotated, and extracted copies carry the source’s document date. A combined PDF carries the date when every source has the same recorded day. You can edit a copy’s date in its preview.

Use **Edit title** in the preview to change the document's catalog title. Search covers both the title and filename. Select the PDF's filename to open its containing folder.

Choose **Move to folder…** in the preview to file the PDF within the library’s `PDFs/` directory. Plico keeps its title, tags, and review state with the file. A filename collision gets a numbered name.

Choose **Fit width** in the preview to enlarge the page and scroll through it. Choose **Fit page** to see it all at once.

Choose **Go to match** on a multi-page text result to open the first page that matches the text terms. Terms on separate pages can match the document as a whole.

After the first jump, use **Previous match** and **Next match** to move through matching pages. Each step starts from the page in view. The preview stays there when you reach the last match in that direction.

Choose **Copy text** to copy the searchable text from all pages of the selected PDF.

Choose **Read text** to read and select the text on the current page. It follows the page controls and search jumps. Choose **Show page** to return to the PDF image.

The library starts under Documents/Plico. Select the library name in the sidebar to choose another folder. **Show PDF folder** opens the `PDFs/` directory. **Refresh index** picks up PDFs that you add, edit, or remove on disk. To move or back up a library with its tags, close Plico and copy the entire directory, including `.plico/`. Digital PDFs with text on every page are copied unchanged.

When a scan copied into `PDFs/` needs OCR, choose **Create searchable copy** in its preview. Plico adds a searchable PDF to the inbox with the scan’s tags.

To correct a PDF’s OCR, choose the language in **OCR** and select **Create OCR copy**. Plico renders the visible pages and writes fresh searchable text into a separate PDF with the source tags.

Select several PDFs and choose **Create OCR copies** to process them in the current OCR language. **OCR results** shows which copies were added or reused, which inputs failed, and which remain after cancellation. New copies appear in Inbox.

Plico v0.16.0 introduced library catalog version 10. A library upgraded to version 10 needs a build that supports that version.

To make a copy while Plico is open, choose **Back up library…** and select a destination outside the current library. Plico creates a dated folder with the PDFs and their catalog. **Open backup** shows that folder in the file manager. Choose the folder as a library to reopen it in Plico.

Refresh also follows a uniquely identifiable PDF that you move or rename inside `PDFs/`, including when two files exchange names. Its title, tags, date, and review state travel with its catalog record. For libraries from older Plico versions, run **Refresh index** once while the original paths are present to prepare them for moves.

After **Refresh index**, the status shows how many PDFs are unavailable. **Index report** keeps their paths available across launches and displays the latest notices in the current session. Restore the files in the library and refresh again to bring their records back into view.

Select an unavailable entry in **Index report** and choose **Restore PDF…** to use a matching copy. Plico verifies the bytes and copies the PDF to its vacant original path. It creates missing ordinary subfolders and opens the existing catalog record with its metadata.

Choose **Restore from folder…** to look for matching backup PDFs in a folder and its subfolders. Matching uses the file contents, so renamed backups work. Each restored PDF returns to its original path. The report keeps unresolved paths and displays any restore notices.

Plico recognizes exact source files already in the library and reports that it reused the existing document. Existing libraries upgrade automatically. For older entries, repeat detection can also compare an unchanged digital PDF to the file you import.

### Scanner compatibility

- Windows uses scanners with a WIA driver. Linux uses SANE drivers plus the `libsane1` runtime for supported local scanners.
- All three platforms discover compatible eSCL or AirScan network scanners on the local network. On macOS you can also import a PDF saved by Image Capture for a USB-only scanner.

If discovery misses an eSCL scanner, enter its hostname, IP address, or full eSCL URL in **Scan** and select **Connect**. A hostname or IP address uses HTTP and the standard `/eSCL` path. Enter an HTTPS URL if your scanner uses HTTPS.

Plico remembers connected addresses in settings on this computer. Select a saved device in **Scan** to use it again, or select **Forget** to remove its saved address.

Scanning supports automatic source selection, flatbed, feeder, and duplex when the selected device provides them. Choose 150, 300, or 600 dpi. An eSCL scanner uses its nearest supported resolution when needed. OCR and search run locally. Scan discovery contacts the local network when you open the scanner picker.

Expand **Cleanup and profiles** in the scanner picker to drop blank pages, straighten, crop, choose colour mode or adjust brightness and contrast. Explicit duplex jobs can turn the back sides by 180 degrees. Save the settings as a named profile to reuse them with compatible scanners.

The scan picker opens on the last scanner you started with when it is available. Plico remembers the paper source, size, and resolution for each scanner on this computer. Existing shared settings supply the defaults for scanners with no saved choices.

If the scanner reports an error after delivering pages, choose **Save partial PDF** or **Discard**. A saved partial scan opens in the inbox with searchable text from the pages that arrived.

If your scanner software saves to a folder, choose **Watch a folder…** under **Library** and select its output folder. Plico imports new or updated PDFs and images after they settle. The watch covers files directly inside that folder and lasts while Plico is open. **Stop watching** ends intake. Your scanner's source files remain in the output folder.

The number beside **Inbox** is the total awaiting review. Watched imports update it while you work in other views. Use **Refresh results** below the document list when an arrival has been held for your current selection or loaded pages.

Plico remembers the chosen folder for the current library. **Resume folder watch** starts watching it again. **Resume and catch up** also checks files already in that folder, including scans saved while Plico was closed.

Enable **Catch up on open** to resume the saved watch and check existing scans whenever you open that library on this computer. **Stop watching** pauses the current session. Use the checkbox to control future opens.

Choose **Import existing files** while a watch is active to check its current files, including unchanged scans. Exact copies already in the library are skipped.

Automatic intake compares a settled scan’s size and modification time with its last known version. If the operating system reports lost notifications, Plico checks the folder again and queues scans that appeared or changed during the watch.

## Development

Use the pinned .NET SDK version. [global.json](https://github.com/itsdanielyc/plico/blob/main/global.json)

```sh
dotnet run --project src/Plico.Desktop
dotnet test Plico.slnx -c Release
```

Native verification commands and project records are available in the repository. [keel.json](https://github.com/itsdanielyc/plico/blob/main/keel.json) · [Product brief](https://github.com/itsdanielyc/plico/blob/main/PRODUCT.md) · [Roadmap](https://github.com/itsdanielyc/plico/blob/main/PLAN.md) · [Verification ledger](https://github.com/itsdanielyc/plico/blob/main/VERIFICATION.md)

Use `gh workflow run archives.yml --ref <release-branch>` to verify a release candidate. Wait for the solution tests and package checks on all six runtimes before tagging that commit.
