# Inbox design

Plico is paper on a quiet desk, and a sibling of the other miille apps: a grey desk, a white sheet, black ink. The family contract is [docs/design/miille.md](docs/design/miille.md), with Onda as its reference; this file records what is particular to Plico. Grey chrome recedes, so the PDF page is the brightest object in the window and the only in-flow one with a shadow. Ink is the only accent: primary buttons, keyboard focus, checked boxes and switches. Colour appears only when it means something: amber for search hits, a tag's own colour, green for a live watch, orange for a reminder that is due, and red for errors. There is no brand colour and no system accent. The proposal, mockups and research are in [docs/design/redesign](docs/design/redesign/DIRECTION.md), [ADR 020](docs/adr/020-desktop-redesign.md) records the structure and [ADR 021](docs/adr/021-miille-family-foundation.md) the miille tokens and control language.

**Type and geometry.** Inter throughout, in one scale: 30 display, 24 view title, 20 heading, 15 body, 14 labels and controls, 13 meta and captions. Nothing is smaller than 13. Controls are 32 px tall, 38 for a large action and for fields and footer buttons in dialogs and Settings. Radii are 10 for controls, 12 for popovers and menus, 14 for callouts, 16 for cards and the command palette, and 1.5 for paper. Keycaps are 22 px with a 2 px bottom edge, tags are 28 px pills (dashed when suggested), and selected list rows sit in the sunken well with a hairline. The tokens live in `Theme/Tokens.axaml` under the family names (`Base`, `Panel`, `Raised`, `Sunken`, `Fg`, `Fg2`, `Fg3`, `Line`, `LineStrong` and the rest) with light and dark dictionaries, and the control styles in `Theme/Controls.axaml`. Dark mode uses Onda's steps: the desk at #0B0B0C, the sheet at #161618, raised surfaces at #1C1C1F, the page canvas at #0F0F11, and the page itself dimmed to about #E6E6E6.

**Focus and chrome.** Keyboard focus is an ink ring with a 2 px gap that follows the control's corners; a focused field also gets an ink border. On Windows 11 the native caption of every window is tinted to the desk, so the title bar and the sidebar read as one surface. Dialogs are raised windows with a 20 px heading, 15 px body text, and a footer under a hairline with the actions on the right and the primary last. Menus and tooltips are raised with 12 and 8 px corners and a hairline; Avalonia's Windows popups are sized to their content, so they carry no drop shadow. The tag field's suggestions are a popover drawn in the window: raised, 12 px corners, the pop shadow, 4 px inside and 34 px items, with a transparent margin for the shadow to fall in. A number field is one 38 px control: a single frame with the value centred and two Fg2 chevrons inside it. A dialog's head names the task in its 20 px title, with no full stop, and puts the particulars (the document, the count) in a 14 px Fg2 line beneath; dialogs built in code are as tall as their content.

