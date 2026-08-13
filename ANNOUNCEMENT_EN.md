# UNMA v0.10.2 + v0.10.3 Hotfix – Operator ergonomics, accessibility, and input fixes

Released: **2026-08-12**

This combined announcement covers the v0.10.2 usability and accessibility
update and the immediately following v0.10.3 hotfix. **Install v0.10.3**; it
includes every v0.10.2 improvement together with the input-contrast and
window-dragging fixes described below.

## v0.10.2 – Operator ergonomics and accessibility

### A clearer alarm editor

- The **Message title** is now the first field and explicitly states that it
  appears on the alarm tile and in history.
- Every custom rule exposes its active state. Disabled rules and deliberately
  disabled panel clones can be reviewed and activated directly in the editor.
- A sticky action bar keeps validation, save, discard, and delete controls in
  view. It explains missing titles, target panels, or conditions and identifies
  invalid color or timing input instead of silently disabling Save.
- Narrow windows use responsive condition cards. At minimum size and 200%
  scaling, an extreme-compact footer keeps the form usable without consuming
  the complete editor body.
- Timing and escalation live in a collapsed **Advanced settings** section. Its
  summary reports defaults, configured values, or input that needs attention;
  hidden validation errors open the section automatically.
- `Ctrl+Enter` saves a valid rule with its current active state. `Esc` closes
  the editor or returns from the unsaved-changes prompt.

### Faster, safer operation

- Custom alarm tiles now provide a visible **Edit** action and an **Inactive**
  badge. Double-click remains available as a shortcut.
- Status and error messages stay visible outside long scrolling pages.
  Persistent errors can be dismissed explicitly.
- Localized tooltips, larger targets, and control metadata improve mouse and
  keyboard operation.
- Keyboard focus on UNMA controls blocks conflicting game and mod shortcuts;
  pointer activation releases non-text focus after the click completes.
- History filters expose all available choices instead of requiring blind
  cycling through values.

### Contrast, reduced motion, and persistent layouts

- Alarm tiles automatically choose black or white text for the strongest
  contrast against the configured active color.
- History uses stable, high-contrast state colors instead of blinking rows.
- **Reduced Motion** replaces blinking alarm backgrounds with a steady active
  highlight without changing alarm state, severity, acknowledgement, or sound.
- Main, editor, and detached windows retain their preferred position and size.
  Detached panels also retain their open state and remain single-instance per
  panel. Pinning, layout import, and Historian fullscreen use the same guarded
  layout behavior.

## v0.10.3 – Hotfix: input contrast and window dragging

- Every editable free-text, search, color, numeric, and timing field now uses a
  dark-green background with bold white text across normal, hover, active,
  selected, and focused states. Focused fields gain a green outline.
- The main window, alarm editor, and detached panels once again follow the
  pointer smoothly and retain their settled position after release.
- A v0.10.2 regression could reset a window position while dragging. v0.10.3
  reapplies viewport correction only after a real resolution or UI-scale
  change, captures the final position after UI layout settles, and corrects it
  only when the window would otherwise remain outside the visible area.
- Window resizing, pinning, preferred layouts, detached-panel state, and
  Historian fullscreen behavior remain intact.

## Compatibility and verification

- Captain of Industry: **0.8.6c**
- Recommended UNMA version: **0.10.3**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Public extension API and assembly binding: **V1**
- World persistence schema: **20**; no migration is required
- Can be added to or removed from existing saves

All 21 language catalogs retain the same 1,069 keys. MultiLangLib remains an
external dependency and is not bundled in the UNMA archive. The final v0.10.3
release passed 138,275 core assertions, the warning-free Release build, all
IL/reflection, localization and rollback checks, and package verification.

## Download and documentation

Download **v0.10.3** from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

One board, one history, one coherent operating picture.
