extends Node2D
class_name World

@onready var map_sprite: Sprite2D = $"../../MapContainer/CultureSprite" as Sprite2D
@onready var map_container: MapContainer = $"../../MapContainer" as MapContainer

@export var map_shader: Shader
var clock

var water_offset: Vector2 = Vector2.ZERO

var type_img: Image

var mat: ShaderMaterial

func _process(_delta: float) -> void:
	if !map_sprite or !MapManager.id_map_image: return
	if map_sprite.material and clock and !clock.paused:
		var move_amount = clock.time_scale * 0.001 * _delta
		water_offset.x += move_amount
		map_sprite.material.set_shader_parameter("ocean_offset", water_offset)
		map_sprite.material.set_shader_parameter("absolute_days", clock.get_absolute_days())


func _enter_tree() -> void:
	GameState.current_world = self
	clock = $/root/Main/Clock
	clock.FromDict(GameState.clock_cache)
	KeyboardManager.settings = $/root/Main/SettingsLayer
	TroopManager.troop_selection = $TroopSelection as TroopSelection
	# TODO(pol): Load CountryManager after map instead of an autoload to avoid this.

func DoSetup(a_progress: Array) -> void:
	var path = "res://starts/%s" % GameState.current_start
	var save_state: Dictionary = {}

	if GameState.pending_load_save != "":
		var save_path = "res://saves/" + GameState.pending_load_save + ".json"
		if FileAccess.file_exists(save_path):
			save_state = MapData.LoadSaveState(save_path)
			if not save_state.is_empty():
				var scenario_path: String = save_state.get("scenario_path", "")
				if not scenario_path.is_empty():
					path = MapData.ScenarioDirFromPath(scenario_path)
					GameState.current_start = path.get_file()
				GameState.is_loading_game = true
				print("World: Pending save found. Loading from: ", GameState.pending_load_save)
			else:
				push_error("World: Failed to load save state from: " + save_path)

	a_progress[0] = 0.05

	# NOTE(soi): this is here bcuz sometimes main menu is used and im too lazy to comment this out
	if MapManager.province_objects.is_empty() or GameState.is_loading_game:
		MapData.LoadMapData(path, a_progress, save_state)
	
	if GameState.is_loading_game:
		GameState.is_loading_game = false
		GameState.pending_load_save = ""
	a_progress[0] = 0.2

	print("World: Map is ready -> configuring visuals...")

	MapManager.all_cities = MapManager.GetAllCities()
	a_progress[0] = 0.21

	if !CountryManager.player_country:
		# CountryManager.set_player_country(CountryManager.countries.keys().pick_random())
		GameState.interactionType = GameState.InteractionType.SELECT_COUNTRY
		# NOTE(soi): wait so like when i click on a country this should magically play as it?????. damm
	# For debugging purposes. Create some troops first
	a_progress[0] = 0.30
	MapManager._BuildGlobalRegistry()
	a_progress[0] = 0.35
	var map_width: int = MapManager.MAP_WIDTH
	var map_height: int = MapManager.MAP_HEIGHT

	mat = ShaderMaterial.new()
	mat.shader = map_shader
	mat.set_shader_parameter("region_id_map", ImageTexture.create_from_image(MapManager.id_map_image))
	mat.set_shader_parameter("state_colors", MapManager.state_color_texture)

	# @warning_ignore("narrowing_conversion")
	type_img = Image.create_empty(map_width, map_height, false, Image.FORMAT_L8)

	print(map_width, map_height)
	MapManager._SetTypeMap(mat, type_img)

	var noise = FastNoiseLite.new()
	noise.seed = randi()

	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH

	noise.frequency = 0.005

	noise.fractal_type = FastNoiseLite.FRACTAL_RIDGED
	noise.fractal_octaves = 5
	noise.fractal_lacunarity = 3.0
	noise.fractal_gain = 0.5

	var noise_tex = NoiseTexture2D.new()
	noise_tex.seamless = true
	noise_tex.width = 512
	noise_tex.height = 512
	noise_tex.noise = noise

	await noise_tex.changed
	mat.set_shader_parameter("ocean_noise", noise_tex)

	mat.set_shader_parameter("original_texture", map_sprite.texture)
	mat.set_shader_parameter("sea_speed", 0.00) # Changed by MainClock
	mat.set_shader_parameter("tex_size", Vector2(map_width, map_height))
	mat.set_shader_parameter("country_border_color", Color.BLACK)

	map_sprite.material = mat
	a_progress[0] = 0.9

	if map_container:
		map_container.map_width = map_width
	else:
		push_error("MapContainer node not found!")

	SettingsManager.apply_settings(SettingsManager.settings)

	a_progress[0] = 1.0
	
	clock.HourPassed.connect(CountryManager._on_hour_passed)
	clock.DayPassed.connect(CountryManager._on_day_passed)
	clock.DayPassed.connect(EventManager._on_day_passed)
	
	_refresh_map_visuals()

