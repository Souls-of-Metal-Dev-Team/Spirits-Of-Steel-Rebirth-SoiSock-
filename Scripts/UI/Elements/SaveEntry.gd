class_name SaveEntry
extends HBoxContainer

signal load_requested(save_name: String)
signal delete_requested(save_name: String)

@export var name_label: Label
@export var load_button: Button
@export var delete_button: Button

var save_name: String = ""


func Setup(p_save_name: String, scenario_exists: bool, scenario_path: String) -> void:
	save_name = p_save_name
	name_label.text = save_name
	if not scenario_exists:
		name_label.modulate = Color(0.5, 0.5, 0.5)
		load_button.disabled = true
		load_button.tooltip_text = "Scenario file missing: " + scenario_path


func _on_load_pressed() -> void:
	load_requested.emit(save_name)


func _on_delete_pressed() -> void:
	delete_requested.emit(save_name)
