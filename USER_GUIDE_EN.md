# UNMA User Guide

This guide applies to **UNMA 0.10.1** and **Captain of Industry 0.8.6c**.

UNMA (Universal Alarm Annunciator) adds a configurable industrial annunciator
to Captain of Industry. It mirrors game notifications, keeps a persistent alarm
history, monitors important settlement values, and lets you build custom alarm
rules for game objects or global game variables.

Required dependency: **MultiLangLib 0.1.0 or newer**.
Optional dependency: **Keybind Framework 2.0.2 or newer** for configurable
primary and secondary shortcuts. UNMA remains fully usable without it.

## Installation and updates

1. Download the current UNMA release.
2. Extract the archive into the Captain of Industry `Mods` directory. The final
   path must contain `Mods/UNMA/manifest.json` and `Mods/UNMA/UNMA.dll`.
3. Install and enable MultiLangLib 0.1.0 or newer.
4. Enable UNMA in the game's mod menu.
5. Load or start a game.

Before updating UNMA, close the game and back up the existing `Mods/UNMA`
folder. World-specific data is stored there as `unma-world-<GameId>.json` plus
backup files, and user-added sounds may be stored under `Sounds/`. Extract the
new `UNMA` folder over the existing folder and allow release files to be
replaced. The release archive contains neither world data nor user-added
sounds. Do not delete the complete old folder first unless you have backed up
and will restore those files.

UNMA can be added to or removed from an existing save.

## Quick start

1. Press **F8** to open or close the main UNMA window.
2. Alternatively, use the floating launcher, which starts near the left edge.
3. Open **ANNUNCIATOR** to see HOME and your permanent panels. Select **ALL**,
   **UNASSIGNED**, or an operational area to filter that board.
4. Use the **Q** button on one slot, **PANEL ACK** for the displayed panel, or
   **MASTER ACK** for every new and cleared-but-unacknowledged alarm.
5. Press **NEXT ALARM** or **Left Shift + F8** to cycle through the panel's
   unacknowledged alarms and focus their game object where available.
6. Open **HISTORY** to inspect previous alarm events.

### Configurable shortcuts

When the optional **Keybind Framework 2.0.2+** is active, its settings page
offers primary and secondary bindings for opening UNMA, acknowledging all
alarms, selecting the next unacknowledged alarm, and muting alarm audio for
five real-time minutes. The built-in fallbacks are **F8** for the main window
and **Left Shift + F8** for the next alarm; the two potentially disruptive
actions start unbound. Muting audio never acknowledges or clears an alarm.
UNMA suppresses all four bindings while any native UNMA text field owns
keyboard focus, so a rebound letter key cannot trigger an operator action
while text is being entered.

### Launcher and native windows

The launcher is shown only during active gameplay and while the main window is
closed. Its `+N` suffix is the number of unacknowledged alarms. Drag the narrow
arrow handle in any direction to move it away from other HUD elements. UNMA
keeps it inside the visible viewport and saves its position per world.

The launcher, main window, alarm editor, detached panels, controls, and
instrument drawings all belong to Captain of Industry's native game-UI
hierarchy. Clicking a UNMA window brings its frame and content to the front as
one unit. A game window activated afterwards can cover it normally; there is no
separate UNMA overlay that remains above the game window.

The main window, editor, and detached panels can be moved, pinned, and resized
from the lower-right handle like other game windows. **MINIMIZE**, the native
close button, and **F8** close only the main window and restore the launcher;
an open editor or detached panel remains independent. Window sizes are kept
inside the current viewport.

Pointer and mouse-wheel input inside a visible UNMA window stays with that
window instead of reaching the world behind it. While a UNMA text field is
active, keyboard input goes to that field. Moving focus away from all UNMA text
fields or closing the focused window releases the keyboard again.

### Main window tabs

