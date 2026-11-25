extends SceneTree

# Run: godot-mono --headless --path . --script res://scratch/test_port_parity.gd

var _checks := 0
var _failures := 0


func _init() -> void:
	var biome = _script("res://Scripts/Map/BiomeData.cs")
	var b = biome.FromDict({"name": "X", "color": "#FF0000"})
	_check(b.ToDict() == {"name": "X", "color": "#FF0000"}, "BiomeData round-trip")

	var resource = _script("res://Scripts/Map/ResourceData.cs")
	var r = resource.FromDict({"name": "R", "color": "#00FF00", "icon": "i", "tags": {"fuel": 2}})
	var rd = r.ToDict()
	_check(rd["name"] == "R" and rd["color"] == "#00FF00" and rd["icon"] == "i", "ResourceData fields")
	_check(absf(float(rd["tags"]["fuel"]) - 2.0) < 0.001, "ResourceData tags float")

	var building = _script("res://Scripts/Map/BuildingData.cs")
	var bd = building.FromDict({"type": "f", "state": 1, "durability": 0.5}).ToDict()
	_check(bd == {"type": "f", "state": 1, "durability": 0.5}, "BuildingData round-trip")

	var population = _script("res://Scripts/Map/PopulationData.cs")
	var pd = population.FromDict({"ethnicity": "e", "culture": "c", "amount": 7}).ToDict()
	_check(pd == {"ethnicity": "e", "culture": "c", "amount": 7}, "PopulationData round-trip")

	var node = _script("res://Scripts/Map/ResourceNode.cs")
	var nd = node.FromDict({"type": "oil", "amount": 3, "quality": 1.5}).ToDict()
	_check(nd == {"type": "oil", "amount": 3, "quality": 1.5}, "ResourceNode round-trip")

	var releasable = _script("res://Scripts/Map/ReleasableData.cs")
	var rel = releasable.FromDict({"data": {"name": "R"}, "claims": [1, 2]}).ToDict()
	_check(rel["data"]["name"] == "R" and rel["claims"].size() == 2, "ReleasableData round-trip")

	var template = _script("res://Scripts/Map/BuildingTemplate.cs")
	var bt = template.FromDict({"name": "t", "power": 2, "functionalities": [], "assigned_inputs": {}}).ToDict()
	_check(bt["name"] == "t" and is_equal_approx(bt["power"], 2.0), "BuildingTemplate round-trip")

	var province = _script("res://Scripts/Map/Province.cs")
	var prov = province.FromDict({"type": 1, "name": "P", "polity": "C", "resources": [], "buildings": [], "populations": []})
	var pdict = prov.ToDict()
	_check(pdict["polity"] == "C" and pdict["claims"].is_empty(), "Province default claims collapse")
	prov.claims = PackedStringArray(["C"])
	_check(prov.ToDict()["claims"].is_empty(), "Province explicit own claim collapse")
	prov.claims = PackedStringArray(["C", "D"])
	_check(prov.ToDict()["claims"].size() == 2, "Province multi-claim preserved")

	var faction_member = _script("res://Scripts/Diplomacy/FactionMember.cs")
	var fm = faction_member.FromDict({"polity": "C", "status": "Leader"}).ToDict()
	_check(fm == {"polity": "C", "status": "Leader"}, "FactionMember round-trip")

	var faction = _script("res://Scripts/Diplomacy/FactionData.cs")
	var fd = faction.FromDict({"name": "F", "color": "#0000FF", "members": [{"polity": "C", "status": "Leader"}]}).ToDict()
	_check(fd["name"] == "F" and fd["color"] == "#0000FF" and fd["members"].size() == 1, "FactionData round-trip")

	var drift = _script("res://Scripts/Diplomacy/IdeologyDriftTarget.cs")
	var drift_dict = drift.FromDict({"final_position": [1, 2], "drift_amount": 0.5}).ToDict()
	_check(drift_dict["final_position"] == [1.0, 2.0] and is_equal_approx(drift_dict["drift_amount"], 0.5), "IdeologyDriftTarget round-trip")

	var figure = _script("res://Scripts/Diplomacy/ImportantFigure.cs")
	var fig_dict = figure.FromDict({
		"name": "A", "skills": {}, "traits": [], "ideology": [1, 2],
		"allegiance": "C", "occupation": "x", "legitimacy": 90,
		"portrait_path": "", "status": 1,
	}).ToDict()
	_check(fig_dict["ideology"] == [1, 2], "ImportantFigure ideology Vector2i")
	_check(fig_dict["status"] == 1, "ImportantFigure status int")

	print("port parity tests: %d checks, %d failures" % [_checks, _failures])
	quit(1 if _failures > 0 else 0)


func _script(path: String):
	var script = load(path)
	assert(script != null, "failed to load " + path)
	return script


func _check(condition: bool, label: String) -> void:
	_checks += 1
	if condition:
		print("PASS ", label)
	else:
		_failures += 1
		print("FAIL ", label)