**Layout.** The window and the sidebar are the desk; the toolbar, queue, page canvas and filing card are one white sheet laid on it, inset 6 px from the top with a 12 px top-left corner and a hairline edge. The sidebar (252 px from 1240 px, 208 and 200 below) holds the library switcher with the graphite app tile, then places only: Inbox with its count, All documents, Upcoming, Without text, saved searches and tags, as 40 px rows (36 below 1100 px) with the current one a white pill and an ink bar at the window's edge. A place's label stays on one line: where a language's name for it is long, the row uses a shorter sidebar label ("Réception" for "Boîte de réception", "Documents" for "Tous les documents"), with the full name as its tooltip and accessible name. The places fade into the desk above the status card. Every scroller in the shell (the places, the queue, the card, Settings and the lists in dialogs) draws its bar only under the pointer. At its foot a status card says what Plico is doing (watching a folder, not watching, files that need you, or work in progress with a progress bar) and opens the setting that explains it; Trash and Settings follow. The card's words are never cut: its title takes a second line in a narrow sidebar and its detail wraps. The intake folder, the watches, the phone inbox and the recognition language live in Settings › Intake and Text recognition. The toolbar holds search ("Search your paperwork" with the / keycap), the folder filter and a help button for Search tips, then the two ways in, Scan and Add files, with the importers under Add files' menu, then the palette and More (⋯). Until the library has its first document the welcome hero carries Scan and Add files and the toolbar shows neither, and the sidebar keeps only its places; saved searches and tags appear with the first document. The queue's header is the place in 24 Bold, the count as a sentence with the sort choice at its right, then the queue's own actions (Save search…, Edit search…, Refresh results, Select shown, Load more) as 28 px buttons on one row, wrapping only when they do not fit; there is no footer. The count is never cut: the sort choice trims its label first, and the count wraps only as a last resort, keeping each number with its noun. Rows show the title, then one meta line with the document's own date or the day it was added as a person says it ("Today", "Yesterday", "Mon", then "22 Nov 2026") and its pages. An empty queue is the family empty state in a dashed outline: a graphite tile, a title that says what is true ("No documents match"), one sentence with a way forward, and Clear search when a search caused it; in compact windows it spans the strip and the page, so its sentence is never squeezed into the strip. Dates are written one way everywhere, in the interface's language: "22 Nov 2026" and "22 Nov 2026 at 12:13" in English, "22 nov. 2026" and "22 nov. 2026 à 12:13" in French; every date field, in the card and in dialogs, shows and takes that form (and 2026-11-22, as search writes it). To the right of the queue, the page sits on a desk-grey canvas with a floating page bar; thumbnails wear the same veil as the page, so paper is the Paper colour everywhere. The filing card holds the title, path, suggestions, Date, Tags, Reminder and Details shown as values, the document's facts (which wrap rather than being cut) and its actions. The title, the date and the tags are edited where they are: the heading is the title's own field (a click or E turns it into a text field in the same type with the title selected; Enter or leaving saves, Escape restores, and an empty title is rolled back with a message), and E, D and T show as keycaps on their fields, which step aside while the field has the keyboard. Reminder and Details still open their dialogs. Mark reviewed and Trash stay pinned at the bottom. Below 1100 px the queue becomes a thumbnail strip, the card narrows, Scan keeps only its glyph, Fit width or Fit page becomes a glyph with its name as the tooltip so the viewer bar keeps one row, the palette button leaves for Ctrl+K, and the toolbar's spacing tightens so a French placeholder stays whole. The heading, count, sort and the queue's actions then run across the strip and the page, so a 1024-pixel screen keeps them.

**Keyboard first.** J and K move, R marks reviewed and opens the next, A accepts suggestions, T puts the keyboard in the tag field, D in the date field, E opens the title for editing, X trashes, O opens, [ and ] turn pages, / searches, ? shows every shortcut, and Ctrl+K / ⌘K opens a command palette with every action and its shortcut. Single keys act when the key is released, and only for a press that began outside a field with no palette open and no Ctrl, ⌘ or Alt held. A letter typed into search or the palette never reviews or trashes a document, even when it comes back up after focus has moved. Ctrl+A selects all shown from anywhere outside a field.

**Suggestions.** Dashed means suggested and solid means filed. For a document still in the Inbox, tag rules and reviewed documents fill dashed chips under the tags, and the date printed on the page sits dashed inside the empty date field, with one sentence saying where they came from. Selecting a chip accepts it. **Accept all** (A) files them together. Marking reviewed never accepts a suggestion silently.

**Motion.** Marking a document reviewed slips a copy of the page into the Inbox tray in 380 ms while the next document is already open. Repeated reviews within 1.2 seconds skip it, and reduced motion skips it entirely. Toasts rise 8 px over 260 ms; nothing else moves.

**Messages and Undo.** There is no status bar. Each message rises as an ink toast from the bottom of the page canvas, 16 px above the page bar, so it never covers Mark reviewed. A toast has a short title with no full stop ("Marked reviewed", "Tag added", "Backup saved") and an optional 13 px body at 84 % that says what it was about or where it went, with folders written as friendly paths ("Documents › Plico backups"), never a raw path. Its words wrap up to its 380 px width and are never cut; the live region speaks the whole message. News the window already shows, such as the library opening, raises no toast. A toast carries at most one action, offered with the message it belongs to: Undo, or Open backup, Open export and OCR results after that work. It leaves after about six seconds and stays while the pointer or focus is on it. A message about a number of documents has a wording for one and for several, so French agrees with its number ("1 document ajouté", "2 documents ajoutés"). Progress and anything that needs attention stay on the status card.