| Tab | Purpose |
| --- | --- |
| **ANNUNCIATOR** | HOME, global panels, object panels, acknowledgement, and new alarm slots |
| **INSTRUMENTS** | Live gauges, calculated values, paper recorders, and instrument alarms |
| **HISTORY** | Completed and current alarm events |
| **SYSTEM** | Built-in health, food, and worker monitoring |
| **NOTIFICATION OPTIONS** | Per-notification sound, visibility, logging, and Vanilla behavior |
| **OPTIONS** | Content scale, alarm colors, cross-save profile, sound rescan, and integration diagnostics |

## Alarm states

UNMA follows the behavior of a traditional industrial annunciator.

| Code | State | Display behavior |
| --- | --- | --- |
| `K` | Active and not acknowledged | Flashes in its active color and repeats its sound |
| `KQ` | Active and acknowledged | Remains active without repeating its sound |
| `KG` | Cleared and not acknowledged | Flashes with black text on a white background |
| `KGQ` | Cleared and acknowledged | Completed history event |

Acknowledging an active alarm does not clear its active color. The color
remains until the monitored condition returns to normal.

Completed `KGQ` history entries remain stored until you delete them. Only
completed entries can be deleted. Deleting all completed events requires a
second press within five seconds.

The **HISTORY** toolbar searches message, detail, source, panel ID, and alarm
ID at the same time. Repeatedly press the state and severity buttons to cycle
their filters; both filters combine with the search. New events show the game
date of their latest raise, clear, or acknowledgement transition. Entries from
older UNMA versions remain visible with an unknown date.

**CSV** and **JSON** export exactly the currently filtered rows to
`%LOCALAPPDATA%\UNMA\exports`. CSV uses RFC 4180 quoting and JSON preserves the
raw game-tick timestamps for further analysis. Export does not remove or
change any history entry.

## Panels and alarm slots

### HOME

HOME is a live overview of alarms that are currently active. It shows `K` and
`KQ` alarms from all sources under **ALL** but does not own permanent slots.
Inactive, cleared, and empty slots are hidden from HOME.

### Operational areas

Operational areas organize global panels without creating another alarm,
history event, historian stream, or audio state. The filter row offers
**ALL**, **UNASSIGNED**, and every user-defined area:

- **ALL** preserves the complete board and HOME behavior.
- **UNASSIGNED** shows global panels that do not belong to an area.
- A concrete area shows only its member panels. Its HOME collects their active
  alarms and deduplicates them into one tile per underlying alarm.

The selected area chip shows its panel, active-alarm, and unacknowledged-alarm
counts. **AREA ACK** and **AREA NEXT** operate only on alarms visible through
the selected area or **UNASSIGNED** view. **MASTER ACK** always remains the
explicit global action.

Acknowledgement still belongs to the underlying alarm occurrence, not to an
area view. If the same alarm is visible through panels in several areas,
acknowledging it in one area acknowledges it everywhere. UNMA does not create
duplicate state or history for those views.

Open **⚙ AREAS** to create, rename, reorder, or mark areas for deletion. These
changes stay in one draft and are applied atomically only when saved. Deleting
an area never deletes its panels, slots, rules, or alarm states; its panels
become **UNASSIGNED**. Save, discard, or return to the draft when UNMA warns
about unsaved area or panel settings.

Assign a global panel from its gear-button settings. A duplicated panel keeps
the source panel's area. A new panel inherits the currently selected concrete
area; when created under **ALL** or **UNASSIGNED**, it starts unassigned.
Object panels and HOME are not assigned to areas.

### Incident Lens

The **INCIDENT LENS** appears only above the HOME dashboard. Its collapsed bar
shows the global pressure level and counts; **EXPAND** opens a read-only view
of temporal clusters among the active alarms in the current dashboard scope.
It does not appear on permanent global or object panels.

The grouping is deliberately a heuristic. Consecutive active alarm
occurrences whose raise times are no more than two game days apart form one
temporal incident cluster. **FIRST SIGNAL** is only the earliest observed
member of that cluster. It is not a confirmed cause, root cause, or proof that
one member triggered another.

