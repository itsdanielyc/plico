# The miille family foundation: tokens, controls, chrome and dialogs

**Status** Accepted for the design branch

Plico follows the miille family contract in [docs/design/miille.md](../design/miille.md): a grey desk, a white sheet, black ink, with Onda as the reference. This record covers the foundation the window and the dialogs are drawn from. The shell's layout is a separate step.

Theme values live in `Theme/Tokens.axaml`, merged from `App.axaml`, with a light and a dark dictionary. The family brushes keep their shared names (`Base`, `Panel`, `Raised`, `Sunken`, `Hover`, `Pressed`, `Selected`, `Line`, `LineStrong`, `Fg`, `Fg2`, `Fg3`, `FgDisabled`, `OnFg`, the status pairs, `Scrim` and the graphite `Tile`), so the same words mean the same values in every miille app. Avalonia has no `color-mix`, so the hover and pressed mixes are resolved values. Plico adds `Desk` for the page canvas, `Paper`, and its meaning colours. Earlier keys that windows still use point at family values with `StaticResource`, and new code uses the family names. Radii, shadows, `InterFont` and `MonoFont` are tokens too. Before this, `InterFont` was referenced by every window but never defined.

Fluent supplies the control templates. Its accent colours, its neutral ramp and the state keys of every control Plico uses or might open are overridden in both dictionaries, so no system blue and no Fluent grey reaches a dialog. A scan of every colour resource the Fluent assembly defines, resolved in both themes, finds nothing outside the family status colours. Shared control classes live in `Theme/Controls.axaml`: buttons (primary, secondary, danger, ghost, icon, link), fields, keycaps, chips, plain lists, menus, callouts and the dialog parts.

Keyboard focus is an ink ring with a 2 px gap, drawn by `FocusRing` as the adorner layer's default focus adorner. It reads the focused control's corner radius, so the ring follows rounded buttons, rows and fields. This reverses the earlier ruling that focus uses the system accent.

On Windows 11 `WindowChrome` tints each window's native caption and border to `Base` and its caption text to `Fg` through `DwmSetWindowAttribute`, when the window opens and when the theme changes. Other systems keep their native caption. The brand kit's `plico.ico` is the executable's icon and every window's icon.

Dialogs are separate windows with the `dialog` class: raised, a 20 px heading, 15 px body text, and a footer under a hairline with the actions on the right and the primary last. `DialogLayout` builds the dialogs written in code in the same shape. Every default button is primary.

**Consequences.** Names, types and plain-text button content are unchanged, so the window tests and the native journey drive the same controls. The native dark check waits for the dark `Base`, RGB 11,11,12, because the main window is now the desk. Menus and tooltips have no drop shadow: Avalonia's Windows popups are transparent windows sized to their content, so a shadow would be clipped unless the menu moved away from its anchor. They keep the shadow's 1 px ring as a hairline. Fluent draws menu shortcuts as text, so they use the keycap's type rather than a drawn keycap.
