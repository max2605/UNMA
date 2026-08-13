UNMA – Universal Notification and Monitoring Annunciator

Version: 0.10.3
Development snapshot: includes unreleased changes after 0.10.3
Captain of Industry: 0.8.6c
Required dependency: MultiLangLib 0.1.0 or newer
Optional dependency: Keybind Framework 2.0.2 or newer

Installation
1. Close Captain of Industry.
2. Extract this archive directly into:
   %APPDATA%\Captain of Industry\Mods
3. Verify that the resulting path is:
   %APPDATA%\Captain of Industry\Mods\UNMA\manifest.json
4. Install and enable MultiLangLib, then enable UNMA for the desired save.

UNMA can be added to and removed from existing saves. World-specific settings
are stored separately and are not included in this release archive.

Version 0.10.3 improves input contrast and restores reliable title-bar
dragging. Every editable text, search, color, numeric, and timing field now
uses a dark-green background with bold white text. Main, editor, and detached
windows follow the pointer smoothly and retain their settled position, while
viewport clamping still responds to real resolution and UI-scale changes.
Acknowledged active alarm occurrences remain visibly marked S / SILENT and
quiet until they end. A silent month-start popup groups occurrences that have
stayed operator-acknowledged for at least one full game month; deliberate
notification-option silence and soundless rules are excluded.

NotEnoughPowerForEntity is one important group episode. The 0 → 1 transition
raises it once. Additional affected buildings update only its ×N count without
another sound, acknowledgement reset, or history entry. Only 1 → 0 closes the
group; the next 0 → 1 starts a new audible occurrence. The group follows its
notification-type Normal, Silent, Hidden, or Ignored rule. Existing entity and
prototype rules remain saved but deliberately dormant for this grouped type.
An operator S remains separate from configured Silent and becomes eligible for
the silent month-start reminder only after one full game month.

The built-in UNMA Recommended Quiet profile now globally sets
NotEnoughFuelToRefuel to Ignored. VehicleNoFuel, NotEnoughPower, and
NotEnoughPowerForEntity deliberately receive no recommended rule and remain
normal, audible, important notifications. NotEnoughPowerForEntity is neither
Recommended Quiet nor Ignored; grouping removes repetition spam without
suppressing the power alarm. The profile change affects a world only after its
preview is inspected and the import explicitly confirmed; the original Captain
of Industry notification remains unchanged.

Only exactly recognized, unchanged built-ins are refreshed in memory: the
six-rule Recommended Silent 0.10.1/0.10.2 baseline, the intermediate 0.10.2
Quiet baseline with two Hidden rules, and the previous 0.10.1/0.10.2/0.10.3
Quiet baseline with two Ignored rules. Their files, divergent profiles, and
custom profiles remain untouched.

Documentation: README.md, USER_GUIDE_EN.md, USER_GUIDE_DE.md
Source and issues: https://github.com/max2605/UNMA