Cluster membership follows the selected **ALL**, **UNASSIGNED**, or concrete
operational-area filter. The pressure indicator is intentionally global so an
operator cannot hide an island-wide storm by narrowing the board. It examines
the last ten game days and weights each occurrence by severity:

| Severity | Weight |
| --- | ---: |
| Notice | 1 |
| Warning | 2 |
| Critical | 4 |
| Emergency | 8 |

Pressure below 8 is **NORMAL**, 8–15 is **ELEVATED**, 16–31 is **STORM**, and
32 or more is **SEVERE**. The same summary reports both recent occurrences and
distinct alarm IDs; repeated occurrences therefore increase the first count
without pretending to be additional alarm types.

The expanded view renders at most six incident cards and eight members per
card. A `+ N MORE` line preserves the full counts when the display cap is
reached. **FOCUS** navigates to a member that is still visible and, where
available, its game object. Focus never acknowledges, hides, clears, deletes,
or silences an alarm and never changes history or audio state.

Incident snapshots are transient, derived results recalculated from the
current alarm and history snapshots. They add no saved fields and require
no new schema migration in 0.10.1. For performance, the UI requests at most
one result per frame and filter. Runtime history is copied only when its
revision changes, global pressure is bounded to the newest 8,192 occurrences,
and sorting plus analysis run outside the alarm lock. If revisions keep
changing, UNMA returns a coherent uncached result after at most two attempts
instead of blocking the render path.

### Global panels

Global panels are permanent annunciator boards that can collect game,
system, provider, and custom alarms.

- Create a panel with **+ PANEL** beside the global-panel tabs in
  **ANNUNCIATOR**.
- Use the adjacent gear button to change its name, column count, filters,
  automatic sources, and slot order.
- **DUPLICATE PANEL** creates an independent copy of that configuration,
  including its slot order, filters, and custom alarms. Cloned custom alarms
  receive new IDs and start disabled for safety; live alarm state and history
  are not copied. The new panel retains the source panel's area assignment.
- Add known alarms to free slots and move slots up or down.
- Newly discovered alarms that match an automatic source or filter are added
  without moving existing slots.
- Detach a panel to display it as a separate movable in-game board; see below.

### Object panels

Supported buildings, storages, vehicles, conveyors, and pipes have their own
panel.

1. Select the object in the game.
2. Click the golden **UNMA alarm bell** in its inspector.
3. UNMA opens the permanent panel belonging to that exact object.

The small arrow on an object-bound alarm slot centers the camera on its object
and opens the corresponding inspector.

The **Q** button acknowledges only that visible slot. **PANEL ACK** acknowledges
all unacknowledged states represented by the current panel, including every
underlying event combined into an object slot. **MASTER ACK** remains the
explicit global action.

The **Z** button snoozes only that slot's alarm audio for one game month. Its
badge changes to **AUDIO Z · 1 MONTH** and **R** resumes audio immediately.
Snoozing an aggregated slot covers every current occurrence behind it, but never
acknowledges, hides, clears, or removes an alarm from counters or history. A
later occurrence receives a new sequence and is audible again.

Double-click a custom alarm slot to open its rule directly in the editor.

### Detached panels

The currently displayed HOME, global, or object panel can be detached into an
independent native window. It shows the same panel state rather than creating a
second alarm state. You may open more than one view of the same panel; detached
boards display at most five columns.

Closing a detached window removes only that view. It does not delete the panel,
slots, or alarms. Detached window position and size are not persisted; a newly
detached view starts at a new default position. The underlying panel remains
saved per world.

## Creating a custom alarm

You can start a rule from a global panel or an object panel.

1. Open the target panel.
2. Click **+ NEW NOTIFICATION** or a free plus slot.
3. Select the source for the first condition.
4. Select a measured value.
5. Choose a calculation, comparison operator, and target value.
6. Click **+ ADD ROW**.
7. Add further rows if required.
8. Enter the notification text and choose severity, active color, sound, and
   acknowledgement behavior.
9. Save the notification.

