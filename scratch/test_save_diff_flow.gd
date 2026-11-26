extends Node

# Run: godot-mono --headless --path . res://scratch/test_save_diff_flow.tscn

const SAVE_PATH := "user://__test_save_diff_flow.json"

var _checks: int = 0
var _failures: int = 0


func _ready() -> void:
	var start_dir := "res://starts/ModernDay"
	var start_state: Dictionary = GameState.mapData.LoadStartState(start_dir)
	_check(not start_state.is_empty(), "LoadStartState: not empty")
	_check(start_state.has("provinces"), "LoadStartState: has provinces")
	_check(start_state.has("polities"), "LoadStartState: has polities")
	_check(start_state.has("significant_figures"), "LoadStartState: has significant_figures")

	# Diff entries use the Sosr.Core.StateDiff wire format. GameState.mapData.LoadSaveState
	# routes them through SaveService.ApplySave -> StateDiff.Merge.
	var provinces: Dictionary = start_state["provinces"]
	var removed_pid: Variant = provinces.keys()[0]
	var new_hour: float = float((start_state["clock"] as Dictionary)["hour"]) + 1.0
	var events: Array = [{"trigger_time": 1.0, "event_data": {"id": "x"}}]
	var diff: Dictionary = {
		"clock": {"hour": {"type": "change", "value": new_hour}},
		"new_state_key": {"type": "add", "value": 42.0},
		"provinces": {removed_pid: {"type": "remove"}},
		"scheduled_events": {"type": "add", "value": events},
	}

	var expected: Dictionary = start_state.duplicate(true)
	(expected["clock"] as Dictionary)["hour"] = new_hour
	expected["new_state_key"] = 42.0
	(expected["provinces"] as Dictionary).erase(removed_pid)
	expected["scheduled_events"] = events

	# Diff save: scenario_path + diff must rebuild the same state.
	GameState.mapData.SaveJSON(SAVE_PATH, {
		"scenario_path": "res://starts/ModernDay/map_data.json",
		"diff": diff,
	})
	var loaded: Dictionary = GameState.mapData.LoadSaveState(SAVE_PATH)
	var loaded_scenario_path: String = loaded.get("scenario_path", "")
	loaded.erase("scenario_path")
	_report_mismatch(loaded, expected, "diff save")
	_check(loaded == expected, "LoadSaveState: diff save merged with start")
	_check(loaded_scenario_path == "res://starts/ModernDay/map_data.json", "LoadSaveState: keeps scenario_path")
	_check(GameState.mapData.ScenarioDirFromPath(loaded_scenario_path) == start_dir, "ScenarioDirFromPath: file path")
	_check(GameState.mapData.ScenarioDirFromPath(start_dir) == start_dir, "ScenarioDirFromPath: dir path")

	# Legacy full-state save must pass through untouched.
	GameState.mapData.SaveJSON(SAVE_PATH, expected)
	_report_mismatch(GameState.mapData.LoadSaveState(SAVE_PATH), expected, "legacy save")
	_check(GameState.mapData.LoadSaveState(SAVE_PATH) == expected, "LoadSaveState: legacy full save")

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
