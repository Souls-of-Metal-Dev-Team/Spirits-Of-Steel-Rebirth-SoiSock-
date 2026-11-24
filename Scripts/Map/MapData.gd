@tool
extends Resource
class_name MapData

# NOTE(soi): 0 clue what we could even need these for
@export var id_map_image: Image
@export var adjacency_list: Dictionary
@export var province_centers: Dictionary

@export var province_to_country: Dictionary
@export var country_to_provinces: Dictionary
@export var max_province_id: int
@export var province_objects: Dictionary

# Keys shared by a scenario's start state and a unified game-state save.
const STATE_KEYS: Array[String] = [
	"clock", "resources", "biomes", "buildings", "provinces",
	"polities", "ideologies", "factions", "wars",
	"original_territories", "scheduled_events",
]

static func _LoadJSON(path: String):
	return JSON.parse_string(
		FileAccess.open(
			path,
			FileAccess.READ,
		).get_as_text(),
	)

static func _SaveJSON(path, filename, string):
	var file: FileAccess = FileAccess.open(path+filename, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(string , "\t"))
		file.close()
		print("Scenario exported to: ", path)
	else:
		push_error("Failed to export_mapdata scenario to: ", path)

# scenario_path may be a folder (res://starts/Name) or the map_data.json file.
static func ScenarioDirFromPath(a_scenario_path: String) -> String:
	if a_scenario_path.ends_with(".json"):
		return a_scenario_path.get_base_dir()
	return a_scenario_path.trim_suffix("/")

# Build the start state in the same shape as World.SaveGame's save_data.
static func LoadStartState(a_start_dir: String) -> Dictionary:
	var paths := {
		"map_data": "%s/map_data.json" % a_start_dir,
		"provinces": "%s/provinces.json" % a_start_dir,
		"scenario_data": "%s/scenario_data.json" % a_start_dir,
	}
	for key in paths:
		if not FileAccess.file_exists(paths[key]):
			push_error("MapData: Start file missing: " + paths[key])
			return {}

	var map_data: Dictionary = _LoadJSON(paths["map_data"])
	var provinces: Dictionary = _LoadJSON(paths["provinces"])
	var scenario_data: Dictionary = _LoadJSON(paths["scenario_data"])

	var combined: Dictionary = {}
	combined.merge(map_data, true)
	combined["provinces"] = provinces
	combined.merge(scenario_data, true)

	var state: Dictionary = {}
	for key in STATE_KEYS:
		if combined.has(key):
			state[key] = combined[key]
	return state

# Resolve a save file into a full game state. Diff saves are merged with their start.
static func LoadSaveState(a_save_path: String) -> Dictionary:
	var save_json: Variant = _LoadJSON(a_save_path)
	if not save_json is Dictionary:
		push_error("MapData: Failed to parse save: " + a_save_path)
		return {}

	var save_dict: Dictionary = save_json
	if not save_dict.has("diff"):
		# Legacy full-state save.
		return save_dict

	var scenario_path: String = save_dict.get("scenario_path", "")
	if scenario_path.is_empty():
		push_error("MapData: Diff save missing scenario_path: " + a_save_path)
		return {}

	var state_diff: Variant = save_dict.get("diff")
	if not state_diff is Dictionary:
		push_error("MapData: Invalid diff in save: " + a_save_path)
		return {}

	var start_state: Dictionary = LoadStartState(ScenarioDirFromPath(scenario_path))
	if start_state.is_empty():
		return {}

	var state: Dictionary = SettingsManager.merge(start_state, state_diff)
	state["scenario_path"] = scenario_path
	return state

static func SaveMapData(path: String) -> void:
	var fig_dicts: Array[Dictionary] = []
	for x: ImportantFigure in MapManager.significantFigures.values():
		fig_dicts.append(x.ToDict())
	var export_map_data = {
		"ideologies": IdeologyManager.ideologies,
		"buildings": MapManager.SaveBuildingData(),
		"resources": MapManager.SaveResourcesData(),
		"biomes": MapManager.SaveBiomeData(),
		"clock": GameState.current_world.clock.ToDict() if GameState.current_world.clock else {},
	}

	var export_scenario = {
		"significant_figures": fig_dicts,
		"polities": CountryManager.SaveCountries(),
		"factions": FactionManager.SaveFactions(),
	}

	var export_province = MapManager.SaveProvinces()

	_SaveJSON(path, "map_data.json",      export_map_data)
	_SaveJSON(path, "provinces.json",     export_province)
	_SaveJSON(path, "scenario_data.json", export_scenario)