Every condition row displays its current value while its source is available.

### Using a game object as the source

Select a building, storage, vehicle, conveyor, or pipe in the game and click
**APPLY CURRENT GAME SELECTION** in the editor.

UNMA discovers supported numeric and Boolean values, including:

- stored quantity, capacity, and fill percentage;
- product-specific storage quantities;
- conveyor and pipe contents;
- transport or vehicle cargo and capacity;
- public numeric or Boolean values exposed by compatible entities;
- additional metrics registered by active provider mods.

Product-specific values are discovered from products currently or previously
seen in the object. A completely unused empty object may initially expose only
general quantity and fill values.

### Using global variables as the source

Click **GLOBAL VARIABLES** instead of selecting a game object. Global
conditions do not depend on an individual entity and remain valid when
buildings are demolished.

Available categories currently include:

- total population and monthly population change;
- free or missing workers and worker reserve percentage;
- health, disease, pollution, expected losses, and disease duration;
- food reserve, starvation, and recent starvation deaths;
- worker-buffer and death-spiral indicators used by the built-in monitoring.
- global stored quantity, storage capacity, and fill percentage for every
  unlocked storable product;
- maintenance fill, reserve, capacity, last-month change, and current or
  maximum monthly demand for every visible maintenance type.

The picker shows live values and remains open while those values refresh.

Example:

```text
Worker reserve < 5
```

For example, search the metric picker for `Coal` and select
`Coal · global stored quantity` to create a low-stock notification.

### Comparisons

UNMA supports all six comparison operators:

```text
<   <=   =   !=   >=   >
```

Use **ABSOLUTE** to compare the measured value directly with the target.

Use **% OF** to compare one value with another value from the same source:

```text
Stored quantity % OF Storage capacity < 5
```

UNMA calculates `measured value / reference value × 100`. A missing, zero, or
negative reference value is treated as unavailable and does not activate the
condition. Values above 100 percent are not artificially limited.

### Alarm timing and hysteresis

Every custom rule can use three durations based on Captain of Industry's game
calendar:

- **ACTIVATION DELAY** requires the combined AND/OR condition to remain true
  before the alarm comes in;
- **RESET DELAY** requires it to remain false before the alarm clears;
- **MINIMUM ACTIVE** keeps an activated alarm standing for at least the chosen
  duration.

Set a value to `0` for the previous immediate behavior. Each normal numeric
condition also has a **HYSTERESIS** value. It creates a dead band around the
threshold so a fluctuating measurement does not repeatedly activate and clear
the alarm. Timers and hysteresis latches are saved per world and continue after
loading. Trend increase/decrease conditions intentionally do not use
hysteresis.

### Escalation and operator attention

Enable **ESCALATION** on a custom rule when an alarm that remains active should
be raised to a strictly higher severity after a selected game-time duration.
Choose an explicit escalation sound or keep **INHERIT BASE SOUND**. Escalation
starts a new occurrence, so it requires acknowledgement again and does not
inherit an occurrence-bound audio snooze from the earlier state.

The optional operator action can open the matching UNMA panel and scroll to
the alarm. A second mode also ends only UNMA's temporary five-minute mute. It
never moves the camera, opens an entity inspector, changes the global audio
setting, modifies a slot snooze, acknowledges an alarm, or controls a machine.

System-alarm stages expose the same actions. They run only when an already
active system alarm advances into a new stage, not on its initial activation.
For a staged escalation, configure a lower immediate stage and a higher stage
with an activation delay.

### Rules with several conditions or objects

After adding the first row, you can select another object in the game and click
**APPLY CURRENT GAME SELECTION** again. The existing rows remain in the draft.

- Select **AND** when every row must match.
- Select **OR** when any row may match.

A rule can therefore combine values from several objects. If an object used by
such a rule is permanently demolished or destroyed, UNMA removes the complete
rule so its logic cannot silently change. Temporary vehicle despawns do not
remove rules.

### Linking an object alarm to global panels

