# Inbox design

Plico is paper on a quiet desk. Graphite and grey chrome recedes, so the PDF page is the brightest object in the window and the only one with a shadow. Colour appears only when it means something: amber for search hits, a tag's own colour, green for a live watch, orange for a reminder that is due, red for errors, and the system accent for keyboard focus. There is no brand accent. The proposal, mockups and research are in [docs/design/redesign](docs/design/redesign/DIRECTION.md), and [ADR 020](docs/adr/020-desktop-redesign.md) records the structure.

**Type and geometry.** Inter throughout, in one scale: 28 display, 22 view title, 18 card title, 14 body, 13 labels, 12 meta. Nothing is smaller than 12. Controls are 32 px tall, 28 small, and 36 for the one primary action in a panel. Radii are 7 for controls, 10 for panels, 12 for popovers and 1.5 for paper. Theme brushes live in `App.axaml` with light and dark dictionaries. Dark mode has its own surface steps and slightly dimmed paper.

**Layout.** The sidebar holds places only: Inbox with its count, All documents, Upcoming, Without text, saved searches and tags. Intake sources (the intake folder, the watched folder with its live dot, the phone inbox and the recognition language) sit at its foot with Trash, Back up and Settings. The toolbar holds search and the two ways in, Scan and Add files, with the importers under Add files' menu. To the right of the queue, the page sits on a desk-grey canvas with a floating page bar. The filing card holds the title, path, suggestions, Date, Tags, Reminder and Fields, the document's details and its actions, with Mark reviewed and Trash pinned at the bottom. Below 1100 px the queue becomes a thumbnail strip and the card narrows. Its heading, count, sort and Save search then run across the strip and the page, so a 1024-pixel screen keeps them.

**Keyboard first.** J and K move, R marks reviewed and opens the next, A accepts suggestions, T tags, D dates, E renames, X trashes, O opens, [ and ] turn pages, / searches, ? shows every shortcut, and Ctrl+K / ⌘K opens a command palette with every action and its shortcut. Single keys never fire while you type in a field.

**Suggestions.** Dashed means suggested and solid means filed. For a document still in the Inbox, tag rules, reviewed documents and the date printed on the page fill dashed chips beside the fields, with one sentence saying where they came from. Selecting a chip accepts it. **Accept all** (A) files them together. Marking reviewed never accepts a suggestion silently.

**Motion.** Marking a document reviewed slips a copy of the page into the Inbox tray in 380 ms while the next document is already open. Repeated reviews within 1.2 seconds skip it, and reduced motion skips it entirely. Nothing else moves.

**Undo.** The message bar offers Undo (Ctrl+Z) after Mark reviewed, a batch review or Trash.

**Empty states.** A new library opens on a welcome page with the library folder, four ways to bring paperwork in and one sentence about privacy. An empty Inbox says "Inbox clear" and points to reminders that are due.

Inbox, All documents, and tags are stable destinations. Metadata and review controls stay with the selected PDF.

The two panes let people browse while recognizing a page by sight. The preview uses the actual PDF. Text matches help distinguish documents with similar layouts. Native file and folder pickers preserve platform access controls and keyboard conventions.

During a file drag, the reading area displays a copy target. On drop, the existing import status reports each batch outcome, including files already in the library.

**Intake folder…** starts the native picker in `PDFs/`. The Intake section shows the chosen subfolder. Add files, drops, and Scan use that destination, and each new PDF enters the Inbox for review.

**Import errors** reappears when the active library has unresolved errors. The dialog shows the latest reason for each source. **Retry files** uses the current intake settings for each file’s original route and updates the list as files succeed or fail again. **Clear list** dismisses the saved entries.

Folder watch lives in the Intake section. An active watch shows its folder name, a green Watching dot, and a stop button. The Inbox count reflects new imports while you review another PDF. Automatic refresh preserves the open selection and the pages you have loaded. When a refresh would replace that view, **Refresh results** appears below the list.

An active watch offers **Import existing files** under the folder name. The status bar reports how many files were queued and shows the import outcome when the watch processes them.

A paused watch offers **Resume folder watch** and **Resume and catch up**. Catch-up checks files already in the saved folder, then the action returns to **Import existing files** while the watch is active.

**Catch up on open** appears when the library has a saved watch. It controls future library opens. A startup error leaves the library open with its manual resume actions available.

**Phone inbox…** groups the second watch's folder picker, catch-up, and stop actions in a menu. The button shows when the phone watch is active and its tooltip shows the saved path.

The scan picker offers address entry alongside discovery. Connecting shows the device model and selects it so the same paper controls and Scan to inbox action apply.

The picker restores the last scanner used to start a job when that device is available. Switching scanners restores the chosen device's paper controls. Refresh keeps the selected device when it is still available.

The preview shows the PDF path below its catalog title. The path opens the document's containing folder in the file manager.

**Move to folder…** opens the system folder picker at the library’s PDF directory. After the move, the path and status show the new location and the preview keeps its current page.

**Refresh index** follows uniquely matched files through external filename swaps. The result list and open preview follow the catalog record to its new path.

The refresh status counts unavailable PDFs beside indexed and moved files. **Index report** opens the unresolved paths and the latest refresh notices. Paths remain visible after reopening the library. The action clears when both lists are empty.

**Restore PDF…** belongs to a selected unavailable path in the report. The file picker asks for a matching PDF. A successful restore opens the original catalog entry and updates the unavailable count.

