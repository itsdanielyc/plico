# The miille family

Plico, Corto and Onda are different tools by one maker. They should feel like siblings: the same
paper, the same ink, the same hand, even though each does a different job. This file is the shared
contract. It is identical in every miille app repository; the app's own `DESIGN.md` records what is
particular to that app and links here.

Onda (`miillehq/onda`, `ui/src/styles/tokens.css`) is the reference implementation. When this file
and Onda disagree, Onda is right and this file gets fixed.

## The idea in one line

**A grey desk, a white sheet, black ink.** The window's frame is a quiet grey surface. The work sits
on a white sheet laid on it, with a rounded corner and a hairline edge. Ink is the only accent. The
graphite tile from the brand kit is the one material, kept for brand moments.

## What makes a screen read as miille

Check a screen against this list. Each item is observable.

1. **The desk.** Title bar and side navigation share one grey surface (`base`, #F3F3F4 light /
   #0B0B0C dark), with no line between them. A native caption is tinted to that grey (Windows
   `DWMWA_CAPTION_COLOR` and `DWMWA_BORDER_COLOR`); a drawn one is 44 px with the mark and the
   name at 13 px / 600.
2. **The sheet.** The working area is a `panel` (#FFFFFF / #161618) inset 6 px below the title bar,
   with a 12 px top-left radius and a 1 px `line` edge drawn as a shadow (`-1px -1px 0 line`). It is
   not a card floating with margin on every side; it runs to the window's right and bottom edges.
3. **Ink is the accent.** Primary buttons are filled with `fg` (#0F0F10 light, #F4F4F5 dark) and
   carry `on-fg` text. Selection, focus and "on" states use ink. There is no brand colour. Colour
   appears only for status (green, amber, red) or because the content brings it (a photo, a tag).
4. **Big, tight headings.** Page titles are 30 px, weight 720, tracking -0.018em. Section titles 20 px
   / 680. Row titles 16 px / 620. Nothing on screen is smaller than 13 px.
5. **One quiet subtitle.** Under a page title, one line in `fg-2` (15 px) says what the page is for,
   in plain words, sometimes ending with an inline link ("Change").
6. **Hairlines, not shadows.** In-flow surfaces (cards, row groups, inputs) use 1 px lines. Shadows
   belong to things that float: menus, dialogs, toasts, the palette, keycaps, paper.
7. **Rows with icon tiles.** Settings and lists are grouped rows on a 16 px-radius surface: a 36 px
   `sunken` icon tile, a 16 px title, a 14 px `fg-2` description, the control on the right.
8. **The graphite tile.** `#262628 → #0F0F10 → #040404`, top to bottom, with a 1 px white inner top
   line at 14 % and a soft radial light. It marks brand moments only: a hero, an onboarding art
   panel, an empty-state icon, the app mark tile. Never chrome, never a button.
9. **Keycaps.** Every shortcut that is mentioned is drawn as a keycap: 26 px tall, 7 px radius,
   `line-strong` border with a 2 px bottom edge, 13 px / 620.
10. **The sidebar.** 252 px. At the top a 40 px search field that opens the command palette, with a
    `Ctrl K` keycap (`⌘K` on a Mac). Places are 42 px rows: 18 px icon, 15 px / 520 label, count at
    the right in `fg-3`. The current place is a white pill with a hairline and a 1 px shadow, plus a
    3 px ink bar at the window's left edge. A status card sits at the foot, then Settings.
11. **A status card, not a status bar.** The app's live state (ready, working, needs attention) is a
    small white card at the foot of the sidebar with a coloured dot, a 15 px / 640 title and a 13 px
    detail line. Selecting it opens the setting that explains it.
12. **Undo instead of "Are you sure?".** Anything reversible happens immediately and offers Undo in a
    toast. Only what cannot be undone asks first, and the dialog names the thing.
13. **Ink toasts.** Toasts are `fg` with `on-fg` text, 14 px radius, with one action ("Undo") and a
    close button. They sit at the bottom right, unless the app's most-pressed control lives there
    (Plico's Mark reviewed); then they rise from the bottom of the main content. A toast never
    covers the control that caused it, and never carries information that is not also on screen.
14. **Sentence case and plain words.** "Mark reviewed", not "Mark Reviewed" or "MARK AS REVIEWED".
    A label says what happens. An ellipsis appears only on a button that opens a native picker or
    a dialog that asks for more ("Choose picture…"), never on a value or a field. No exclamation
    marks, apologies or "successfully". British spelling.
15. **Motion that explains.** `cubic-bezier(0.2, 0.8, 0.2, 1)`; 120 ms hover, 180 ms state, 260 ms
    entrances; transform and opacity only. Nothing moves while the app is idle. Reduced motion
    drops every duration to zero and keeps every state.
16. **The app's own mark, alive.** Each app turns its mark into one piece of interface: Onda's wave
    is the pill that listens, Corto's play mark is the play button and playhead, Plico's tray is
    where a reviewed page lands. One signature per app; everything else stays quiet.
17. **Whose words.** Interface text is Inter. Words that belong to the person (what they said, what
    their paper says) are set in Newsreader where the app shows them as reading text, so it is
    always clear whose words are on screen.
18. **Diagrams instead of paragraphs.** Where a setting needs explaining (where audio goes, where a
    file goes), draw it: three small nodes joined by a dashed line, the middle one a graphite tile.

## Tokens

Names are shared across apps so a person moving between repositories finds the same words. CSS
apps use them as custom properties (`--base`); Avalonia apps use them as brush keys (`Base`).

### Colour

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `base` | #F3F3F4 | #0B0B0C | the desk: title bar, sidebar, gutters |
| `panel` | #FFFFFF | #161618 | the sheet, cards, row groups |
| `raised` | #FFFFFF | #1C1C1F | dialogs, menus, the palette, popovers |
| `sunken` | #F5F5F6 | #1D1D20 | wells, icon tiles, segmented tracks, keycap faces |
| `hover` | rgba(15,15,16,.05) | rgba(255,255,255,.06) | pointer over a quiet control |
| `pressed` | rgba(15,15,16,.09) | rgba(255,255,255,.10) | pressed quiet control |
| `selected` | #FFFFFF | #1F1F22 | the current nav pill, the chosen segment |
| `line` | #E2E2E5 | #2A2A2E | hairlines |
| `line-strong` | #CDCDD2 | #3B3B41 | control borders |
| `fg` | #0F0F10 | #F4F4F5 | text, ink fills |
| `fg-2` | #45454B | #C4C4CA | secondary text (9.5:1) |
| `fg-3` | #66666D | #9D9DA5 | the faintest meaningful text (5.7:1) |
| `fg-disabled` | #9A9AA1 | #6A6A72 | disabled text only |
| `on-fg` | #FFFFFF | #0F0F10 | text on ink |
| `scrim` | rgba(15,15,16,.32) | rgba(0,0,0,.55) | behind dialogs |
| `ok` / `ok-bg` | #16794A / #E8F4ED | #4CC98B / rgba(76,201,139,.12) | live, done |
| `warn` / `warn-bg` | #8A5300 / #FDF3E1 | #F0B45A / rgba(240,180,90,.12) | due soon, needs a look |
| `err` / `err-bg` | #B42318 / #FDECEB | #FF7B6E / rgba(255,123,110,.12) | failed, destructive |
| `tile` | #262628 → #0F0F10 (55%) → #040404 | #2C2C2F → #1A1A1C (60%) → #121214 | brand moments |
| `tile-fg` / `-2` / `-3` | #FFFFFF / #CACAD0 / #A3A3AA | same | text on the tile |
| `tile-line` | rgba(255,255,255,.14) | same | the tile's inner top edge |

### Type

Inter Variable for the interface, bundled. Newsreader Variable for the person's own words where
they are shown as reading text. Cascadia Mono (then the platform monospace) for paths and code.

| Role | Size | Weight | Tracking | Line |
| --- | --- | --- | --- | --- |
| Smallest (meta, counts, keycaps) | 13 | 540–620 | 0 | 1.4 |
| Buttons, chips, secondary rows | 14 | 560 | 0 | 1 |
| Body | 15 | 450–500 | 0 | 1.5 |
| Row titles | 16 | 620 | 0 | 1.35 |
| Section titles | 20 | 680 | -0.01em | 1.3 |
| Page titles | 30 | 720 | -0.018em | 1.2 |
| Hero and onboarding titles | 30–34 | 720 | -0.018em | 1.15 |
| Big numbers | 32 | 720 | -0.03em | 1.15 |

Sentence case everywhere. Tabular numerals for counts, sizes, dates and times.

### Geometry

| Token | Value |
| --- | --- |
| Spacing grid | 4 px (4, 8, 12, 16, 20, 24, 32, 40) |
| `radius-sm` | 6 px (small chips inside controls, scrollbar thumbs) |
| `radius-control` | 10 px (buttons, inputs, nav rows, segmented track 12 / segment 9) |
| `radius-card` | 16 px (cards, row groups, callouts, empty states) |
| `radius-hero` | 22 px (hero and art tiles) |
| dialog radius | 18 px; palette 16 px; toast 14 px; status card 14 px |
| `control-h` / `control-h-sm` | 38 px / 32 px |
| nav row | 42 px; search field 40 px |
| sidebar | 252 px |
| page | max 920 px including 44 px side padding (832 px of text), left-aligned in the sheet; 34 px top, 56 px bottom |
| `shadow-1` | `0 1px 2px rgba(15,15,16,.05), 0 0 0 1px line` (dark: `0 0 0 1px line`) |
| `shadow-pop` | `0 16px 40px rgba(15,15,16,.16), 0 2px 8px rgba(15,15,16,.08), 0 0 0 1px rgba(15,15,16,.08)` (dark: `0 18px 44px rgba(0,0,0,.5), 0 0 0 1px line-strong`) |
| focus ring | `0 0 0 2px panel, 0 0 0 4px fg` |

### Motion

| Token | Value |
| --- | --- |
| `ease-out` | cubic-bezier(0.2, 0.8, 0.2, 1) |
| `ease-in-out` | cubic-bezier(0.6, 0, 0.2, 1) |
| `dur-fast` / `dur-base` / `dur-slow` | 120 / 180 / 260 ms |

## Components

The values below are Onda's; reproduce them, adapting only what the toolkit forces.

- **Primary button.** `fg` fill, `on-fg` text, 38 px, 10 px radius, 14 px / 560, 14 px side padding,
  16 px icon with 8 px gap. Hover: `fg` mixed 86 % with `panel`. Disabled: `sunken` fill,
  `fg-disabled` text, `line` border.
- **Secondary button.** `panel` fill, 1 px `line-strong` border, `fg` text. Hover: border darkens
  towards `fg` by 30 %, fill tints 4 %.
- **Ghost button.** Transparent, `fg-2` text; hover `hover` fill and `fg` text.
- **Icon button.** 34 px square, 10 px radius, `fg-2` icon; hover `hover` fill.
- **Link.** `fg`, 14 px / 560, underline offset 3 px in `line-strong`, solid on hover.
- **On the tile.** Inverse button: #FFFFFF fill, #0F0F10 text. Inverse ghost: rgba(255,255,255,.06)
  fill, rgba(255,255,255,.24) border, white text.
- **Input.** 38 px, 10 px radius, `line-strong` border, `panel` fill, 15 px text, `fg-3` placeholder.
  Focus: `fg` border plus a 1 px `fg` ring. Invalid: `err` border, message below in 13 px `err`.
- **Select.** As input, chevron at 12 px from the right, min 190 px, max 320 px with an ellipsis.
- **Switch.** 42 × 24, pill. Off: `sunken` track, `line-strong` border, `fg-2` thumb. On: `fg` track,
  `on-fg` thumb.
- **Segmented control.** `sunken` track, 3 px padding, 12 px radius, inset `line` hairline; segments
  32 px, 9 px radius, 14 px / 540; the chosen one `panel` with a 1 px shadow and hairline, 620.
- **Radio cards.** Cards with a hairline; the chosen card gets a 2 px `fg` border. A radio dot at
  the top right. Used for choices that deserve a preview (appearance, quality, export preset).
- **Chip.** 28 px pill, `sunken` fill with an inset `line`, 13 px / 560, `fg-2`. Strong chip: `fg`
  fill, `on-fg` text. Status chips use the `-bg` tint with the status colour.
- **Keycap.** 26 px, min width 26, 7 px radius, `panel` face, 1 px `line-strong` border with a 2 px
  bottom, 13 px / 620. Medium keycap 32 px / 14 px. Hero keycaps on the tile use the light tile
  gradient (#FFFFFF → #F2F2F3 → #DADADC) with an inset bottom shadow.
- **Card.** `panel`, 16 px radius, `shadow-1`, 22 × 24 padding.
- **Rows.** `panel`, 16 px radius, `shadow-1`; each row min 72 px, 16 × 20 padding, 16 px gap,
  rows divided by `line`.
- **Callout.** `sunken` with an inset hairline, 16 px radius, 40 px `panel` icon tile, 16 px / 640
  title, 14 px text. Warn and error variants use the status tints.
- **Empty state.** Centred, 1 px dashed `line-strong`, 16 px radius, 44 px graphite icon tile with a
  white glyph, a 16 px title, one 14 px sentence, one action.
- **Dialog.** `raised`, 18 px radius, `shadow-pop`, 480 px (600 large). Head 22 × 24 padding with a
  20 px title and a 14 px `fg-2` description; foot divided by `line`, actions right, primary last.
  Enters with an 8 px rise and a 0.985 scale over 260 ms.
- **Command palette.** 560 px, 16 px radius, starts at 12 % of the window height; a 54 px input
  with 17 px text; groups in 13 px / 600 `fg-3`; 40 px items, 9 px radius, `hover` for the active
  one; shortcuts as keycaps on the right.
- **Toast.** Bottom right, 380 px max, `fg` fill, `on-fg` text, 14 px radius, a 14 px / 620 title
  and a 13 px body at 84 %, one action as a 28 px tinted button, then a close button.
- **Status card.** `panel`, 14 px radius, hairline, 14 px padding; a 9 px dot with a 3 px halo in
  its own colour, a 15 px / 640 title, a 13 px `fg-2` detail line, an optional second line under a
  `line` divider.
- **Menus.** `raised`, 12 px radius, `shadow-pop`, 4 px padding, 32–36 px items with 8 px radius;
  every item has an icon or none do; shortcuts as keycaps on the right.

## Voice

- Say what happened or what a control does. "Saved", "Marked reviewed", "Couldn't read this page."
  Failures follow two shapes: "Couldn't {verb}" and "{Thing} didn't {verb}".
- Second person for the person, the app named in the third person ("Plico will file it here"),
  first person only in the person's own toggles ("Remind me", "Catch up when I open Plico").
- Short. Titles are two to five words; a description is one sentence, at most two.
- Curly quotes around the person's own words, "·" between facts in a meta line, "›" for paths
  ("Settings › Intake"), "…" for something in progress ("Reading 3 of 12…").
- Speak the person's platform: "this PC" on Windows and "this computer" elsewhere, "Match Windows",
  Ctrl or ⌘, the platform's own settings paths.
- Privacy is stated once, where it matters, as a fact: "Text recognition runs on this computer."
  Not repeated on every screen.
- Errors say what went wrong and offer one way forward, as a button.
- Numbers are digits with tabular figures; sizes in MB; dates in the person's locale.
- The parent shows only where it should: "Plico by miille" in About and the installer, nowhere in
  everyday chrome.

## Behaviour

- Edits save as they happen. If saving fails, the change is rolled back and a message says why.
- Deletion is immediate with Undo. Only what cannot be undone asks first.
- `Ctrl K` / `⌘K` opens a palette that reaches every page, setting and action, with its shortcut.
  `Ctrl ,` / `⌘,` opens settings. `Esc` closes the topmost layer.
- Focus is always visible and always lands somewhere sensible after an action.
- Theme follows the system by default; Settings offers it, Light and Dark, previewed as small
  window drawings. The choice also sets the native window theme.
- The app adapts to the window, not the device: below about 1040 px decorative art goes first, then
  grids stack, then the sidebar narrows to icons. Each app records its own breakpoints.
- Nothing animates or polls while nothing changes.

## Per toolkit

- **CSS (Onda, Corto):** custom properties with the names above in `tokens.css`; components read
  tokens, never hex values.
- **Avalonia (Plico):** brushes with PascalCase keys (`Base`, `Panel`, `Raised`, `Sunken`, `Hover`,
  `Pressed`, `Selected`, `Line`, `LineStrong`, `Fg`, `Fg2`, `Fg3`, `FgDisabled`, `OnFg`, `Ok`,
  `OkBg`, `Warn`, `WarnBg`, `Err`, `ErrBg`, `Tile`, `TileFg`, `TileFg2`, `TileFg3`, `TileLine`) in
  `App.axaml` light and dark theme dictionaries; control classes in `Theme/Controls.axaml`.

## Icons

Lucide, stroke 2, coloured by the text level they sit in (`fg-2` idle, `fg` active, `fg-3` quiet).
Sizes: nav 19, buttons 16, settings row tiles 18 in a 36 tile, callouts and empty states 20 in a
40–44 tile, chips 14, list icon buttons 17 in a 34 button, dialog close 18, toast 18. Every item
in a menu has an icon, or none does.

## Desktop manners

Default arrow cursor everywhere (no hand), interface text not selectable, thin scrollbars in
`line-strong`, no rubber-band scrolling of the window, the browser's context menu replaced or
suppressed outside text.