An alarm created for an object panel can also be displayed on one or more
global panels. Select the desired panels in the editor. This creates additional
display slots, not duplicate alarm states.

## Editing, closing, and deleting custom alarms

- The alarm editor is a separate, scrollable native window. It can remain open
  while the main window is minimized.
- Double-click a custom slot to edit its rule.
- If another unsaved draft is already open, UNMA keeps the old draft and shows
  a prominent warning instead of silently replacing it.
- Closing an editor that contains a draft offers four choices:
  - **SAVE & CLOSE** saves the rule and closes the editor;
  - **MINIMIZE** closes the window but keeps the draft for later;
  - **DISCARD** removes the unsaved changes.
  - **BACK TO EDITOR** cancels closing and returns to the draft.
- Use **EMPTY DRAFT** to reset the editor without saving.
- When editing an existing custom alarm, use **DELETE ALARM** and press it a
  second time to confirm deletion.

## Vanilla game notifications

Open **NOTIFICATION OPTIONS** to configure known Vanilla notification types.
They can be configured globally by notification type, for one exact object, or
for every object of the same prototype.

| Mode | UNMA sound | HOME / counters | History |
| --- | --- | --- | --- |
| **NORMAL** | Enabled | Visible | Stored |
| **LOG, SOUND OFF** | Disabled | Visible | Stored |
| **LOG, SOUND OFF, HIDE** | Disabled | Hidden | Stored |
| **DO NOT LOG / IGNORE COMPLETELY** | Disabled | Hidden | Not created |

Object-specific rules override prototype rules, which override global
notification-type rules. Completely ignoring a type
also removes matching active and recent events that UNMA can still identify
safely.

These settings affect only UNMA. They do not disable or modify the original
Captain of Industry notification.

Object panels also show inactive previews for known notifications that may
occur on that object. This allows you to configure them before their first
occurrence. A notification configured as **LOG, SOUND OFF, HIDE** remains
hidden from HOME, global panels, counters, and audio, but its real active color
and state are still shown in its own object panel. **IGNORE COMPLETELY** remains
invisible everywhere.

## System alarms

The **SYSTEM** tab contains the built-in health, food, and worker monitoring.
Each system alarm can be enabled, edited, or restored to its factory defaults.
Its stages expose measured value, operator, threshold, hysteresis, activation
and reset delay, minimum active time, severity, color, sound, and an optional
operator action. Each stage is timed independently before the highest
applicable severity and priority is displayed. Restoring factory defaults
requires a second press for confirmation.

The game's health value is not a conventional 0–100 percent scale. `10` is the
neutral base value, and health-related population loss starts below `0`. UNMA
uses the completed monthly value and considers disease, pollution, expected
population loss, and available worker reserve.

By default, **EMERGENCY** is reserved for an active health or hunger death
spiral. Worker shortages escalate only to **CRITICAL**.

## Sounds

UNMA includes a warning bell, industrial horn, motor siren, and several
synthesized signals. Sounds repeat while an alarm is unacknowledged.

To add a custom sound, copy a supported PCM WAV or Ogg Vorbis file to:

```text
Mods/UNMA/Sounds/
```

After adding files, use the re-read action in **OPTIONS**. Restart the game only
if a valid file is still not listed. Use only audio you created or are licensed
to use and redistribute.

Sound and automatic acknowledgement on clear can be configured per known alarm
type. Volume is a global mod setting. Custom rules choose their sound and
acknowledgement behavior directly in the editor.

## Options

The **OPTIONS** page is vertically scrollable. Keep the pointer over the page
and use the mouse wheel to reach all entries in a small window or at a large
content scale.

- Scale the content inside the main, editor, and detached windows from 75 to
  200 percent in 25-percent steps, or reset it to 100 percent. Native COI
  frames, navigation chrome, and the launcher continue to follow the game's
  own UI scale. Window minimums grow with the selected content scale within
  the available viewport; panel and area settings also stack their controls
  so required actions remain reachable at 200 percent.
