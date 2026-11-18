# Inbox design

Plico pairs a forest-green library rail with a light reading area. Inbox, All documents, and tags are stable destinations. Add files and Scan stay beside search. Metadata and review controls stay with the selected PDF.

The two panes let people browse while recognizing a page by sight. The preview uses the actual PDF. Text matches help distinguish documents with similar layouts. Native file and folder pickers preserve platform access controls and keyboard conventions.

During a file drag, the reading area displays a copy target. On drop, the existing import status reports each batch outcome, including files already in the library.

Folder watch uses the Library rail and shows the selected folder beside the stop action. Automatic imports update results using the current filters so a new scan does not interrupt reading.

An active watch offers **Import existing files** under the folder name. The status bar reports how many files were queued and shows the import outcome when the watch processes them.

The scan picker offers address entry alongside discovery. Connecting shows the device model and selects it so the same paper controls and Scan to inbox action apply.

The preview shows the PDF path below its catalog title. The path opens the document's containing folder in the file manager.

**Move to folder…** opens the system folder picker at the library’s PDF directory. After the move, the path and status show the new location and the preview keeps its current page.

Batch review lives below the document list. The selection count identifies its scope. The preview keeps the individual document's review action.

**Move PDFs…** uses one folder choice for the selection. The status reports completed moves, files already in the folder, and the next file when a batch pauses.

**Export PDFs…** appears in the batch controls. After the export finishes, the status bar shows the output path and offers **Open export**.

**Combine PDFs…** opens a title and order dialog. It starts with the visible results order and lets the user move documents before creating the new inbox PDF.

**Extract pages…** opens a page selection and title dialog from a multi-page preview. The open page is the initial selection. The result opens as a separate inbox PDF.

**Rotate pages…** adds a direction to that dialog and is available on single-page PDFs. The preview starts on the new copy after confirmation. Its pages retain their order.

The OCR selector sits beside intake controls. Its current language is visible before a new file or scan begins.

The scan interruption dialog shows the captured page count and the scanner error. **Save partial PDF** applies OCR and adds the available pages to the inbox. **Discard** closes the dialog and clears the captured pages from staging.

The sort selector sits beside the results count. It applies to the current inbox, tag, or text search. The selected PDF stays open when it is visible in the reordered list.

The results count shows **Searching…** after a query edit. **Load more** becomes available when the current search has a next page.

The search box hints at `tag:`, `dated:`, `added:`, and `folder:` filters. Its tooltip gives examples and explains quotation marks for phrases. `added:` uses the displayed local intake date. Folder filters use the stored relative path.

The preview’s document date opens an editor with a year-month-day field. Saving a blank field clears the date. The list labels the document date and intake date so both are visible when filled.

**Save search…** names the query and puts it in the sidebar. Opening a shortcut displays its name above the results and restores the selected tag and order. **Edit search…** opens the name, query, and order fields and offers to remove the shortcut.

A selected sidebar tag offers **Rename…**. When the requested name belongs to another tag, the dialog describes the change and offers **Merge tags** as an explicit action. The resulting tag opens after the change.

Both tag fields suggest up to eight names from the current library. The single-document field skips tags already on that PDF. Choosing a suggestion fills in its catalog spelling. **Add**, **Tag selected**, or Enter after closing the suggestions applies the label.

The preview shows the whole page by default. **Fit width** fills the available reading width and allows vertical scrolling. Page navigation stays visible and starts each new page at the top.

**Go to match** sits beneath the preview header actions for multi-page text results. After the first jump, **Previous match** and **Next match** move from the page currently in view. The status bar reports the page number or when there are no more matches in that direction.

**Copy text** sits beside the page controls when the selected PDF has searchable text. The status bar confirms a successful copy.

**Create searchable copy** appears beneath the preview of a PDF without searchable text. **Create OCR copy** runs recognition again on a PDF with text. The header’s OCR selector supplies the language. A new copy appears in the inbox with the source tags.

Keep labels present on actions. Use a polite live region for processing status. Keep the PDF accessible by keyboard and show a visible response for unavailable previews.