**Restore from folder…** applies to all unavailable paths in the current library. The status shows restored and remaining counts, and **Index report** shows any problems with individual paths.

Selecting more than one document turns the filing card into a batch card. The selection count identifies its scope, and **Mark selected reviewed** is its primary action.

Reviewing from a filtered queue keeps the loaded result range. When the open PDF leaves that queue, the preview advances to the next match. At the end of the results it shows the preceding match.

**Create OCR copies** uses the current language for the selected PDFs. The status line shows progress through the selection. **OCR results** opens the latest batch report with a result and file path for each source.

**Move PDFs…** uses one folder choice for the selection. The status reports completed moves, files already in the folder, and the next file when a batch pauses.

**Export PDF…** sits in the filing card's actions and writes a copy to the chosen folder. **Export PDFs…** appears in the batch card and creates a dated folder. The status bar gives the destination and offers **Open export**.

**Combine PDFs…** opens a title and order dialog. It starts with the visible results order and lets the user move documents before creating the new inbox PDF.

**Extract pages…** opens a page selection and title dialog from a multi-page preview. The open page is the initial selection. The result opens as a separate inbox PDF.

**Rotate pages…** adds a direction to that dialog and is available on single-page PDFs. The preview starts on the new copy after confirmation. Its pages retain their order.

The recognition language selector sits in the Intake section, with the OCR engine and orientation options beside it. Its current language is visible before a new file or scan begins.

The scan interruption dialog shows the captured page count and the scanner error. **Save partial PDF** applies OCR and adds the available pages to the inbox. **Discard** closes the dialog and clears the captured pages from staging.

The sort selector sits beside the results count. It applies to the current inbox, tag, or text search. The selected PDF stays open when it is visible in the reordered list.

The results count shows **Searching…** after a query edit. **Load more** becomes available when the current search has a next page.

The search box hints at review status, tags, and dates. Its tooltip gives examples for `status:inbox`, `status:reviewed`, and the existing filters. `added:` uses the displayed local intake date. Folder filters use the stored relative path.

The folder button beside search opens a native picker in the library’s PDF directory. The selected folder appears as an editable search clause alongside the other terms.

**Without text** opens the library-wide `text:missing` search. The query is visible and editable. The same selection actions allow batch OCR from this view, and `text:present` finds PDFs with searchable text.

A malformed filter shows **Check search** beside the sort selector and an example in the results pane. The search box keeps the query editable. A valid search with no hits shows **No documents match.**

The preview’s document date opens an editor with a year-month-day field. Saving a blank field clears the date. The list labels the document date and intake date so both are visible when filled.

**Document date…** in the batch controls opens the same editor with the selected PDF count. A shared date fills the field. Mixed or missing dates open with an empty field and an explicit **Clear dates** action. After confirmation, matching PDFs remain selected in the refreshed results.

**Save search…** names the query and puts it in the sidebar. Opening a shortcut displays its name above the results and restores the selected tag and order. **Edit search…** opens the name, query, and order fields and offers to remove the shortcut.

A saved Inbox search updates as you mark documents reviewed. The next matching PDF stays available in the same search.

A selected sidebar tag offers **Rename…**. When the requested name belongs to another tag, the dialog describes the change and offers **Merge tags** as an explicit action. The resulting tag opens after the change.

Both tag fields suggest up to eight names from the current library. The single-document field skips tags already on that PDF. Choosing a suggestion fills in its catalog spelling. **Add**, **Tag selected**, or Enter after closing the suggestions applies the label.

**Remove tag…** appears when a selection contains tagged PDFs. Its chooser sorts the tags on that selection by name and shows a document count for each. Confirming removes the chosen assignments and keeps the matching results selected.

After a batch tag edit, the results list refreshes the number of rows already loaded. The selection follows the PDFs that still match, and **Load more** continues from the refreshed list.

The preview shows the whole page by default. **Fit width** fills the available reading width and allows vertical scrolling. Page navigation stays visible and starts each new page at the top.

**Go to match** appears in amber in the page header for multi-page text results. After the first jump, **Previous match** and **Next match** move from the page currently in view. The status bar reports the page number or when there are no more matches in that direction.

**Copy text** sits at the start of the floating page bar when the selected PDF has searchable text. The status bar confirms a successful copy.

**Read text** displays the current page as scrollable, selectable text beside the same navigation controls. **Show page** returns to the image at the current page. A new selection opens in page view. An image-only page in a searchable PDF shows a message and keeps the navigation available.

**Create searchable copy** appears on a banner over a page without searchable text and in the card's actions. **Create OCR copy** runs recognition again on a PDF with text. The Intake section's language selector supplies the language. A new copy appears in the inbox with the source tags.

With a custom **Intake folder**, **Include watched scans** appears under the destination label. The sidebar spacing keeps the watch actions within the minimum window height.

Keep labels present on actions. Use a polite live region for processing status. Keep the PDF accessible by keyboard and show a visible response for unavailable previews.

Trash (X) sits beside Mark reviewed and opens the next document. **Filing suggestions…** sits in the card's actions. Restore and permanent deletion live in Trash at the foot of the sidebar.

The ⋯ menu (**Library tools…**) and Settings give access to tag rules, integrity and metadata exports. Keep date and tag changes reviewable in one dialog, including when moving between documents in a selection. PDF/A, password protection and embedded metadata have explicit export choices.

Search uses the same amber highlight in the result excerpt and on the displayed page. The preview scrolls and fits with the highlight layer attached to the image.