- Edit and save the Warning, Critical, and Emergency colors.
- View the custom-sound directory and re-read supported WAV and Ogg files
  without restarting the game.
- Inspect information about system alarms, detached panels, the alarm state
  model, and external integrations.
- Reload provider JSON, API, language, and sound data and inspect integration
  diagnostics.

Global panels are managed in **ANNUNCIATOR**; alarm stages are edited in
**SYSTEM**. Audio enablement, volume, polling interval, and built-in monitoring
are mod settings whose startup defaults are listed below.

UNMA blocks clicks, drags, and mouse-wheel input inside its visible native
windows from affecting the game world behind them. Outside those windows,
building selection, camera movement, and zoom remain available. Native text
fields block game shortcuts only while they own keyboard focus.

The startup defaults in `config.json` are:

| Option | Default | Purpose |
| --- | ---: | --- |
| `showOnGameStart` | `true` | Open UNMA after loading a world |
| `enableAudio` | `true` | Repeat alarm sounds until acknowledgement |
| `audioVolumePercent` | `65` | Set UNMA sound volume from 0 to 100 percent |
| `pollIntervalMs` | `500` | Evaluate custom rules every 500 ms |
| `enableSystemAlarms` | `true` | Monitor health, food, and workers |

### Cross-save default profile

The current configuration can be saved as a default profile under **OPTIONS**
and imported into another save. The profile is separate from world files and
is stored at:

```text
%LOCALAPPDATA%\UNMA\profiles\default.json
```

Only when this file is genuinely absent does UNMA create and persist the
built-in **UNMA Recommended Quiet** profile. Exactly recognized, unchanged
earlier built-ins – **UNMA Recommended Silent** with six Silent rules and the
intermediate Quiet profile with two additional Hidden rules – are upgraded to
the current Quiet profile in memory only; their seed files remain unchanged.
Divergent and custom profiles are neither supplemented nor overwritten. The
built-in profile is not imported into a save automatically: its preview must
still be inspected and the import explicitly confirmed.

The recommended profile sets only these global notification types to
**SILENT** or **LOG · SOUND OFF**:

- `UpgradeInProgress`;
- `DowngradeInProgress`;
- `VehicleGoalStruggling`;
- `VehicleNoReachableDesignations`;
- `NoTreesToHarvest`;
- `ExcavatorHasNoValidTruck`.

**SILENT** disables only UNMA's sound for these notifications. The original
Captain of Industry notification remains unchanged, and UNMA continues to
show and record it in HOME and history.

The profile additionally sets these notification types to **IGNORED** or
**DO NOT LOG · IGNORE COMPLETELY**:

- `TruckCannotDeliver`;
- `TruckCannotDeliverMixedCargo`.

CoI frequently withdraws and re-emits these vehicle notifications with a new,
transient `NotificationId`. **IGNORED** discards each new UNMA event before
`SetAlarm`, history creation, and persistence, preventing the flicker from
continually increasing Incident Lens, history, and save-processing load. A
confirmed import and configuration normalization remove matching active states
and memories. They also remove older global history entries when no more
specific non-ignored entity or prototype rule must preserve them. The original
Captain of Industry notification remains visible and unchanged.
`CannotDeliverFromMineTower`, `VehicleGoalUnreachable`, and `VehicleNoFuel` are
deliberately excluded and remain normal and audible.

The following categories can be selected independently when saving and
importing:

- notification rules, including sound assignments and automatic
  acknowledgement;
- system-alarm configuration;
- alarm colors and UI scale;
- window positions and sizes; this category is unselected by default.

The startup options in `config.json`, including global audio enablement and
volume, already apply independently of a save and are not duplicated in the
profile.

In the source save, select the required categories and save the default
profile. In the target save, open the import and inspect its preview. It
classifies values as new, changed, unchanged, or skipped. Only confirmation
atomically merges the selected values into the target configuration. Matching
keys take the profile value, while other target values and unselected
categories remain intact. If validation or the initial atomic configuration
write fails, the complete target configuration remains unchanged. A rare
failure while subsequently saving the reconciled live alarm state is reported
as a partial failure; settings already imported successfully remain in place.

