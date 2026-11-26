---
title: State diff format
nav_group: Architecture
nav_order: 1
---

`World.SaveGame` writes `scenario_path` plus a `diff` against that start. `MapData.LoadSaveState` merges the start state with the diff. Saves without a `diff` are treated as legacy full-state saves.

## Entry shapes

`StateDiff.Diff` emits one entry per changed key:

| Change | Entry |
| --- | --- |
| Add key | `{"type": "add", "value": <new value>}` |
| Remove key | `{"type": "remove", "value": <old value>}` |
| Change scalar | `{"type": "change", "value": <new value>}` |
| Change dictionary | nested diff object |
| Change array | `{"type": "change", "add": [...], "remove": [...]}` |

Array diffs store values, not indices. `StateDiff.Merge` removes the listed values from the source array, then appends additions.

Both start and current state are normalized through JSON before diffing. This keeps live `int` values from appearing changed against `float` values parsed from JSON.