Undo belongs to its action. There is one offer at a time, made by the action it reverses and named by its toast: "Marked reviewed" or "Back in the Inbox" over the document, "2 documents marked reviewed", "Moved to Trash" over the document, "Renamed" over the new title, "Date set" over the day or "Date cleared", "Tag added" or "Tag removed" over the tag, "Tag “Tax” added to 3 documents", "Suggestion accepted". A later undoable action replaces the offer; any other change Plico makes to a document (a move, a reminder, details, a page edit, a tag rename, a batch date, an index refresh, a Trash restore or purge) retires it, so Undo never reaches past the latest change. Looking around (searching, moving through the queue, exporting, adding files) leaves the offer, so Ctrl+Z / ⌘Z keeps working after the toast has gone, and does nothing when there is no offer. The palette's Undo is the same offer.

**Empty states.** Empty states use the graphite tile with a white glyph, a 16 px title and one sentence, in a dashed frame where they need a container: "Inbox clear" points to All documents or to reminders that are due, and an empty search says why in the queue. A new library opens on a welcome page: a graphite hero with the two ways in, a row group with the library folder, watching a folder and moving from another app, and one sentence about privacy. It fits 900 × 590, and the hero's mark leaves first below 1040 px. Dropping files shows the mark on an 88 px tile.

**Words.** Plico speaks the family voice ([miille.md › Voice](docs/design/miille.md)). One name per concept: Rename for the title, Reminder for a document's dates, Details for its fields, Upcoming for the place, Back to Inbox for a reviewed document. An ellipsis ends only a label that opens a choice, a picker or a dialog that asks for more, so Settings, Trash, Version history and More have none. British spelling throughout. "This PC" on Windows and "this computer" elsewhere come from `PlatformText`, with a catalog row for each.

Inbox, All documents, and tags are stable destinations. Metadata and review controls stay with the selected PDF.

The two panes let people browse while recognizing a page by sight. The preview uses the actual PDF. Text matches help distinguish documents with similar layouts. Native file and folder pickers preserve platform access controls and keyboard conventions.

During a file drag, the reading area displays a copy target. On drop, the existing import status reports each batch outcome, including files already in the library.

**Intake folder…** starts the native picker in `PDFs/`. Settings › Intake and the status card show the chosen subfolder. Add files, drops, and Scan use that destination, and each new PDF enters the Inbox for review.

**Import errors** reappears when the active library has unresolved errors. The dialog shows the latest reason for each source. **Retry files** uses the current intake settings for each file’s original route and updates the list as files succeed or fail again. **Clear list** dismisses the saved entries.

Folder watch lives in Settings › Intake. An active watch turns the status card's dot green with "Watching" and the folder's name; Settings offers the stop button. The Inbox count reflects new imports while you review another PDF. Automatic refresh preserves the open selection and the pages you have loaded. When a refresh would replace that view, **Refresh results** appears in the queue header with **Select shown**, **Load more** and Save search, at every width.

An active watch offers **Import existing files** in Settings › Intake. A toast reports how many files were queued and shows the import outcome when the watch processes them.

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

**Create OCR copies** uses the current language for the selected PDFs. The status card shows progress through the selection. **OCR results** opens the latest batch report with a result and file path for each source.

**Move PDFs…** uses one folder choice for the selection. The status reports completed moves, files already in the folder, and the next file when a batch pauses.

**Export PDF…** sits in the filing card's actions and writes a copy to the chosen folder. **Export PDFs…** appears in the batch card and creates a dated folder. The toast gives the destination and offers **Open export**.

**Combine PDFs…** opens a title and order dialog. It starts with the visible results order and lets the user move documents before creating the new inbox PDF.

**Extract pages…** opens a page selection and title dialog from a multi-page preview. The open page is the initial selection. The result opens as a separate inbox PDF.

**Rotate pages…** adds a direction to that dialog and is available on single-page PDFs. The preview starts on the new copy after confirmation. Its pages retain their order.

The recognition language selector sits in Settings › Text recognition, with the OCR engine and orientation switches beside it. The status card shows the current language before a new file or scan begins.

The scan interruption dialog shows the captured page count and the scanner error. **Save partial PDF** applies OCR and adds the available pages to the inbox. **Discard** closes the dialog and clears the captured pages from staging.

The sort selector sits beside the results count. It applies to the current inbox, tag, or text search. The selected PDF stays open when it is visible in the reordered list.

The results count shows **Searching…** after a query edit. **Load more** becomes available when the current search has a next page.

The search field says "Search your paperwork". The help button beside it opens **Search tips**, a flyout in the interface's language with an example of each filter (`OR`, quotes, `-tag:`, `tag:`, `status:`, `text:`, `dated:`, `added:`, `folder:`) and what it finds; the field itself has no tooltip. `added:` uses the displayed local intake date. Folder filters use the stored relative path.

