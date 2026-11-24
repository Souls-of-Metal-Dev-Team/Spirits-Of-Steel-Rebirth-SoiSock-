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
		# If file doesn't exist, create it with funcaults
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



func diffValue(
    a_sourceValue: Variant,
    a_changeValue: Variant,
) -> Variant:
	match typeof(a_sourceValue):
		TYPE_ARRAY:
			var allArray = []
			allArray.append_array(a_sourceValue)
			allArray.append_array(a_changeValue)

			var changeDict = {}
			for value in allArray:
				if value not in a_sourceValue:
					changeDict.get_or_add("add", []).append(value)
				elif value not in a_changeValue:
					changeDict.get_or_add("remove", []).append(value)
			return changeDict

		TYPE_DICTIONARY:
			return diff(a_sourceValue, a_changeValue)

		TYPE_INT, TYPE_FLOAT, TYPE_STRING, TYPE_BOOL:
			return a_changeValue
			
		_:
			return


func diff(
    a_source: Dictionary,
    a_change: Dictionary,
) -> Dictionary:
	var sourceKeys = a_source.keys()
	var changeKeys = a_change.keys()
	var allKeys = []
	allKeys.append_array(sourceKeys)
	allKeys.append_array(changeKeys)

	var diffKeys: Dictionary[Variant, Variant] = {}
	for key in allKeys:
		diffKeys[key] = null

	var diffDict = {}
	for key in diffKeys.keys():
			if key not in sourceKeys:
					diffDict[key] = {"type": "add", "value": a_change[key]}
			elif key not in changeKeys:
					diffDict[key] = {"type": "remove", "value": a_source[key]}
			elif a_change[key] != a_source[key]:
				var source_type := typeof(a_source[key])
				var change_type := typeof(a_change[key])

				if source_type == TYPE_DICTIONARY and change_type == TYPE_DICTIONARY:
						diffDict[key] = diff(a_source[key], a_change[key])
				elif source_type == TYPE_ARRAY and change_type == TYPE_ARRAY:
						var array_diff: Dictionary = diffValue(a_source[key], a_change[key])
						array_diff["type"] = "change"
						diffDict[key] = array_diff
				else:
						diffDict[key] = {"type": "change", "value": a_change[key]}
					# diffDict[key] = {
					# 		"type": "change",
					# 		"value": diffValue(a_source[key], a_change[key]),
					# }

	return diffDict


func merge(
    a_source: Dictionary,
    a_diff: Dictionary,
) -> Dictionary:
	var result: Dictionary[Variant, Variant] = {}
	for key in a_source.keys():
		result[key] = a_source[key]

	for key in a_diff.keys():
		var entry: Variant = a_diff[key]
		if not entry is Dictionary:
			result[key] = entry
			continue

		var entry_dict: Dictionary = entry
		var entry_type: Variant = entry_dict.get("type")

		if entry_type == "remove":
			result.erase(key)
			continue

		if entry_type == "change" and (entry_dict.has("add") or entry_dict.has("remove")):
			var merged_array: Array = []
			if result.has(key) and result[key] is Array:
				var source_array: Array = result[key]
				merged_array.append_array(source_array)
			for value in entry_dict.get("remove", []):
				merged_array.erase(value)
			for value in entry_dict.get("add", []):
				merged_array.append(value)
			result[key] = merged_array
			continue

		if entry_type == "add" or entry_type == "change":
			result[key] = entry_dict.get("value")
			continue

		if result.has(key) and result[key] is Dictionary:
			result[key] = merge(result[key], entry_dict)
			continue

		result[key] = entry_dict

	return result

