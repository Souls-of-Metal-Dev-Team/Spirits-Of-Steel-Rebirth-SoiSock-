---
title: Runtime architecture
nav_group: Development
nav_order: 2
---

## Entry point and scenes

`project.godot` sets `run/main_scene` by UID to `Scenes/main.tscn`. [`Scenes/Main.cs`]({% link development/getting-started.md %}) starts in the menu and drains pending .NET finalizers in `_ExitTree`; finalizers left for engine teardown can crash on exit with SIGSEGV. Prefer `using` for large parsed data.

Global state lives in autoloads registered in `project.godot`. Use each autoload's static `Instance`, such as `MapManager.Instance`, which looks the node up through `Scripts/Autoload.cs`. The GDScript console addon is wrapped by `GameConsole` in the same file. Autoloads are ordinary nodes under `/root`, not Engine singletons.

Name mismatches worth remembering:

| Autoload | Source |
| --- | --- |
| `SceneSwitcher` | `Managers/SceneManager.cs` |
| `MapData` | `Scripts/Map/MapData.cs` |
| `Console` | Console addon |

`SceneSwitcher` switches between menu, world, and editor scenes behind a loading screen and caches the world scene. `GameState` holds session state: current start, interaction mode and UI flags, pending save load, clock cache, and `current_world`.

## Map and clock

`Managers/MapManager.cs` is the core map state. It owns the province ID image, province/country lookups (`country_to_provinces`, owned, occupied, rightful), province objects, the `SoiStar` pathfinding graph, and the state-color texture consumed by `shaders/map_shader.gdshader`.

`Scripts/GameClock.cs` emits `HourPassed` and `DayPassed`. `Scripts/Map/World.cs` connects those signals to `CountryManager` and `EventManager`; UI scripts connect to the clock on their own. `CountryManager` then processes countries, runs `EconomyManager.ProcessEconomyDay()`, and checks super events.

## UI and data classes

`Scripts/UI/` contains UI controllers; `GameUI.cs` is the main in-game HUD. Scene `[connection]` entries and `[Export]` node paths bind to UI members by name. Renaming a UI method or exported field requires updating the matching `.tscn`.

`Scripts/Map/` holds data classes such as `CountryData`, `Province`, `BuildingData`, and `ResourceData`, mostly with `FromDict`/`ToDict` serialization. Domain code also lives in `Scripts/Diplomacy/`, `Scripts/Divisions/`, and `Scripts/Events/`.

## Conventions

New code uses CamelCase, but much ported code still uses snake_case. Match the file being edited. Author notes use `// NOTE(soi):` and `// TODO(pol):`.

Historical dependency graphs are in [Node dependencies]({% link architecture/node-dependencies.md %}). They were generated before the C# rewrite and still contain old `.gd` names.
