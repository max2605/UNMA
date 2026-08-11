# UNMA v0.10.1 – Cross-save configuration profiles

Released: **2026-08-11**

UNMA v0.10.1 adds a controlled way to carry selected operator preferences from
one Captain of Industry save to another without copying world state.

## Recommended quiet baseline

Only when `%LOCALAPPDATA%\UNMA\profiles\default.json` is genuinely absent does
UNMA create and persist **UNMA Recommended Quiet**. Exactly recognized,
unchanged earlier built-ins – **UNMA Recommended Silent** with six Silent rules
and the intermediate Quiet profile with two additional Hidden rules – are
upgraded to the current Quiet profile in memory only; their seed files remain
unchanged. Divergent and custom profiles are neither supplemented nor
overwritten. The built-in profile is never imported automatically: players
still inspect the preview and explicitly confirm the merge.

The recommended profile sets `UpgradeInProgress`, `DowngradeInProgress`,
`VehicleGoalStruggling`, `VehicleNoReachableDesignations`,
`NoTreesToHarvest`, and `ExcavatorHasNoValidTruck` to Silent. Silent disables
only UNMA's sound. The original Captain of Industry notification remains
unchanged, while HOME visibility and history are retained.

It sets `TruckCannotDeliver` and `TruckCannotDeliverMixedCargo` to Ignored.
CoI withdraws and re-emits these vehicle notifications with transient
`NotificationId` values. UNMA now discards every new event before `SetAlarm`,
history creation, and persistence, reducing Incident Lens and save-processing
load. Confirmed import and configuration normalization remove matching active
states and memories, plus older global history when no more-specific
non-ignored entity or prototype rule applies. The original Captain of Industry
notification remains visible and unchanged. `CannotDeliverFromMineTower`,
`VehicleGoalUnreachable`, and `VehicleNoFuel` remain normal and audible.

## Cross-save workflow

- Save one default profile under **OPTIONS**, selecting notification rules,
  system-alarm configuration, alarm colors/UI scale, and optionally window
  positions and sizes. Window layout starts unselected.
- Preview an import before applying it. UNMA reports new, changed, unchanged,
  and skipped values, then atomically merges only the selected categories into
  the target world. Unrelated target settings remain intact.
- Transfer Vanilla rules keyed by stable `NotificationType` and entity
  prototype, including their sound assignment and automatic acknowledgement.
- Safely skip exact-entity rules because entity IDs belong to one world. Both
  preview and result report those skipped rules.
- Validate edited or damaged profile values before merging them and report
  unavailable sounds in the preview instead of hiding compatibility problems.
- Keep history, active alarm and acknowledgement state, running timers,
  escalation and snooze state, and every other runtime memory out of the
  profile.
- Keep imported ignore rules scoped to UNMA. They do not disable or alter the
  original Captain of Industry notification.

The atomically written profile is stored separately from world configurations
at `%LOCALAPPDATA%\UNMA\profiles\default.json`. Global startup settings from
`config.json`, including master audio enablement and volume, already apply
across saves and are not duplicated in the profile.

Public extension API and assembly binding remain V1. World persistence remains
on schema 20; no world migration is required for v0.10.1.

## Compatibility

- Captain of Industry: **0.8.6c**
- UNMA: **0.10.1**
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
