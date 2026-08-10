# UNMA v0.9.18 – Fully Native COI UI and Updated User Guides

UNMA v0.9.18 moves every visible UNMA surface into Captain of Industry's native UI hierarchy. Window frames and their contents now follow the same game-controlled z-order, eliminating the old split-layer overlap with vanilla game windows while preserving movable, pinnable, and resizable monitoring panels.

## What changed in v0.9.18

- The launcher, main window, alarm editor, detached panels, forms, and instrument rendering now all live in Captain of Industry's native UI Toolkit hierarchy.
- Frames and content move forward and backward together in the game's normal window order. Vanilla windows can cover UNMA normally, and clicking a UNMA window brings the complete window to the front.
- The legacy IMGUI overlay and transparent uGUI pointer shield have been removed.
- Pointer handling, text-field focus, content scaling, and dynamic-list interactions are now consistent across multiple open UNMA windows.
- The **OPTIONS** page is vertically scrollable, keeping every setting accessible in smaller windows and at larger content scales.

## What UNMA already offers

- Configurable annunciator panels with acknowledgement, alarm sounds, links to game objects, and explicit single- or multi-panel destinations.
- Calculated instruments that combine matching values from multiple buildings using sum, average, minimum, or maximum.
- Industrial gauges, edgewise meters, seven-segment displays, Nixie tubes, CRTs, and paper recorders with archives from one in-game day to one century.
- Alarm conditions for values, percentages, increases, decreases, and sustained states over game time, including linked instrument values.
- Global metrics for population, health, workers, food, maintenance, and stored products, plus complete English and German localization through MultiLangLib.

## Updated user guides

Both user guides have been revised for v0.9.18. They now cover safe installation and updates, the native launcher and window behavior, annunciator panels, the alarm editor, instruments and recorder archives, the scrollable Options page, integrations, saved data, and troubleshooting.

- [English User Guide](https://coigame.com/Topic/1926/User-Guide)
- [Deutsche Benutzeranleitung](https://coigame.com/Topic/1927/Benutzeranleitung)

## Compatibility

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.18**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Can be added to or removed from existing saves.

## Download and safe update

Download the current package from the [UNMA releases page](https://github.com/max2605/UNMA/releases).

Before updating, close the game and back up the existing `Mods/UNMA` folder. World data (`unma-world-<GameId>.json` and its backup files) and user-added sounds are stored inside that folder, but they are not included in the release ZIP.

Extract the new `UNMA` folder over the existing folder so those additional files remain. If you replace the folder completely, restore the world-data files and custom sounds from your backup afterward. Do not delete the old folder first without making a backup.

Thank you for testing UNMA and for the detailed UI feedback. If you encounter a layering, input, scaling, or migration issue, please include your game version, UNMA version, and the relevant log or screenshot in your report.

Keep the alarms loud, the panels readable, and the coal moving.