A Vanilla rule is portable when it is stored by stable notification type
(`NotificationType`) or by notification type plus entity prototype. Rules for
one exact entity ID belong to one world and are safely skipped; both the
preview and final result report them. For example, an `UpgradeInProgress` rule
for every object of a conveyor prototype can remain completely ignored by UNMA
without accidentally targeting an unrelated entity with the same numeric ID
in the new save.

History, active alarms, acknowledgements, running delays, escalations, snooze
states, and all other timing memories are never written to or imported from
the profile. A transferred ignore rule affects UNMA only. It neither disables
nor changes the original Captain of Industry notification. UNMA writes the
profile atomically through a temporary file and backup.

## Provider-mod alarms

Active mods can extend UNMA with alarm definitions, entity metrics, templates,
and directly published alarm states. Depending on the provider, one template
may create an aggregate slot or one stable slot per matching entity.

Provider failures are isolated so one malformed extension cannot prevent other
providers or UNMA from loading. User-visible provider text uses the active
MultiLangLib language where the provider supplies translations.

The programming interface is documented in the
[external mod API guide](https://github.com/max2605/UNMA/blob/main/docs/external-mod-api.md).

## Optional external display data

UNMA writes fault-tolerant local JSON data for optional companion displays:

```text
%LOCALAPPDATA%/UNMA/notifications.jsonl
%LOCALAPPDATA%/UNMA/panels.json
```

The first file contains alarm transitions. The second contains the current
panel and slot state. File-system failures are logged but do not interrupt the
game simulation. UNMA does not require an external display to operate.

## Saved data and removal

UNMA stores world-specific data in
`Mods/UNMA/unma-world-<GameId>.json`. These files are not part of a release
archive. The following survive saving and reloading:

- panel definitions, slot order, operational areas, and area assignments;
- custom rules and linked panels;
- acknowledged active alarms;
- cleared but unacknowledged alarms;
- completed history events;
- instrument panels, instruments, sources, calculations, and display scales;
- customized system-alarm stages plus Vanilla behavior and sound overrides;
- content scale, launcher position, and main-window/editor sizes.

Schema 20 migrates configurations from earlier UNMA versions with every
existing panel unassigned. Their previous **ALL** board behavior therefore
remains unchanged until areas are deliberately created and assigned.
The Incident Lens stores no configuration or result of its own. The separate
default profile does not extend a world file either, so 0.10.1 remains on
schema 20. If a configuration from a newer UNMA schema is found,
this version leaves the main file and its backup artifacts byte-for-byte
untouched, uses safe defaults, and blocks configuration writes for the session
instead of discarding unknown future fields.

If a configuration file is damaged, UNMA creates a backup and replaces it with
safe defaults.

Captain of Industry may separately retain the positions of movable native
windows through its own window system. Back up the entire `Mods/UNMA` folder
before uninstalling if you may want to restore your UNMA setup later.

UNMA can be removed from an existing save because it does not add physical game
entities. Deleting the mod folder also deletes UNMA's world files, so retain the
backup if you may reinstall later. Remove the mod only while the game is closed.

## Instrument panel: monitoring multiple storages

1. Open the first storage inspector in the game.
2. Open UNMA with **F8** and select **INSTRUMENTS**.
3. Choose **SOURCE FROM OPEN BUILDING**.
4. Select a metric such as **Fill level**, enter the `FROM` and `TO` scale
   values, open **TYPE**, and choose an instrument from the scrollable preview
   gallery.
5. Install the instrument and repeat for the other coal storages.

The small arrow opens the first source and the **X** removes only the
instrument. If an open draft or saved alarm depends on it, UNMA asks you to
finish or delete that alarm first. **ALARM** creates an alarm rule for that
instrument, including a calculated value. The editor exposes it as
**LINKED VALUES: label** and allows other values from the same instrument panel
to be added as conditions.
**INSTRUMENT** is the permanent third source button beside game selection and
global variables. Every instrument alarm independently selects one or more
destination panels, and at least one destination must remain selected.
**HIST** opens the same large historian for every instrument type. It offers
one game day, one game month, one game year, ten years, one century, or all
retained samples, and applies the selected range to both chart and analysis.
The footer shows current, minimum, average, maximum, linear rate per game
month, and R-squared. A reliable rising or falling trend adds a directed ETA
to the configured scale maximum or minimum; insufficient, stable, unreliable,
and beyond-100-years results are stated explicitly.

Instrument definitions and panel layouts are saved per world. Historian samples
exist only for the current running session and begin again after reloading the
world.

Use **+ OPEN BUILDING WITH SAME METRIC** to add more open buildings that expose
the same metric.
One instrument can display a single value, sum, average, minimum, or maximum.
Its alarm supports **VALUE**, **DECREASE**, **INCREASE**, and **SUSTAIN**.
Changes can use an absolute amount or percentage. SUSTAIN requires the chosen
operator and target value to remain true for the entire interval. Intervals
use game days, months, years, decades, or centuries and follow pause and game
speed. One failed comparison restarts a sustained interval.

Edgewise meters remain ideal for dense rows; CRTs and paper recorders also
show a continuously connected short-term trace. Additional named instrument
panels can be created with **+ PANEL**. Removing an instrument panel moves its
instruments to a remaining panel instead of deleting them. The type selector
opens a scrollable preview gallery.

## Troubleshooting

- **UNMA does not load:** Confirm that both UNMA and MultiLangLib are installed,
  enabled, and compatible with Captain of Industry 0.8.6c. Also verify that the
  path is `Mods/UNMA/manifest.json`, not `Mods/UNMA/UNMA/manifest.json`.
- **The launcher is missing:** Press **F8**. The launcher is hidden while the
  main window is open and while a game menu suppresses gameplay UI.
- **A game window covers UNMA:** This is normal native window ordering. Click
  the desired UNMA window to bring its complete frame and content forward, or
  pin it with the standard COI control.
- **Only UNMA content remains above another window while its frame is behind:**
  Version 0.9.18 removed that legacy split-layer behavior. Close the game,
  verify that only one `Mods/UNMA` installation exists, update the DLL, and
  restart.
- **The bottom of OPTIONS is not visible:** Scroll while the pointer is over the
  page, enlarge the window, or temporarily reduce the content scale.
- **An alarm is missing from HOME:** HOME shows only currently active alarms.
  Also check whether the notification is hidden or completely ignored in
  **NOTIFICATION OPTIONS**.
- **A known Vanilla notification is not listed:** Some types are discovered at
  runtime. Open the relevant object panel; registered potential notifications
  are displayed there before they become active when the game exposes them.
- **A product metric is missing:** Let the object handle that product once, then
  reopen or refresh its source selection.
- **A condition shows a missing source:** The object may have been removed, its
  prototype may have changed, or a provider metric may no longer be available.
- **A percentage rule never triggers:** Verify that its reference value exists
  and is greater than zero.
- **A custom sound is missing:** Check the file format and location, use the
  re-read action in **OPTIONS**, and restart the game if it is still absent.
- **A draft was not replaced:** This is intentional. Save, discard, or empty the
  existing draft before opening another rule.

## Current limitations

- Logistics zones, designations, and abstract routes are not selectable game
  entities and therefore cannot be used directly as object sources.
- A completely unused empty multi-product object may not expose individual
  products until one has been present.
- Transport capacity describes current content space, not throughput per time.
- Detached panels remain inside the game UI. Operating-system windows on other
  monitors require a separate companion application.

## Links

- [UNMA releases](https://github.com/max2605/UNMA/releases)
- [External mod API](https://github.com/max2605/UNMA/blob/main/docs/external-mod-api.md)
- [Provider integration quick start](https://github.com/max2605/UNMA/blob/main/docs/provider-integration.de.md)
