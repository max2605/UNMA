UNMA – Universal Notification and Monitoring Annunciator

Version: 0.10.1
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

Version 0.10.1 adds a selectable cross-save default profile under OPTIONS.
Preview and atomically merge portable notification and prototype rules,
system-alarm settings, alarm colors/UI scale, and optionally window layout.
World-bound entity rules, history, active alarms, and timing memories are
never transferred. The profile is stored at:
%LOCALAPPDATA%\UNMA\profiles\default.json

Documentation: README.md, USER_GUIDE_EN.md, USER_GUIDE_DE.md
Source and issues: https://github.com/max2605/UNMA