func _refresh_map_visuals() -> void:
	if mat and MapManager.state_color_texture:
		mat.set_shader_parameter("state_colors", MapManager.state_color_texture)


func SaveGame(slot: String):
	# Construct a unified save dictionary
	var save_data: Dictionary = {
		"clock": clock.ToDict() if clock else {},
		"resources": MapManager.SaveResourcesData(),
		"biomes": MapManager.SaveBiomeData(),
		"buildings": MapManager.SaveBuildingData(),
		"provinces": MapManager.SaveProvinces(),
		"polities": CountryManager.SaveCountries(),
		"ideologies": IdeologyManager.ideologies,
		"factions": FactionManager.SaveFactions(),
		"wars": WarManager.save_wars(),
		"original_territories": WarManager.save_original_territories(),
		"scheduled_events": EventManager.save_events()
	}
	
	# Store only what changed since the start; the start is loaded again on load.
	# Normalize both sides through JSON so ints in live state match floats parsed
	# from the start files. Otherwise every number looks changed.
	var scenario_path := "res://starts/%s/map_data.json" % GameState.current_start
	var start_state := MapData.LoadStartState(MapData.ScenarioDirFromPath(scenario_path))
	if start_state.is_empty():
		push_error("Save failed! Could not load start state: " + scenario_path)
		return

	var baseline: Dictionary = JSON.parse_string(JSON.stringify(start_state))
	var current: Dictionary = JSON.parse_string(JSON.stringify(save_data))
	var save_file_data: Dictionary = {
		"scenario_path": scenario_path,
		"diff": SettingsManager.diff(baseline, current),
	}

	# Ensure directory exists
	var dir = DirAccess.open("res://")
	if not dir.dir_exists("saves/"):
		dir.make_dir_recursive("saves/")

	var path = "res://saves/" + slot + ".json"
	if MapData.SaveJSON(path, save_file_data):
		print("Game State saved successfully to: ", ProjectSettings.globalize_path(path))

	
func load_game(save_name: String):
	var path: String = "res://saves/" + save_name + ".json"

	if not FileAccess.file_exists(path):
		push_error("Save file not found: " + path)
		return

	# Diff saves are merged with their start; legacy full saves are used as-is.
	var save_data: Dictionary = MapData.LoadSaveState(path)
	if save_data.is_empty():
		push_error("Failed to load save state: " + path)
		return

	var scenario_path: String = save_data.get(
		"scenario_path",
		"res://starts/%s/map_data.json" % GameState.current_start,
	)
	var start_dir := MapData.ScenarioDirFromPath(scenario_path)

	# Pause systems that react to state
	if map_container:
		map_container.set_process(false)

	# Full State Reset
	TroopManager.clear_all_troops()
	WarManager.reset_state()

	# Close Peace UI if open
	var peace_ui = get_tree().root.find_child("PeaceProcessUI", true, false)
	if peace_ui and peace_ui.has_method("force_close"):
		peace_ui.force_close()
	GameState.in_peace_process = false

	GameState.current_start = start_dir.get_file()
	GameState.is_loading_game = true
	MapData.LoadMapData(start_dir, [0.0], save_data)
	GameState.is_loading_game = false
	_refresh_map_visuals()

	if map_container:
		map_container.set_process(true)

	print("Game loaded successfully:", save_name)
