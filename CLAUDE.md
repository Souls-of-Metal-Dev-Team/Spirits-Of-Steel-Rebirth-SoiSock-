# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Spirits of Steel Rebirth is a grand-strategy / RTS game built in **Godot 4.7** (GL Compatibility renderer). All game code is C# (`namespace Sosr`) in a .NET project (`Spirits of Steel Rebirth.csproj`, `net8.0`), so it needs the **mono/.NET build of Godot**. GDScript remains only in `addons/` and the `scratch/` test harnesses.

## Commands

There is no linter. Unit tests cover only the engine-free `Core/` library.

- Open the editor: `godot-mono --editor --path .`
- Run the game: `godot-mono --path .` (the main scene is set in `project.godot`)
- Build the C# assembly: `dotnet build` (the Godot editor also does this)
- Run unit tests: `dotnet test Tests/Sosr.Tests.csproj` (xUnit, no Godot needed)
- Smoke-test the game headless: `godot-mono --headless --path . --quit-after 300` (a non-zero exit, e.g. 139, is a crash).
- Export targets are in `export_presets.cfg`.

`.uid` files sit next to every script and shader. Commit them together with their source files, because scenes and `project.godot` reference many resources by `uid://` instead of by path.

## Architecture

### Autoload singletons (`Managers/`)
Global state lives in autoloads registered in `project.godot` (`MapManager`, `CountryManager`, `GameState`, `WarManager`, `TroopManager`, `DecisionManager`, `EconomyManager`, `EventManager`, `InterpreterManager`, `FactionManager`, `IdeologyManager`, `PlansManager`, etc.). Reach them with the typed `Instance` accessor each one has (`MapManager.Instance`, `CountryManager.Instance.Player`), which looks the node up through `Scripts/Autoload.cs`. The GDScript console addon is wrapped by `GameConsole` in the same file. Some names don't match their files:
- `SceneSwitcher` → `Managers/SceneManager.cs`. It switches between the MENU, WORLD and EDITOR scenes behind a loading screen and keeps the world scene cached.
- `GameState` is a grab-bag of session flags: the current start, the selected country, UI modes, pending save loads, and a reference to `current_world`.

`MapManager.cs` is the core. It holds the province ID map image, the province → country lookups (`country_to_provinces`, owned, occupied, rightful), the `Province` objects, the province graph (`SoiStar`, used for pathfinding), and the state-color texture that the map shader reads.

### Game loop
`Scripts/GameClock.cs` (`$/root/Main/Clock`) emits `HourPassed` and `DayPassed`. `World.DoSetup` (`Scripts/Map/World.cs`) connects these to `CountryManager._on_hour_passed` and `_on_day_passed`, and to `EventManager._on_day_passed`. CountryManager then calls `ProcessHour()` / `ProcessDay()` on each `CountryData`, runs `EconomyManager.ProcessEconomyDay()`, and checks for super events. UI scripts connect to the clock on their own.

### Scenarios ("starts") and saves
Each folder in `starts/<Name>/` is a playable scenario:
- `map_data.json`: biomes, resources, buildings, ideologies, wars and the clock
- `provinces.json`: the provinces, keyed by province ID
- `scenario_data.json`: polities, factions and significant figures
- `regions.png`: the province ID map
- `decisions/`: `default.json` plus optional `<CountryName>.json` overrides. If a start has no `default.json`, the root `decisions/default.json` is used.
- `superevents.json`, `building_functions.json`, and an optional `flags/` folder

`World.DoSetup` / `MapData.LoadMapData` loads these three files from `res://starts/<GameState.current_start>/`. If `GameState.pending_load_save` is set, it loads `user://saves/<name>.json` instead; a save has the same shape plus `scenario_path`.

### Data-driven scripting
Decisions and super events are written as JSON expression trees of the form `{"func": "<name>", "args": [...]}`. `InterpreterManager` evaluates them. To add a new scriptable function:
1. Add its argument types to `InterpreterManager.functions`.
2. Add its parameter names to `param_names` (the in-game decision editor shows these).
3. Handle it in `get_function`.

Building recipes in `building_functions.json` work differently. Their values are Godot `Expression` strings (e.g. `has_tag('fuel')`, `input_amount(0)`), which `Scripts/Map/TagMatcher.cs` evaluates.

`Docs/ImportantFigureSkillsReference.md` lists the skill modifiers that significant figures can have (additive vs. multiplicative).

### Code layout
- `Core/`: `Sosr.Core`, a plain .NET library with no Godot reference, so `Tests/` can run it headless. State there is a plain tree (`Dictionary<string, object?>`, `List<object?>`, `double`, `string`, `bool`, `null`; see `StateJson`). `Scripts/VariantState.cs` converts it to and from Godot Variants. The Godot `.csproj` excludes `Core/**` and `Tests/**` from its own compile glob
- `Scripts/Map/`: data classes (`CountryData`, `Province`, `BuildingData`, `ResourceData`, ...), mostly with `FromDict`/`ToDict` serialization
- `Scripts/Diplomacy/`, `Scripts/Divisions/`, `Scripts/Events/`: domain data classes and popups
- `Scripts/UI/`: UI controllers; `GameUI.cs` is the main in-game HUD. Scene `[connection]`s and `[Export]` node paths bind to UI members by name, so renaming a UI method or exported field means updating the `.tscn` too
- `Scripts/UI/MapEditor/`: the in-game map/scenario editor
- `shaders/map_shader.gdshader`: renders the map from the region ID texture and the state-color lookup texture

## Conventions

- New code uses CamelCase for function names (recent commits are migrating to it), but much of the existing code, including most ported C#, still uses the original snake_case names. Match the file you're editing.
- `Main._ExitTree` drains pending .NET finalizers. Finalizers left to run during engine teardown crash the game on exit (SIGSEGV on the `.NET Finalizer` thread). Dispose large parsed data with `using` instead of leaving it to the GC.
- Author notes are written as `// NOTE(soi):` / `// TODO(pol):`.