static func LoadMapData(a_start_path: String, a_percent: Array = [0], a_state_override: Dictionary = {}):
	var map_data:      Dictionary = _LoadJSON("%s/map_data.json"%a_start_path)
	var provinces:     Dictionary = _LoadJSON("%s/provinces.json"%a_start_path)
	var scenario_data: Dictionary = _LoadJSON("%s/scenario_data.json"%a_start_path)

	# Loading a save: keep the start's static data (regions, decisions, flags, ...)
	# but replace every dynamic section with the saved state.
	if not a_state_override.is_empty():
		for key in ["biomes", "resources", "buildings", "ideologies", "clock", "wars", "original_territories"]:
			if a_state_override.has(key):
				map_data[key] = a_state_override[key]
		if a_state_override.has("provinces"):
			provinces = a_state_override["provinces"]
		if a_state_override.has("polities"):
			scenario_data["polities"] = a_state_override["polities"]
		if a_state_override.has("factions"):
			scenario_data["factions"] = a_state_override["factions"]

	# print(map_data)
	# print(provinces)
	# print(scenario_data)

	var regions_tex: Texture2D = load("%s/regions.png"%a_start_path)
	print("%s/regions.png"%a_start_path)

	# GameState.current_start = a_text
	a_percent[0] = 0.01
	EconomyManager.initialize(a_start_path + "/building_functions.json")
	a_percent[0] = 0.02
	PlansManager.load_plans_from_path(a_start_path + "plans.json")
	a_percent[0] = 0.06
	EventManager.load_super_events(a_start_path + "superevents.json")
	a_percent[0] = 0.18

	var b_data: Array = map_data.get("biomes", [])
	MapManager.LoadBiomes(b_data)
	
	var r_data: Array = map_data.get("resources", [])
	MapManager.LoadResources(r_data)
	
	var buildingData = map_data.get("buildings")
	EconomyManager.LoadBuildings(buildingData if buildingData is Dictionary else {})
	
	var ideologyData = map_data.get("ideologies", {})
	IdeologyManager.Initialize(ideologyData)
	a_percent[0] = 0.3
	
	
	DecisionManager.load_decisions_from_path("%s/decisions/"%a_start_path)
	a_percent[0] = 0.2
	TroopManager.set_custom_flag_path("%s/flags/"%a_start_path)
	a_percent[0] = 0.12
	CountryManager.initialize_countries(scenario_data.get("polities", []) as Array)
	a_percent[0] = 0.75
	MapManager.LoadProvinces(
		regions_tex,
		provinces,
		a_percent,
	)
	a_percent[0] = 0.6
	
	# Restore Clock
	if GameState.current_world.clock:
		if map_data.has("clock"):
			GameState.current_world.clock.FromDict(map_data["clock"])
		else:
			# Default start date for scenarios/new games
			GameState.current_world.clock.FromDict({"hour": 0, "date": {"year": 2010, "month": 1, "day": 1}})
	
	WarManager.load_wars(map_data.get("wars", []))
	WarManager.load_original_territories(map_data.get("original_territories", {}))
	WarManager.check_for_new_battles()
	
	MapManager.BuildLookupTexture()
	a_percent[0] = 0.85
	FactionManager.Initialize(scenario_data.get("factions", []))
	a_percent[0] = 0.9
	
	for fig in scenario_data.get("significant_figures", []):
		MapManager.significantFigures[fig["name"]] = ImportantFigure.FromDict(fig)
	CountryManager.generate_missing_leaders()
	CountryManager.cleanup_empty_countries()

	if a_state_override.has("scheduled_events"):
		EventManager.load_events(a_state_override["scheduled_events"] as Array)

	Console.add_command_autocomplete_list("tag", CountryManager.countries.keys())
