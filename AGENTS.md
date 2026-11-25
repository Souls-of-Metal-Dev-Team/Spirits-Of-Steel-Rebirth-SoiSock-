# AGENTS.md

Compact orientation for this repository. `CLAUDE.md` has broader architecture notes; if prose conflicts with `project.godot`, scripts, or C# sources, trust the executable source.

## Toolchain and commands

- Godot **4.7.2 mono/.NET** with the GL Compatibility renderer. Game code is C# in `namespace Sosr`; GDScript remains only in `addons/` and `scratch/`. The `.csproj` `RootNamespace` differs; new game code uses `Sosr`.
- Editor: `godot-mono --editor --path .`
- Run: `godot-mono --path .`
- Build: `dotnet build`
- Headless smoke test: `godot-mono --headless --path . --quit-after 300`. A non-zero exit (e.g. 139) is a crash.
- Scratch checks:
  - `godot-mono --headless --path . --script res://scratch/test_port_parity.gd`
  - `godot-mono --headless --path . res://scratch/test_save_diff_flow.tscn`
  - `godot-mono --headless --path . res://scratch/test_settings_manager_diff.tscn`
- `--script` harnesses cannot see autoloads; autoload-dependent checks use the matching `.tscn`.
- There is no code CI, linter, formatter, or formal test suite beyond Godot and those scratch checks. `.github/workflows/docs.yml` only publishes `docs/` to GitHub Pages. Export targets are in `export_presets.cfg`; the Windows preset still points at 4.6.1 templates in a personal path.
- Commit `.uid` files alongside their scripts/shaders. Scenes and `project.godot` reference many resources by `uid://`.

## Runtime wiring

- `Scenes/Main.cs` starts in the menu and its `_ExitTree` drains pending .NET finalizers. Leaving finalizers to engine teardown can SIGSEGV on exit; dispose large parsed data with `using`.
- Global state lives in autoloads registered in `project.godot`. Use each autoload's static `Instance` (looked up through `Scripts/Autoload.cs`); the GDScript console addon is wrapped by `GameConsole` in the same file. Autoloads are ordinary `/root` nodes, not Engine singletons.
- Name mismatches: `SceneSwitcher` is `Managers/SceneManager.cs` (it caches the world scene); `MapData` is `Scripts/Map/MapData.cs`. `GameState` is the session grab-bag: current start, interaction/selected-country state, pending save, clock cache, and `current_world`.
- `Managers/MapManager.cs` owns the province ID image, province/country lookups, province objects, the `SoiStar` pathfinding graph, and the state-color texture read by the map shader.
- `Scripts/GameClock.cs` emits `HourPassed`/`DayPassed`. `Scripts/Map/World.cs` connects them to `CountryManager` and `EventManager`; UI scripts connect to the clock independently.

## Scenarios, saves, and data-driven logic

- Starts live in `starts/<Name>/`. The current `MapData.LoadMapData` expects `map_data.json`, `provinces.json`, `scenario_data.json`, `regions.png`, `decisions/`, `building_functions.json`, and `superevents.json`. Only `ModernDay` currently has all three state JSON files; the other starts lack `provinces.json` and `scenario_data.json`.
- `World.DoSetup` loads the current start; a non-empty `GameState.pending_load_save` loads `res://saves/<name>.json` instead.
- Decisions use the start's `decisions/default.json` or fall back to root `decisions/default.json`; other JSON files in that directory are country overrides.
- Saves are diffs (`scenario_path` + `diff`) merged with their start by `MapData.LoadSaveState`; legacy full saves are still accepted. `World.SaveGame` JSON-normalizes both sides before diffing so int-vs-float changes do not appear.
- Decisions and super events are JSON trees such as `{"func": "name", "args": [...]}`. To add a function, update `InterpreterManager.functions`, `param_names`, and `get_function`.
- Building recipes in `building_functions.json` are Godot `Expression` strings evaluated by `Scripts/Map/TagMatcher.cs`, not the decision interpreter.
- Significant-figure skill modifiers are documented in `docs/reference/important-figure-skills.md`.
- UI controllers bind to `.tscn` nodes and signals by exported field/method names; rename both sides together.
- New code uses CamelCase, but much ported code uses snake_case; match the file being edited. Author notes use `// NOTE(soi):` and `// TODO(pol):`.