The folder button beside search opens a native picker in the library’s PDF directory. The selected folder appears as an editable search clause alongside the other terms.

**Without text** opens the library-wide `text:missing` search. The query is visible and editable. The same selection actions allow batch OCR from this view, and `text:present` finds PDFs with searchable text.

A search that cannot run keeps the last results, the open document and its page as they were. The field takes a red border and one line under it says how to repair the search ("Use one review status: status:inbox or status:reviewed."), so the correction never lands in the queue or the narrow strip. A valid search with no hits shows the **No documents match** empty state in the queue.

The document date is a field on the filing card. It shows the date as the interface writes it ("29 Feb 2024", "29 févr. 2024") and takes a date typed the same way, in any case, with or without accents or the abbreviation's full stop, or as 2024-02-29 like search; the calendar at its left opens as a popover in the window, and a saved date has a clear button. Enter, leaving the field, the calendar or clear saves at once and offers Undo. A date that does not exist stays in the field with one line under it ("Use a date like 22 Nov 2026."), and Escape puts the saved date back. The list labels the document date and intake date so both are visible when filled.

**Document date…** in the batch controls opens the same editor with the selected PDF count. A shared date fills the field. Mixed or missing dates open with an empty field and an explicit **Clear dates** action. After confirmation, matching PDFs remain selected in the refreshed results.

**Save search…** names the query and puts it in the sidebar. Opening a shortcut displays its name above the results and restores the selected tag and order. **Edit search…** opens the name, query, and order fields and offers to remove the shortcut.

A saved Inbox search updates as you mark documents reviewed. The next matching PDF stays available in the same search.

A selected sidebar tag offers **Rename…**. When the requested name belongs to another tag, the dialog describes the change and offers **Merge tags** as an explicit action. The resulting tag opens after the change.

Both tag fields suggest up to eight names from the current library. The single-document field skips tags already on that PDF. Choosing a suggestion uses its catalog spelling. In the filing card one Enter adds the tag: the suggestion chosen with the arrow keys, or what was typed, closing the list; the small **Add** button does the same for the mouse, and the empty field shows its T keycap. In the batch card **Tag selected**, or Enter after closing the suggestions, applies the label.

**Remove tag…** appears when a selection contains tagged PDFs. Its chooser sorts the tags on that selection by name and shows a document count for each. Confirming removes the chosen assignments and keeps the matching results selected.

After a batch tag edit, the results list refreshes the number of rows already loaded. The selection follows the PDFs that still match, and **Load more** continues from the refreshed list.

The preview shows the whole page by default. **Fit width** fills the available reading width and allows vertical scrolling. Page navigation stays visible and starts each new page at the top.

**Go to match** appears on the viewer bar's own row for multi-page text results, a chevron after a small amber dot; amber stays on the hits themselves. After the first jump a second chevron, **Previous match**, joins it and the first becomes **Next match**; both move from the page currently in view, and their names are their tooltips. When the fit label and the chevrons do not fit on one row, the zoom group takes a second row at the right. A toast reports the page number or when there are no more matches in that direction.

**Copy text** sits at the start of the floating page bar when the selected PDF has searchable text. A toast confirms a successful copy.

**Read text** displays the current page as scrollable, selectable text beside the same navigation controls. **Show page** returns to the image at the current page. A new selection opens in page view. An image-only page in a searchable PDF shows a message and keeps the navigation available.

**Create searchable copy** appears on a banner over a page without searchable text and in the card's actions. **Create OCR copy** runs recognition again on a PDF with text. The language in Settings › Text recognition supplies the language. A new copy appears in the inbox with the source tags.

With a custom **Intake folder**, **Include watched scans** appears in Settings › Intake under the destination. Settings scrolls the watch choices into reach at its minimum size, and the status card stays on screen at the window's.

Keep labels present on actions. Use a polite live region for processing status. Keep the PDF accessible by keyboard and show a visible response for unavailable previews.

Trash (X) sits beside Mark reviewed and opens the next document. **Filing suggestions…** sits in the card's actions. Restore and permanent deletion live in Trash at the foot of the sidebar.

The ⋯ menu (**Library tools…**) and Settings give access to tag rules, integrity and metadata exports. Keep date and tag changes reviewable in one dialog, including when moving between documents in a selection. PDF/A, password protection and embedded metadata have explicit export choices.

Search uses the same amber highlight in the result excerpt and on the displayed page. The preview scrolls and fits with the highlight layer attached to the image.
