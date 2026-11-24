extends Node

# Run: godot-mono --headless --path . res://scratch/test_save_diff_flow.tscn

const SAVE_PATH := "res://saves/__test_save_diff_flow.json"

var _checks: int = 0
var _failures: int = 0


func _ready() -> void:
	var start_dir := "res://starts/ModernDay"
	var start_state: Dictionary = MapData.LoadStartState(start_dir)
	_check(not start_state.is_empty(), "LoadStartState: not empty")
	_check(start_state.has("provinces"), "LoadStartState: has provinces")
	_check(start_state.has("polities"), "LoadStartState: has polities")
	_check(not start_state.has("significant_figures"), "LoadStartState: drops non-state keys")

	# Synthetic "current state": change a scalar, edit a nested value,
	# add a key, remove a province, append a polity, add scheduled events.
	var current: Dictionary = start_state.duplicate(true)
	current["clock"]["hour"] = 13.0
	current["new_state_key"] = 42.0
	var province_keys: Array = current["provinces"].keys()
	if not province_keys.is_empty():
		current["provinces"].erase(province_keys[0])
	current["polities"].append({"tag": "TEST", "name": "Testland"})
	current["scheduled_events"] = [{"trigger_time": 1.0, "event_data": {"id": "x"}}]

	var diff: Dictionary = SettingsManager.diff(start_state, current)
	_check(diff.has("clock"), "diff: clock changed")
	_check(diff.has("new_state_key"), "diff: new key added")
	_check(diff.has("scheduled_events"), "diff: events added")
	_check(SettingsManager.merge(start_state, diff) == current, "merge: round-trip equals current")

	# Live state uses ints where parsed JSON uses floats. Normalizing both sides
	# through JSON must keep an unchanged value out of the diff.
	var live: Dictionary = start_state.duplicate(true)
	live["clock"]["hour"] = int(start_state["clock"]["hour"])
	var norm_start: Dictionary = JSON.parse_string(JSON.stringify(start_state))
	var norm_live: Dictionary = JSON.parse_string(JSON.stringify(live))
	_check(not SettingsManager.diff(norm_start, norm_live).has("clock"), "normalization: int/float drift ignored")

	# Diff save: scenario_path + diff must rebuild the same state.
	var file := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	file.store_string(JSON.stringify({
		"scenario_path": "res://starts/ModernDay/map_data.json",
		"diff": diff,
	}))
	file.close()
	var loaded: Dictionary = MapData.LoadSaveState(SAVE_PATH)
	var loaded_scenario_path: String = loaded.get("scenario_path", "")
	loaded.erase("scenario_path")
	_report_mismatch(loaded, current, "diff save")
	_check(loaded == current, "LoadSaveState: diff save merged with start")
	_check(loaded_scenario_path == "res://starts/ModernDay/map_data.json", "LoadSaveState: keeps scenario_path")
	_check(MapData.ScenarioDirFromPath(loaded_scenario_path) == start_dir, "ScenarioDirFromPath: file path")
	_check(MapData.ScenarioDirFromPath(start_dir) == start_dir, "ScenarioDirFromPath: dir path")

	# Legacy full-state save must pass through untouched.
	file = FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	file.store_string(JSON.stringify(current))
	file.close()
	_report_mismatch(MapData.LoadSaveState(SAVE_PATH), current, "legacy save")
	_check(MapData.LoadSaveState(SAVE_PATH) == current, "LoadSaveState: legacy full save")

	DirAccess.remove_absolute(SAVE_PATH)
	print("save diff flow tests: %d checks, %d failures" % [_checks, _failures])
	get_tree().quit(1 if _failures > 0 else 0)


func _report_mismatch(p_actual: Dictionary, p_expected: Dictionary, p_label: String) -> void:
	if p_actual == p_expected:
		return
	var keys: Array = p_expected.keys()
	for key in p_actual.keys():
		if not keys.has(key):
			print("  %s extra key: %s" % [p_label, key])
	for key in keys:
		if not p_actual.has(key):
			print("  %s missing key: %s" % [p_label, key])
		elif p_actual[key] != p_expected[key]:
			var a := JSON.stringify(p_actual[key], "")
			var b := JSON.stringify(p_expected[key], "")
			var show_len := mini(a.length(), 160)
			print("  %s key %s actual=%s expected=%s" % [p_label, key, a.substr(0, show_len), b.substr(0, mini(b.length(), 160))])


func _check(p_condition: bool, p_label: String) -> void:
	_checks += 1
	if p_condition:
		print("PASS ", p_label)
	else:
		_failures += 1
		print("FAIL ", p_label)
