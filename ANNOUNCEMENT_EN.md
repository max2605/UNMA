# UNMA v0.10.3 – Input contrast and window dragging

Released: **2026-08-12**

UNMA v0.10.3 improves the readability of every editable field and restores
reliable title-bar dragging without changing the public extension API or
world-save schema.

## High-contrast input fields

- Every editable free-text, search, color, numeric, and timing field now uses a
  dark-green background with bold white text.
- Normal, hover, active, selected, and focused states retain the same readable
  treatment instead of falling back to a low-contrast default.
- Focused fields keep the dark-green background and gain a green outline.

## Reliable window dragging

- The main window, alarm editor, and detached panels once again follow the
  pointer smoothly when dragged by their title bars.
- Releasing the pointer now retains the settled position instead of snapping
  the window back to its previous coordinates.
- Viewport constraints are reapplied only after a real resolution or UI-scale
  change. Final drag coordinates are captured after UI layout settles and are
  corrected only when the window would otherwise remain outside the visible
  area.
- Window resizing, pinning, preferred layouts, detached-panel state, and
  Historian fullscreen behavior remain intact.

## Compatibility and verification

- Captain of Industry: **0.8.6c**
- UNMA: **0.10.3**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Public extension API and assembly binding: **V1**
- World persistence schema: **20**; no migration is required
- Can be added to or removed from existing saves

All 21 language catalogs retain the same 1,069 keys. MultiLangLib remains an
external dependency and is not bundled in the UNMA archive. The release passed
138,275 core assertions, the warning-free Release build, all IL/reflection,
localization and rollback checks, and package verification.

## Download and documentation

Download the package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

One board, one history, one coherent operating picture.
