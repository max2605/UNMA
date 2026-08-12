# UNMA v0.10.2 – Operator ergonomics and accessibility

Released: **2026-08-12**

UNMA v0.10.2 makes alarm creation, editing, and daily operation easier to
understand without changing the public extension API or world-save schema.

## A clearer alarm editor

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

## Faster, safer operation

- Custom alarm tiles now provide a visible **Edit** action and an **Inactive**
  badge. Double-click remains available as a shortcut.
- Status and error messages stay visible outside long scrolling pages.
  Persistent errors can be dismissed explicitly.
- Localized tooltips and control metadata describe tile actions, and larger
  targets improve mouse and keyboard operation.
- Keyboard focus on UNMA controls blocks conflicting game and mod shortcuts;
  pointer activation releases non-text focus after the click completes.
- History filters expose all available choices instead of requiring blind
  cycling through values.

## Contrast and reduced motion

- Alarm tiles automatically choose black or white text for the strongest
  contrast against the configured active color.
- History uses stable, high-contrast state colors instead of blinking rows.
- **Reduced Motion** replaces blinking alarm backgrounds with a steady active
  highlight. Alarm state, severity, acknowledgement, and sound behavior remain
  unchanged. The setting is included in appearance transfer profiles.

## Window layouts that respect user intent

Main, editor, and detached windows now retain their preferred position and
size. Detached panels also retain their open state and are single-instance per
panel. Temporary viewport or UI-scale clamps no longer overwrite the stored
layout, and the preferred layout returns when space becomes available again.
Pinning, rapid layout updates, imported layouts, and Historian fullscreen use
the same guarded behavior.

## Compatibility and verification

- Captain of Industry: **0.8.6c**
- UNMA: **0.10.2**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Public extension API and assembly binding: **V1**
- World persistence schema: **20**; no migration is required
- Can be added to or removed from existing saves

All 21 language catalogs contain the same 1,069 keys. MultiLangLib remains an
external dependency and is not bundled in the UNMA archive. The release passed
138,229 core assertions, the warning-free Release build, all IL/reflection,
localization and rollback checks, and package verification.

## Download and documentation

Download the package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

One board, one history, one coherent operating picture.
