# UNMA v0.10.0 – Consolidated operations release

UNMA v0.10.0 is the stable consolidation of the operator-focused 0.9.19
through 0.9.26 development line. It brings the complete workflow together in
one release while retaining public API V1 and persistence schema 20.

## Included workflows

- Optional Keybind Framework 2.0.2+ integration for primary and secondary
  shortcuts, with safe built-in fallbacks and text-field focus protection.
- Per-slot, panel, and global acknowledgement plus cyclic alarm navigation.
- Searchable alarm history with state/severity filters, game-time timestamps,
  RFC 4180 CSV export, and JSON export.
- Atomic global-panel duplication with deep-copied, deliberately disabled
  custom rules.
- Game-time activation/reset delays, minimum active time, per-condition
  hysteresis, persisted timing state, and per-slot audio snooze.
- One-shot escalation and bounded operator attention that only opens and
  focuses UNMA state.
- A shared instrument Historian with statistics, linear trend quality, and
  directed ETA.
- Operational alarm areas with scoped dashboard, acknowledgement, and next
  navigation while the underlying acknowledgement state stays global.
- A read-only Incident Lens with scoped temporal clusters and separately
  labeled global alarm pressure.

## Final stabilization

- Native text-field focus suppresses all UNMA shortcut actions, including
  rebound letter keys.
- Scale-aware minimum sizes preserve the logical workspace of the main window,
  editor, and detached panels up to 200 percent within the available viewport.
- Configuration schema 21 or newer is detected before full deserialization.
  UNMA leaves the original configuration and backup artifacts untouched and
  blocks writes for that session rather than dropping unknown future fields.
- Public extension API and assembly binding remain V1; schema remains 20 and
  no migration is required from 0.9.26.
- Release build: zero warnings and zero errors.
- Automated verification: 125,569 core assertions plus all IL/reflection,
  localization, rollback, and deterministic-package checks.
- The release sequence intentionally moves directly from 0.9.26 to 0.10.0;
  no 0.9.27 package was published.

## Compatibility

- Captain of Industry: **0.8.6c**
- UNMA: **0.10.0**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Optional dependency: **Keybind Framework 2.0.2 or newer**
- Can be added to or removed from existing saves.

## Download and documentation

Download the package from the
[UNMA releases page](https://github.com/max2605/UNMA/releases). Close the game
and back up `Mods/UNMA` before updating.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

One board, one history, one coherent operating picture.
