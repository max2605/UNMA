# UNMA v0.9.17 – Native COI UI, Instrument Panels, and Advanced Alarms

UNMA has grown from a configurable annunciator board into a complete industrial monitoring and alarm system for Captain of Industry.

## What is new

- A native Captain of Industry window style with movable and resizable windows, proper input handling, and immersive COI controls.
- 1970s power-plant instrumentation: edgewise meters, round gauges, seven-segment displays, Nixie tubes, CRTs, and paper recorders.
- Multiple named instrument panels for organizing large factories.
- Calculated instruments that combine the same value from multiple buildings using sum, average, minimum, or maximum.
- Paper-recorder archives ranging from one in-game day to a full century.
- Alarm conditions for values, increases, decreases, percentages, and sustained states over in-game time.
- Global variables for population, health, workers, food, maintenance, and stored products.
- A dedicated **INSTRUMENT** source in the alarm editor.
- Explicit single- or multi-panel destinations for every instrument alarm. New alarms are never silently assigned to the Supply panel.
- Full MultiLangLib integration with complete English and German text plus safe English fallbacks for all supported locales.

## Compatibility

- Captain of Industry: **0.8.6c**
- UNMA: **0.9.17**
- Required dependency: **MultiLangLib 0.1.0 or newer**
- Can be added to or removed from existing saves.

## Updating

Close the game, remove the previous `UNMA` mod directory, and extract the new ZIP directly into:

`%APPDATA%\Captain of Industry\Mods`

The archive already contains the required `UNMA` root directory. Existing world-specific panels, alarm rules, histories, and instrument configurations are retained outside the release package.

Thank you for testing UNMA and for the detailed UI feedback. Keep the alarms loud, the panels readable, and the coal moving.
