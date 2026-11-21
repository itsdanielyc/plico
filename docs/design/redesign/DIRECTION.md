# Plico redesign: direction and mockups, round 1

Status: approved. Round two of the mockups raised the type scale so nothing is smaller than 12 px and dropped explanatory footnotes from the shortcuts sheet. The first four build slices are implemented in the desktop app. [PLAN.md](../../../PLAN.md#desktop-redesign) lists what shipped and what is still open, and [DESIGN.md](../../../DESIGN.md) is the current contract.

The interactive mockups live in [`prototype/`](prototype/index.html). [Published preview](https://claude.ai/artifact/HnjuPpr2i4j3PshhXmHEg7) (private to the repository owner). To run them locally, serve the folder over HTTP, for example `python -m http.server` from `prototype/`. The prototype uses sample documents only. Every company, person and amount in it is invented.

## Frame

| | |
| --- | --- |
| Surface | Avalonia 12 desktop app on Windows, macOS and Linux (x64 and ARM64) |
| Register | Product UI with branded-native fidelity: Plico's own look inside the window and platform behaviour at the edges (window controls, menus, shortcuts, file dialogs, text editing) |
| Audience | Households and independent workers who scan or download paperwork and need to find it again |
| Primary jobs | 1. Get new paperwork reviewed and filed. 2. Find a document by what it says. 3. Bring paper in without fuss. |
| Emphasis | The page itself. The eye lands on the paper, then on the filing card beside it. Chrome stays quiet. |

## What today's window gets wrong

Evidence: `artifacts/final-native-windows/*.png` from the v0.18.0 verification run.

- About twenty actions sit on screen at equal weight. The preview alone carries Edit title, Open PDF, Set document date…, Metadata…, Deadlines…, Copy text, Read text, Fit width, Thumbnails, PDF tools…, Move to folder…, Export PDF…, Create OCR copy, Trash & next, Filing suggestions…, Related…, Rotate pages…, a tag box, Add and Mark reviewed.
- The sidebar mixes places with commands: Show PDF folder, Intake folder…, Library tools…, Back up library…, Watch a folder…, Resume folder watch, Resume and catch up, Catch up on open and Phone inbox… sit under Inbox and Tags. At the minimum window size they scroll off.
- The page, the thing people recognise, gets about 40 per cent of the window width.
- Filing a document means visiting a title button, three links that open dialogs, a tag box at the bottom and a button in the corner.
- Suggestions hide behind Filing suggestions… and Metadata…, the opposite of where triage happens.
- Progress and errors arrive as one line of text in a status bar, and import errors in a separate dialog.
- There is no command palette and no macOS menu bar. The R, T, X, J and K shortcuts work only while the list has focus, and nothing on screen mentions them.

## Direction

**Concept: paper on a quiet desk.** The interface is graphite and grey, taken from the miille tile in the brand kit. The paperwork is the only bright object in the window and the only thing that casts a shadow. Colour appears only when it means something.

- **Type.** Inter, already bundled through `Avalonia.Fonts.Inter`, in one compact scale: 28 display, 20 view title, 17 card title, 13 body, 12.5 labels, 11.5 meta. Hierarchy comes from weight (400, 500, 600, 650) and ink level more than size. Dates and amounts use tabular figures. File paths and query syntax use the system monospace.
- **Colour.** Neutral greys with a faint cool bias. The primary action is graphite on light and near-white on dark, like the two app icon tiles. Plico has no brand colour, so colour is reserved for:
  - amber, for search hits on the page and in excerpts
  - each tag's own colour, shown as a dot
  - green, for a live watched folder
  - orange, for a date that is coming due
  - red, for destructive actions and errors
  - the system accent, for the keyboard focus ring

  Dark mode has its own surface steps, and paper is dimmed by 10 per cent so a white page doesn't glare.
- **Geometry.** 4 px grid. Controls are 30 px tall, 26 px small, and 36 px for the single primary action in a panel. Radii are 7 for controls, 10 for panels and popovers, 12 for dialogs, and 1.5 for paper, because paper has crisp corners. Borders are 1 px hairlines. Shadows belong to paper and to floating surfaces only.
- **Layout.** Three panes for review: sidebar (216), queue (284), page (flexible) and filing card (312). Browsing and search drop the card for a wider list, with Details (Ctrl+I) bringing it back. All documents adds a grid of real page thumbnails grouped by month.
- **Interaction model.** Sidebar for places only. Toolbar for search and the two ways in (Scan, Add files). Contextual actions on the card and in the ⋯ menu. Everything, including the 40 or so rarely used tools, in a Ctrl+K / ⌘K command palette that shows each shortcut. Single-letter keys for the review loop, shown on the buttons and in a `?` sheet.
- **Motion.** Motion explains where something went and never makes you wait. Every animation either runs on a copy or plays after the state has already changed. Repeated keyboard actions skip it, and reduced motion keeps every state change but drops the travel. Durations are 120–280 ms with a decelerate curve in and an accelerate curve out.
- **Signature.** Marking a document reviewed slips a copy of the page into a tray drawn from the logo: a sheet with a folded corner settling into a tray. The Inbox count rolls down at the same time. It runs in 380 ms beside the next document, which is already open and interactive. If you press R again within 1.2 seconds, it doesn't play. Everything else stays deliberately quiet.

**One rule carries the suggestion system: dashed means suggested, solid means filed.** Suggested values appear inside the fields they would fill. Select one to accept it, or press A to accept them all. Typing replaces it.

## Where everything goes

Nothing is removed. Each current control has a home.

| Today | Redesign |
| --- | --- |
| Inbox, All documents | Sidebar places. All documents gains a grid view (G) |
| Without text | `text:missing` filter chip, the palette's "Documents without text", and the scan banner on an image-only page |
| Tags, Rename…, Merge tags | Sidebar tags. Right-click to rename, merge or recolour. Settings › Filing › Tags |
| Saved searches, Save search…, Edit search… | Sidebar section, a Save chip in the search header, and right-click to edit or remove |
| Library name, Show PDF folder, Back up library… | Library switcher at the top of the sidebar. Settings › Library |
| Intake folder…, Include watched scans | Settings › Intake. The drop overlay shows the destination |
| Watch a folder…, Resume, Resume and catch up, Import existing files, Catch up on open, Phone inbox… | One line per source under Intake in the sidebar, with a live dot. Controls in the Activity panel and Settings › Intake |
| Status bar | Messages at the bottom of the window with Undo, plus the Activity panel for progress |
| Import errors… | Activity › Needs you, each entry with a reason, Retry and Dismiss |
| Duplicate notice | "Already in your library", with Show it |
| Trash… | Sidebar Trash view: restore, or delete permanently with confirmation |
| Search box, Folder…, Check search | One search field with filter tokens, a suggestions dropdown, filter chips (Status, Tag, From, Date, Type, Text, Folder) and inline syntax help |
| OCR language selector, OCR… options | Settings › Text recognition. The scan sheet footer states the language. Make searchable copy asks only when it matters |
| Scan, Add files | Toolbar. Add files has a menu for the importers (Paperless-ngx, Evernote, Mariner Paperless) and password-protected PDFs |
| Sort | Sort button in the list header |
| Select shown, Clear selection, batch panel | Ctrl+A, Esc and Shift+J. The filing card becomes a batch card |
| Edit title, filename link | Inline title. The path opens the containing folder |
| Set document date…, Metadata…, Deadlines… | Date, From, Type, Reminder and Note fields on the filing card. Custom fields and links appear under Details |
| Filing suggestions…, Local filing… | Inline dashed suggestions with their reason ("From the letterhead and 9 reviewed Northstar documents") |
| Tag box, Add, Mark reviewed | Tags field (T) and Mark reviewed (R), which opens the next document |
| Trash & next | Trash (X) opens the next document and offers Undo |
| Open PDF, Copy text, Read text, Fit width, Thumbnails, zoom, Go to match | Page bar: page numbers, zoom (Space), read text, match count with previous and next. Open (O) at the top right |
| PDF tools… (reorder, delete, rotate, split), Rotate pages…, Extract pages… | One Edit pages surface (rotate, reorder, delete, extract, split) saving a new version |
| Redact, stamp, compress, export, versions, OCR copy, Related, Ask local model | ⋯ menu on the card and the palette |
| Library tools… (tag rules, metadata definitions, integrity, index report, refresh index, JSON and CSV export, ASN labels, separator sheet, migrations, reminders, local models, semantic search, undo local filing, appearance) | Settings sections (General, Library, Intake, Scanning, Text recognition, Filing, Reminders, Local models, Advanced) and the palette |
| Scan dialog, interruption dialog, manual duplex | One scan sheet: settings on the right and pages arriving on the left. Separators show where documents split. A jam appears inside the same sheet with Keep and Discard |

Two places are new:

- **Upcoming:** a sidebar place listing reminders and suggested deadlines by date.
- **Activity:** a toolbar panel for files being read, watched folders, and anything that needs attention.

## Key flows

**Review.** Open Inbox and the oldest-first queue shows real thumbnails. The first document is open with its suggestions in place.
1. Press A to accept the suggestions, or fix a field with F (sender), D (date) or T (tag).
2. Press R to mark it reviewed. The next document is already on screen, and the message at the bottom offers Undo (Ctrl+Z).
3. Press Shift+R to mark it reviewed and stay. X moves it to Trash and opens the next.
4. At zero, the window says "Inbox clear" and lists what is coming up.

**Batch.** Shift-click, Shift+J or Ctrl+A selects documents. The page area fans out the selection and the card becomes a batch card. Tags show how many of the selection carry each one. The card offers Move, Set date, Combine, Export, Export as binder, Make searchable copies and Trash, with Mark N reviewed as the primary action. Everything can be undone.

**Find.** Press `/`. Typing suggests filters and the best documents. `tag:car ` becomes a token. Enter shows the results with excerpts, and the page marks every hit in amber with "1 of 3" navigation. Save turns the query into a sidebar shortcut.

**Capture.**
- **Drop:** dragging files shows the tray, the file names and the destination folder. On drop, rows appear at the top of the Inbox and move through "Reading text…" and "Finding dates and senders…". A duplicate is named, with a link to the copy you already have.
- **Scan:** the sheet remembers the scanner and profile, and you press Scan. Pages feed in on the left and separator sheets mark the splits. Add N to Inbox finishes.
- **Watched folders:** these report in the sidebar and in Activity.

**First run.** One window states the idea, shows the library folder with Change…, and offers four ways in: Add files, Scan paper, Watch a folder, and Move from another app. One sentence covers privacy. There is no tour.

## Keyboard

Keys follow the character on the user's layout, so they work on AZERTY and QWERTZ. Ctrl on Windows and Linux becomes ⌘ on macOS.

| Reviewing | | Everywhere | |
| --- | --- | --- | --- |
| J / K, ↓ / ↑ | Next, previous | Ctrl+K | Commands |
| R | Mark reviewed, open next | / or Ctrl+F | Search |
| Shift+R | Mark reviewed, stay | Ctrl+Z | Undo |
| A | Accept all suggestions | Ctrl+A, Shift+J | Select all, extend |
| T, D, F, N, E | Tag, date, sender, note, rename | Esc | Clear or close |
| X | Move to Trash | Ctrl+O, Ctrl+Shift+S | Add files, Scan |
| O | Open in PDF viewer | Ctrl+I | Details |
| Space | Zoom (grid: quick look) | Ctrl+, | Settings |
| [ / ] | Previous, next page | G, ? | Grid or list, shortcuts sheet |

## Platform edges

- **Windows:** content extends into the title bar and keeps the native caption buttons and Snap Layouts. Fluent conventions apply to focus, menus and dialogs.
- **macOS:** traffic lights sit in the sidebar. A full menu bar (`NativeMenu`) mirrors the palette with standard App, File, Edit, View, Document, Window and Help menus, and Settings… opens with ⌘,. Dialogs drop as sheets.
- **Linux:** a header bar with window controls. Plico follows the system colour scheme through the portal and keeps native file dialogs.
- **Everywhere:**
  - right-click works on rows, cards, tags, pages and saved searches
  - Plico follows the system theme, high contrast, reduced motion and text scaling
  - the window size, pane widths and last view are restored
  - the Inbox count appears in the taskbar or Dock badge
- **Collapse order:** the filing card narrows to 280. The queue becomes a 76 px thumbnail strip. The sidebar becomes icons. The minimum window stays 900 × 590.

## What the core needs to provide

These are contracts for `Plico.Core`. Most of the data exists today, but it reaches the window through separate dialogs.

1. **Per-field suggestions.** One call returns, for each field (sender, type, date, tags, deadline and title), a value, its source (rule, learned, text or local model) and a short reason. It replaces the Filing suggestions and Local filing paths in the review layout.
2. **Undo.** Each metadata edit, review-state change, trash and batch operation returns enough of the previous state to reverse it. Undo must stay consistent between the files and SQLite.
3. **Intake progress events.** Per file: queued, reading text (page n of m), finding dates and senders, done, duplicate of a document ID, or failed with a reason and a retry route.
4. **Scanner page stream.** Each page is delivered as it is captured, with separator, ASN and blank-page flags. The current interruption result keeps the pages that already arrived.
5. **Thumbnails.** A cached first-page render per document at list and grid sizes, invalidated by version.
6. **Upcoming.** A library query for active reminders and deadlines with their documents, sorted by date.
7. **Orientation hint.** A per-page "looks rotated by N degrees" flag for the Edit pages surface.

## Proposed build order

Each slice keeps the native journey green. `NativeVerification*.cs` drives the window by control names, so each slice updates those names together with the XAML.

1. **Foundation.** Tokens as theme resources (light, dark, high contrast), control styles, the icon set and a Settings window that takes over Library tools… and the rail commands.
2. **Shell.** Places-only sidebar, toolbar, Intake rows, Activity panel, command palette, shortcuts sheet and the macOS menu bar.
3. **Review.** Queue rows with thumbnails, continuous page viewer and page bar, filing card with inline fields and suggestions, the R loop with undo messages.
4. **Batch and Upcoming.**
5. **Search.** Tokens, suggestions dropdown, chips, match navigation and saved searches.
6. **Browse.** Grid and quick look.
7. **Capture.** Drop overlay, arrival rows, the scan sheet with page stream and inline interruption, duplicate messages.
8. **Edit pages.**
9. **First run, empty states, motion polish, French strings, and native verification on all six targets.**

## Rulings

- **Ruling: no brand accent.** The UI is monochrome graphite, the primary action is graphite or white, and focus uses the system accent. Why: the brand kit says Plico has no brand colour, and black, white and greys keep it in step with miille. Cost if wrong: one token.
- **Ruling: Inter in all chrome** rather than each platform's system font. Why: it is already bundled, it matches the brand guidelines, and it keeps one layout across three platforms. Cost if wrong: macOS feels slightly less native, which can be fixed later with a font token.
- **Ruling: Mark reviewed doesn't accept pending suggestions.** Accepting stays explicit (A). Why: a wrong suggestion filed silently is worse than one extra key. Cost if wrong: an extra key per document. A setting could change it.
- **Ruling: the review animation is skipped within 1.2 s of the previous review.** Why: repeated keyboard actions should not animate. Cost if wrong: a timing tweak.

## Answered after round one

The direction was approved as proposed, so the recommendations stand:

1. Upcoming is a place in the sidebar. Until the Upcoming query exists, it opens the due-reminders dialog.
2. All documents opens as a list. The grid is a later slice.
3. Two states, Inbox and Reviewed. No "Set aside" state.
4. Settings is a separate window on every platform.

## Implementation rulings

- **Ruling: Sender and Type fields wait for the core.** The filing card shows Date, Tags, Reminder and Fields, because the catalog has no correspondent or document type yet. Why: inventing fields the library cannot store would mislead. Cost if wrong: two rows on the card once contract 1 lands.
- **Ruling: the recognition language stays in the sidebar's Intake section**, beside the watches, as well as in Settings. Why: the language is visible before a scan or import starts, and the native journey drives it there. Cost if wrong: one row moves.
- **Ruling: every named control the tests drive keeps its name and type.** Buttons whose text the tests read keep plain-string content, and a style template adds their icons. Why: the native journey and the 87 headless tests keep guarding behaviour through the redesign. Cost if wrong: none to users.

## Research behind it

| Looked at | Idea taken |
| --- | --- |
| Paperless-ngx (document detail, Save & next, inline field suggestions, bulk editor, upload stages) | Suggestions at the field; one action that saves and advances; progress in named stages; duplicates explained in words |
| Docspell (curation, Confirm, multi-edit) | Confirm folds the suggestions away; batch edit uses the same form as single edit |
| DEVONthink (Inbox, preview plus inspector, Classify) | Ranked suggestions with a visible reason; the preview pinned beside the metadata |
| NAPS2, Apple Preview, Genius Scan, Adobe Scan | Live page feed, pages as direct objects, split decided at capture, plain status text while capturing |
| Superhuman, Linear, Readwise Reader, HEY | Auto-advance after each action; undo instead of confirmation; a palette that teaches shortcuts and knows aliases; a calm end state at zero |
| Lightroom Classic, Apple Photos | Single-key culling, grid and single views, Space to look closer |
| Things 3, Raycast | Quiet chrome; motion only for changes of place; keyboard hints where actions live |

Users complain about these patterns in the same apps, so the redesign avoids them:

- an inbox that never empties, because each document needs a separate step to leave it
- confirmation dialogs for actions that can be undone
- processing that gives no sign of progress
- suggestions hidden behind menus
- too many places to check on day one

## Verified so far

- **What ran:** the prototype in Chromium (Edge headless and the Claude in-app browser) at 1330 and 1440 px wide.
  - Light and dark themes, with Windows, macOS and Linux window edges.
  - Flows exercised: accept and review with undo, tagging with autocomplete, the command palette with aliases, search tokens with page highlights, the scan sequence through Add to Inbox, and the activity panel with a duplicate.
- **Not verified:**
  - screen readers
  - phone-width layout of the mockup page beyond a quick look

  Motion was judged from screenshots and timing, not on a real display.
- **The Avalonia implementation** has its own evidence in [VERIFICATION.md](../../../VERIFICATION.md).
