# Desktop redesign: one window, one palette, quiet chrome

**Status** Accepted for the design branch

The desktop window follows the "paper on a quiet desk" direction in [the redesign proposal](../design/redesign/DIRECTION.md). Graphite and grey chrome recedes so the PDF page is the brightest object and the only one with a shadow. Colour appears only when it carries meaning.

Theme values live as named brushes in `App.axaml`, with a light and a dark dictionary. Every earlier key stays defined so the existing dialogs follow the new palette without per-dialog changes. Shared control language (buttons, chips, keycaps, lists, surfaces) lives in `Theme/Controls.axaml`. Icons are stroke geometries on a 20-unit grid in `Theme/Icons.axaml`, drawn by the `Icon` control in the inherited text colour.

Rarely used tools leave the permanent chrome. They stay reachable from the ⋯ menu, from Settings, and from a Ctrl+K / ⌘K command palette that lists every action with its shortcut. The palette is built from the view model's existing commands when it opens, so it cannot offer an action the window cannot run.

Buttons that tests or screen readers read by their text keep plain-string content. A style template draws their icon from the `Ui.Icon` attached property.

Optional background reads (row thumbnails, inline filing suggestions, page previews) go through one tracked path. Switching libraries or closing Plico cancels them and waits for them to release their files before the library closes. This keeps the PDF folder and SQLite catalog free of stray handles. These reads run beside catalog writes, so every catalog connection sets SQLite's busy timeout. A write then waits for a reader on another connection. Without it, an insert that returns its row ID commits as its reader closes, a step Microsoft.Data.Sqlite does not retry, and the write fails at once with "database is locked".

Undo covers review changes and Trash for the latest action. Each revert goes back through `InboxLibrary`, so files and the catalog stay consistent.

Single-key shortcuts act on key release, so the character never lands in a field the shortcut focuses. The window arms a key only when it goes down outside a text field, with no overlay open and without Ctrl, ⌘ or Alt, and a release counts only for an armed key. A letter typed into search or the palette is therefore inert when it comes back up after focus has moved.

**Consequences.** The window keeps every named control that the headless tests and the native journey drive. The native checks now assert that review and Trash stay on the filing card, and that every PDF action scrolls into view at the minimum window size. The motion layer works on a snapshot and never blocks a state change. It skips repeated reviews within 1.2 seconds, and it skips entirely when Windows asks for reduced animation or `PLICO_REDUCE_MOTION=1` is set.
