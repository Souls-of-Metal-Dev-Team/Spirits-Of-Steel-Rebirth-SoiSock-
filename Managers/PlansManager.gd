extends Node

var plans: Dictionary = {}
var current_start_folder: String = ""


func _ready() -> void:
	load_plans_from_path("res://plans.json")


func load_plans_from_path(path: String):
	current_start_folder = path.get_base_dir() + "/"
	plans.clear()
	var data: Variant = MapData.LoadJSON(path)
	if data is Dictionary:
		plans = data

func has_available_plans(country: CountryData) -> bool:
	if not plans.has(country.country_name):
		return false
	
	var choices = []
	for element in plans[country.country_name]:
		if element == null:
			continue
		if element.get("type", "") == "button":
			if InterpreterManager.get_function(element.get("condition", {}), country):
				if country.is_player:
					return true
				else:
					choices.append(element.get("finished", {}))

	if !choices.is_empty():
		var choice = choices.pick_random()
		if choice:
			InterpreterManager.get_function(choice, country)
	return false
