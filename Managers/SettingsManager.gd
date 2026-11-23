extends Node

const SETTINGS_FILE = "res://settings.json"

var settings: ConfigFile = ConfigFile.new()

func _ready():
	settings.set_value("Developer Options", "debug_mode", false)

	settings.set_value("Sound", "radios", MusicManager.radios)
	settings.set_value("Sound", "sfx_volume", 0.1)
	settings.set_value("Sound", "music_volume", 1.0)

	settings.set_value("Graphics", "scanlines", 0.0)
	settings.set_value("Graphics", "vignette_lower", 0.0)
	settings.set_value("Graphics", "vignette_upper", 0.0)
	settings.set_value("Graphics", "map_effects", 1.0)
	settings.set_value("Graphics", "provinceBorderThickness", 0.5)
	settings.set_value("Graphics", "province_borders", 0.8)
	settings.set_value("Graphics", "ui_upper", 0.8)
	settings.set_value("Graphics", "ui_lower", 0.8)
	settings.set_value("Graphics", "ui_dirt", 0.8)
	settings.set_value("Graphics", "daynight_contrast", 0.01)
	settings.set_value("Graphics", "daynight_smoothness", 0.01)
	settings.set_value("Graphics", "clouds", 0.01)
	settings.set_value("Graphics", "clouds_edgeness", 0.01)

	load_settings()

func load_settings():
	if FileAccess.file_exists(SETTINGS_FILE):
		settings.load(SETTINGS_FILE)
	else:
		# If file doesn't exist, create it with defaults
		settings.save(SETTINGS_FILE)

static func apply_settings(a_settings: ConfigFile):
	# Audio
	MusicManager.set_sfx_volume(a_settings.get_value("Sound", "sfx_volume"))
	MusicManager.set_music_volume(a_settings.get_value("Sound", "music_volume"))
	MusicManager.radios = a_settings.get_value("Sound", "radios")

	# Graphics (These require GameState.current_world to be set)
	if GameState.current_world and GameState.current_world.map_sprite:
		var mat = GameState.current_world.map_sprite.material
		# shader param -> Graphics key
		var shader_map = {
			"scanlines": "scanlines",
			"upper": "vignette_upper",
			"lower": "vignette_lower",
			"toggle": "map_effects",
			"contrast": "daynight_contrast",
			"smoothness": "daynight_smoothness",
			"clouds": "clouds",
			"edgeness": "clouds_edgeness",
			"BorderThickness": "provinceBorderThickness",
			"internal_border_darkness": "province_borders",
		}
		for param in shader_map:
			mat.set_shader_parameter(param, a_settings.get_value("Graphics", shader_map[param]))
	
	# UI Effects (Wait for idfkanymore if it's in Settings menu, 
	# but these are actually for the whole UI if managed globally)
	# For now, we apply them when the settings menu is open/ready
