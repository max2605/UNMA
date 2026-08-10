# UNMA v0.9.20 – History 2.0

UNMA v0.9.20 turns the persistent alarm history into an operator-friendly
diagnostic tool. Large histories can now be searched, filtered, timestamped,
and exported without changing any alarm state.

## What changed in v0.9.20

- Free-text search covers message, detail, source, panel ID, and alarm ID.
- State filters include all, open, completed, and the exact `K`, `KQ`, `KG`,
  and `KGQ` states.
- A severity filter combines with state and text filters.
- New history entries retain separate game-tick timestamps for raise, clear,
  and acknowledgement transitions.
- The visible time column shows the latest transition as game year, month, and
  day. Legacy entries remain readable with an unknown date.
- **CSV** and **JSON** export exactly the currently filtered rows to
  `%LOCALAPPDATA%\UNMA\exports`.

CSV output follows RFC 4180 quoting and CRLF rules. JSON and CSV both preserve
raw tick values, Unicode labels, sources, panel IDs, and stable sequence order
for external analysis.

## Compatibility and safety

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.20**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.
- Existing histories need no manual migration; invalid timestamp values are
  normalized safely while loading.

This release also includes a dependency-free, game-tick timing policy for
future alarm delays and hysteresis. Its legacy defaults preserve the current
immediate behavior, so no existing rule changes behavior in v0.9.20.

## Download and documentation

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

Keep the history searchable and the coal moving.
