---
title: Scenarios and saves
nav_group: Development
nav_order: 3
---

## Scenario layout

Playable starts live in `starts/<Name>/`. The current `Scripts/Map/MapData.cs` loader expects:

| File | Contents |
| --- | --- |
| `map_data.json` | resources, biomes, buildings, ideologies, clock, wars, original territories |
| `provinces.json` | province ownership and province-level state |
| `scenario_data.json` | polities, factions, significant figures |
| `regions.png` | province ID map |
| `building_functions.json` | building recipe expressions |
| `superevents.json` | scheduled super event definitions |
| `decisions/` | decision trees |
| `flags/` | optional country flag overrides |

Only `ModernDay` currently has all three state JSON files. Older starts have a single monolithic `map_data.json` and need porting before `MapData.LoadMapData` can load them correctly.

`World.DoSetup` loads `res://starts/<GameState.current_start>/`. A non-empty `GameState.pending_load_save` loads `res://saves/<name>.json` instead.

## Decisions

`DecisionManager` loads `decisions/default.json` from the start. If that file is missing, it falls back to root `decisions/default.json`. Other JSON files in the start's `decisions/` directory are country overrides.

## Saves

New saves contain only changes from the start:

```json
{
  "scenario_path": "res://starts/ModernDay/map_data.json",
  "diff": {}
}
```

`MapData.LoadSaveState` merges the start state with `diff`. Legacy full-state saves are accepted as-is. `World.SaveGame` normalizes both start and current state through JSON before diffing so live `int` values do not look changed against parsed JSON `float` values. See [State diff format]({% link architecture/state-diff.md %}) for entry shapes.
