# UNMA User Guide

This guide applies to **UNMA 0.9.18** and **Captain of Industry 0.8.6c**.

UNMA (Universal Alarm Annunciator) adds a configurable industrial annunciator
to Captain of Industry. It mirrors game notifications, keeps a persistent alarm
history, monitors important settlement values, and lets you build custom alarm
rules for game objects or global game variables.

Required dependency: **MultiLangLib 0.1.0 or newer**.

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
3. Open **ANNUNCIATOR** to see HOME and your permanent panels.
4. Press **MASTER QUIT / ACKNOWLEDGE** to acknowledge all new and
   cleared-but-unacknowledged alarms and silence their sounds.
5. Open **HISTORY** to inspect previous alarm events.

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
| **OPTIONS** | Content scale, alarm colors, sound rescan, and integration diagnostics |

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

## Panels and alarm slots

### HOME

HOME is a live overview of alarms that are currently active. It shows `K` and
`KQ` alarms from all sources but does not own permanent slots. Inactive,
cleared, and empty slots are hidden from HOME.

### Global panels

Global panels are permanent annunciator boards that can collect game,
system, provider, and custom alarms.

- Create a panel with **+ PANEL** beside the global-panel tabs in
  **ANNUNCIATOR**.
- Use the adjacent gear button to change its name, column count, filters,
  automatic sources, and slot order.
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
Object-bound notifications can be configured for one exact object or for every
object of the same prototype.

| Mode | UNMA sound | HOME / counters | History |
| --- | --- | --- | --- |
| **NORMAL** | Enabled | Visible | Stored |
| **LOG, SOUND OFF** | Disabled | Visible | Stored |
| **LOG, SOUND OFF, HIDE** | Disabled | Hidden | Stored |
| **DO NOT LOG / IGNORE COMPLETELY** | Disabled | Hidden | Not created |

Object-specific rules override prototype rules. Completely ignoring a type
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
Its stages expose measured value, operator, threshold, severity, color, and
sound. Restoring factory defaults requires a second press for confirmation.

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
  own UI scale.
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

- panel definitions and slot order;
- custom rules and linked panels;
- acknowledged active alarms;
- cleared but unacknowledged alarms;
- completed history events;
- instrument panels, instruments, sources, calculations, and display scales;
- customized system-alarm stages plus Vanilla behavior and sound overrides;
- content scale, launcher position, and main-window/editor sizes.

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
destination panels, and at least one destination must remain selected. A paper
recorder's **ARCHIVE** action opens a large history
view for one game day, one game month, one game year, ten years, one century,
or all retained samples. The recorder
advances from left to right without compressing every new value into the
existing picture.

Instrument definitions and panel layouts are saved per world. Recorder samples
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
