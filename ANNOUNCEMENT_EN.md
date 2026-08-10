# UNMA v0.9.21 – Reusable panel workflows

UNMA v0.9.21 makes recurring annunciator layouts much faster to build. A
configured global panel can now be duplicated as an independent starting point
without copying live alarm state or history.

## What changed in v0.9.21

- **DUPLICATE PANEL** is available in every global panel's settings.
- The copy retains its columns, filters, automatic-source settings, exclusions,
  and permanent slot order.
- Every assigned custom alarm is deep-copied with a collision-free ID.
- Cloned custom alarms deliberately start disabled so thresholds and targets can
  be reviewed before they become operational.
- Custom alarm links are cleared, while live state, acknowledgements, and
  history remain attached only to the original alarms.
- Dashboard and entity panels cannot be duplicated because they have different
  ownership and lifecycle semantics.
- Orphaned custom-rule slots from damaged legacy configurations are skipped and
  reported instead of making the whole operation unsafe.

The copy is persisted as one atomic operation. If saving fails, UNMA removes
the new panel and all cloned rules again and preserves the original
configuration.

## Compatibility and safety

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.21**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.
- No schema migration is required for existing worlds.

## Download and documentation

Download the current package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

Copy the panel, verify the alarms, then keep the factory moving.
