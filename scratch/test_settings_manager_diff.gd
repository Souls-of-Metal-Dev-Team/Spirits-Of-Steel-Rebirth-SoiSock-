extends Node

# Run: godot-mono --headless --path . res://scratch/test_settings_manager_diff.tscn

var _manager: Node
var _checks: int = 0
var _failures: int = 0


func _ready() -> void:
	_manager = get_node("/root/SettingsManager")

	_test_diff_empty()
	_test_diff_unchanged()
	_test_diff_add_key()
	_test_diff_removed_key()
	_test_diff_changed_scalars()
	_test_diff_changed_bool()
	_test_diff_changed_dictionary()
	_test_diff_changed_array()
	_test_diff_unchanged_array()
	_test_diff_mixed()
	_test_diff_value_scalars()
	_test_diff_value_array()
	_test_merge_nested_add_remove()
	_test_merge_does_not_mutate_source()

	print("diff tests: %d checks, %d failures" % [_checks, _failures])
	get_tree().quit(1 if _failures > 0 else 0)


func _dict(p_entries: Dictionary) -> Dictionary[Variant, Variant]:
	var result: Dictionary[Variant, Variant] = {}
	for key in p_entries:
		result[key] = p_entries[key]
	return result


func _print_value(p_label: String, p_value: Variant) -> void:
	var text: String = str(p_value)
	if p_value is Dictionary or p_value is Array:
		text = JSON.stringify(p_value, "  ")
	var lines: PackedStringArray = text.split("\n")
	print("     %s: %s" % [p_label, lines[0]])
	for i in range(1, lines.size()):
		print("               %s" % lines[i])


func _check_equal(p_actual: Variant, p_expected: Variant, p_label: String) -> void:
	_checks += 1
	var equal: bool = false
	if typeof(p_actual) == typeof(p_expected):
		equal = p_actual == p_expected
	if equal:
		print("PASS ", p_label)
	else:
		_failures += 1
		print("FAIL ", p_label)
		_print_value("expected", p_expected)
		_print_value("actual", p_actual)


func _check_diff(p_source: Dictionary, p_change: Dictionary, p_expected: Dictionary, p_label: String) -> void:
	var source := _dict(p_source)
	var change := _dict(p_change)
	var diff: Dictionary = _manager.diff(source, change)
	_check_equal(diff, p_expected, p_label)
	_check_equal(_manager.merge(source, diff), change, "merge round-trip: " + p_label)


func _test_diff_empty() -> void:
	_check_diff({}, {}, {}, "diff: empty dictionaries")


func _test_diff_unchanged() -> void:
	_check_diff({"a": 1, "b": "x"}, {"a": 1, "b": "x"}, {}, "diff: unchanged keys")


func _test_diff_add_key() -> void:
	_check_diff({}, {"new": 5}, {"new": {"type": "add", "value": 5}}, "diff: addd key")


func _test_diff_removed_key() -> void:
	_check_diff({"ol": 5}, {}, {"ol": {"type": "remove", "value": 5}}, "diff: removed key")


func _test_diff_changed_scalars() -> void:
	_check_diff({"n": 1}, {"n": 2}, {"n": {"type": "change", "value": 2}}, "diff: changed int")
	_check_diff({"n": 1.5}, {"n": 2.5}, {"n": {"type": "change", "value": 2.5}}, "diff: changed float")
	_check_diff({"s": "a"}, {"s": "b"}, {"s": {"type": "change", "value": "b"}}, "diff: changed string")


func _test_diff_changed_bool() -> void:
	_check_diff(
		{"debug_mode": false},
		{"debug_mode": true},
		{"debug_mode": {"type": "change", "value": true}},
		"diff: changed bool"
	)


func _test_diff_changed_dictionary() -> void:
	_check_diff(
		{"nested": {"x": 1}},
		{"nested": {"x": 2}},
		{"nested": {"x": {"type": "change", "value": 2}}},
		"diff: changed dictionary"
	)


func _test_diff_changed_array() -> void:
	_check_diff(
		{"arr": [1, 2]},
		{"arr": [2, 3]},
		{
			"arr": 
				{
					"type": "change",
					"add": [3],
					"remove": [1],
				}
			,
		},
		"diff: changed array reports both sides"
	)


func _test_diff_unchanged_array() -> void:
	_check_diff({"arr": [1, 2]}, {"arr": [1, 2]}, {}, "diff: unchanged array")


func _test_diff_mixed() -> void:
	_check_diff(
		{"same": 1, "change": 2, "remove": 3, "arr": [1, 2]},
		{"same": 1, "change": 9, "add": 5, "arr": [2, 3]},
		{
			"change": {"type": "change", "value": 9},
			"remove": {"type": "remove", "value": 3},
			"arr": {"type": "change", "add": [3], "remove": [1]},
			"add": {"type": "add", "value": 5},
		},
		"diff: mixed keys"
	)


func _test_diff_value_scalars() -> void:
	_check_equal(_manager.diffValue(2, 9), 9, "diffValue: int")
	_check_equal(_manager.diffValue(1.5, 2.5), 2.5, "diffValue: float")
	_check_equal(_manager.diffValue("a", "b"), "b", "diffValue: string")
	_check_equal(_manager.diffValue(false, true), true, "diffValue: bool")
	_check_equal(
		_manager.diffValue(
			{"x": 1},
			{"x": 2},
		),
		{"x": {"type": "change", "value": 2}},
		"diffValue: dictionary",
	)


func _test_diff_value_array() -> void:
	_check_equal(
		_manager.diffValue([1, 2], [2, 3]),
		{"add": [3], "remove": [1]},
		"diffValue: array"
	)


func _test_merge_nested_add_remove() -> void:
	var source := _dict({"nested": {"keep": 1, "drop": 2}})
	var change := _dict({"nested": {"keep": 1, "new": 3}})
	_check_equal(
		_manager.merge(source, _manager.diff(source, change)),
		change,
		"merge: nested add and remove"
	)


func _test_merge_does_not_mutate_source() -> void:
	var source := _dict({"nested": {"x": 1}, "arr": [1, 2]})
	var change := _dict({"nested": {"x": 2}, "arr": [2, 3]})
	var diff: Dictionary = _manager.diff(source, change)
	_manager.merge(source, diff)
	_check_equal(
		source,
		_dict({"nested": {"x": 1}, "arr": [1, 2]}),
		"merge: source not mutated"
	)
