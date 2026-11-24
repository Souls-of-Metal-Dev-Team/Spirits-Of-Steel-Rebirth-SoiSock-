extends CanvasLayer

# --- CONFIGURATION ---
const GRID_SIZE = 50.0
const NODE_SIZE = Vector2(220, 90)

const COL_LINE_INACTIVE = Color(0.3, 0.3, 0.3)
const COL_LINE_ACTIVE = Color(0.2, 0.8, 0.2)
const COL_LINE_EXCLUSIVE = Color(0.8, 0.2, 0.2)

# GraphNode slots. The body carries the input port; the two labels carry outputs.
const SLOT_BODY := 0
const SLOT_PREREQ := 1
const SLOT_EXCLUSIVE := 2

# GraphNode ports are numbered per side. Slot 1 -> output port 0, slot 2 -> output port 1.
const PORT_INPUT := 0
const PORT_PREREQ_OUT := 0
const PORT_EXCLUSIVE_OUT := 1

# Port types. TYPE_INPUT is accepted from both relation types via GraphEdit.
const TYPE_PREREQ := 0
const TYPE_EXCLUSIVE := 1
const TYPE_INPUT := 2

# --- NODES ---
@onready var tree_graph: GraphEdit = $CanvasAnchor/TreeGraph
@onready var tabs_container: TabBar = $StaticUI/Header/TabsContainer
@export var info_text: RichTextLabel
@export var info_tab: TabContainer
@export var edit_label: TextEdit
@export var edit_desc: TextEdit
@export var edit_days: SpinBox
@export var edit_ppcost: SpinBox
@export var close_button: Button
@export var edit_button: Button
@export var editing_buttons: HBoxContainer
@export var edit_function: CheckBox
@export var save_button: Button
@export var add_decision: Button
@export var add_req: Button
@export var add_finished: Button
@export var editor_tab: TabContainer
@export var reqs_block: VBoxContainer
@export var finished_block: VBoxContainer
@onready var expression_scene = load("res://Scenes/Expression.tscn")


var current_category: String = "Economy"
var graph_nodes: Dictionary = {} # decision id -> GraphNode
var edit_mode: bool = false
var selected_decision: Dictionary = {}
var selected_node: GraphNode = null
var _is_loading_expr: bool = false
var _drag_origin: Dictionary = {}


func _ready():
	DecisionManager.ui_overlay = self
	edit_button.visible = SettingsManager.settings.get_value("Developer Options", "debug_mode")

	tree_graph.add_valid_connection_type(TYPE_PREREQ, TYPE_INPUT)
	tree_graph.add_valid_connection_type(TYPE_INPUT, TYPE_PREREQ)
	tree_graph.add_valid_connection_type(TYPE_EXCLUSIVE, TYPE_INPUT)
	tree_graph.add_valid_connection_type(TYPE_INPUT, TYPE_EXCLUSIVE)
	tree_graph.connection_drag_started.connect(_on_connection_drag_started)
	tree_graph.connection_request.connect(_on_connection_request)
	tree_graph.disconnection_request.connect(_on_disconnection_request)
	tree_graph.delete_nodes_request.connect(_on_delete_nodes_request)
	tree_graph.end_node_move.connect(_on_end_node_move)
	tree_graph.node_selected.connect(_on_graph_node_selected)

	_on_edit_mode_toggled(false)
	tabs_container.tab_changed.connect(_on_tab_changed)


# --- LOGIC ---
func OpenMenu():
	show()
	_rebuild_tabs()
	_load_category(current_category)
	GameState.decision_menu_open = true
	# Editing is only available in debug mode
	edit_button.visible = SettingsManager.settings.get_value("Developer Options", "debug_mode")
	if not SettingsManager.settings.get_value("Developer Options", "debug_mode"):
		edit_button.set_pressed_no_signal(false)
	_on_edit_mode_toggled(edit_button.button_pressed)
	_toggle_pause(true)


func CloseMenu():
	hide()
	GameState.decision_menu_open = false
	_toggle_pause(false)


func _toggle_pause(pause: bool):
	var world = GameState.current_world
	if world:
		world.set_process(!pause)
		world.clock.set_process(!pause)
		var cam = world.find_child("CameraController", true, false)
		if cam:
			cam.set_process(!pause)
	TroopManager.set_process(!pause)


func _rebuild_tabs():
	while tabs_container.tab_count > 0:
		tabs_container.remove_tab(0)

	var player_country = CountryManager.player_country
	var all_decisions = DecisionManager.get_country_categories(player_country.country_name)
	var categories = all_decisions.keys()

	for i in range(categories.size()):
		var cat = categories[i]
		tabs_container.add_tab(cat.to_upper())
		if cat == current_category:
			tabs_container.current_tab = i


func _on_tab_changed(tab_idx: int):
	var player_country = CountryManager.player_country
	var all_decisions = DecisionManager.get_country_categories(player_country.country_name)
	var categories = all_decisions.keys()

	if tab_idx < categories.size():
		current_category = categories[tab_idx]
		_load_category(current_category)


func _load_category(cat_name: String):
	for c in tree_graph.get_children():
		if c is GraphNode:
			# Remove immediately so new nodes can reuse the same names.
			tree_graph.remove_child(c)
			c.queue_free()
	tree_graph.clear_connections()
	graph_nodes.clear()
	_clear_selection()

	var player = CountryManager.player_country
	var nodes = DecisionManager.get_country_categories(player.country_name).get(cat_name, [])

	for i in range(nodes.size()):
		_create_node(nodes[i], i, player)

	_update_connections()
	call_deferred("_refresh_port_colors")


func _update_connections():
	tree_graph.clear_connections()
	var nodes := _current_nodes()

	var name_by_id := {}
	for i in range(nodes.size()):
		name_by_id[str(nodes[i]["id"])] = _node_name(i)

	# Prerequisites point from the required decision to the dependent decision.
	for node in nodes:
		var target_name: StringName = name_by_id.get(str(node["id"]), StringName())
		if target_name == StringName():
			continue
		for req_id in _as_array(node.get("prereq")):
			var source_name: StringName = name_by_id.get(str(req_id), StringName())
			if source_name != StringName() and source_name != target_name:
				tree_graph.connect_node(
					source_name, PORT_PREREQ_OUT, target_name, PORT_INPUT, true
				)

	# Exclusive edges are drawn once, between the lexicographically lower and higher ids.
	var drawn_exclusives := {}
	for node in nodes:
		var node_id = str(node["id"])
		for ex_id in _as_array(node.get("exclusive")):
			var other_id = str(ex_id)
			if other_id == node_id:
				continue
			var low_id = node_id if node_id < other_id else other_id
			var high_id = other_id if node_id < other_id else node_id
			var pair_key = low_id + "|" + high_id
			if drawn_exclusives.has(pair_key):
				continue
			drawn_exclusives[pair_key] = true

			var source_name: StringName = name_by_id.get(low_id, StringName())
			var target_name: StringName = name_by_id.get(high_id, StringName())
			if source_name != StringName() and target_name != StringName():
				tree_graph.connect_node(
					source_name, PORT_EXCLUSIVE_OUT, target_name, PORT_INPUT, true
				)


func _create_node(data: Dictionary, idx: int, player: CountryData) -> GraphNode:
	var node := GraphNode.new()
	node.name = _node_name(idx)
	node.title = str(data.get("title", ""))
	node.position_offset = Vector2(data["pos"][0], data["pos"][1])
	node.custom_minimum_size = NODE_SIZE
	node.resizable = false
	node.draggable = edit_mode
	node.set_meta("idx", idx)
	node.set_meta("id", data["id"])
	node.mouse_entered.connect(func(): _show_info(data))
	node.mouse_exited.connect(func(): _reset_info())
	node.node_selected.connect(func(): _select_node(node))

	var body := Button.new()
	body.name = "Body"
	body.custom_minimum_size = Vector2(NODE_SIZE.x - 24, 44)
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.mouse_filter = Control.MOUSE_FILTER_STOP
	body.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	body.tooltip_text = _make_tooltip(data)
	body.mouse_entered.connect(func(): _show_info(data))
	body.mouse_exited.connect(func(): _reset_info())
	body.pressed.connect(
		func():
			if edit_mode:
				_select_node(node)
			else:
				DecisionManager.start_decision(player, current_category, idx)
	)
	node.add_child(body)

	var req_label := Label.new()
	req_label.name = "PrereqPort"
	req_label.text = "REQ"
	req_label.add_theme_font_size_override("font_size", 10)
	req_label.add_theme_color_override("font_color", Color(0.55, 0.55, 0.55))
	req_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	node.add_child(req_label)

	var ex_label := Label.new()
	ex_label.name = "ExclusivePort"
	ex_label.text = "EX"
	ex_label.add_theme_font_size_override("font_size", 10)
	ex_label.add_theme_color_override("font_color", Color(0.55, 0.55, 0.55))
	ex_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	node.add_child(ex_label)

	# GraphNode builds its port cache on ready, so slots are set up then.
	node.ready.connect(_setup_node_slots.bind(node), CONNECT_ONE_SHOT)
	tree_graph.add_child(node)
	_apply_node_style(node, data, player)

	graph_nodes[str(data["id"])] = node
	return node


func _setup_node_slots(node: GraphNode) -> void:
	if not is_instance_valid(node):
		return
	var data := _data_for_node(node)
	if data.is_empty():
		return

	var player = CountryManager.player_country
	var prereq_color = COL_LINE_ACTIVE if player.has_meta("finished_" + str(data["id"])) \
		else COL_LINE_INACTIVE
	node.set_slot(SLOT_BODY, true, TYPE_INPUT, COL_LINE_INACTIVE, false, 0, Color.WHITE)
	node.set_slot(
		SLOT_PREREQ, false, 0, Color.WHITE, true, TYPE_PREREQ, prereq_color
	)
	node.set_slot(
		SLOT_EXCLUSIVE, false, 0, Color.WHITE, true, TYPE_EXCLUSIVE, COL_LINE_EXCLUSIVE
	)


func _refresh_port_colors() -> void:
	var player = CountryManager.player_country
	for node in graph_nodes.values():
		if not is_instance_valid(node) or node.get_output_port_count() < 2:
			continue
		var data := _data_for_node(node)
		if data.is_empty():
			continue
		var prereq_color = COL_LINE_ACTIVE if player.has_meta("finished_" + str(data["id"])) \
			else COL_LINE_INACTIVE
		node.set_slot_color_left(SLOT_BODY, COL_LINE_INACTIVE)
		node.set_slot_color_right(SLOT_PREREQ, prereq_color)
		node.set_slot_color_right(SLOT_EXCLUSIVE, COL_LINE_EXCLUSIVE)


func _show_info(data: Dictionary):
	info_text.text = "[b][font_size=26][color=yellow]%s[/color][/font_size][/b]\n" % data["title"]
	info_text.text += "[font_size=18][i]%s[/i][/font_size]\n\n" % data.get("desc", "")
	info_text.text += (
		"[color=orange]Time: %d Days | Cost: %d Political Power[/color]"
		% [data["days"], data["cost_pp"]]
	)

	if data.has("reqs"):
		var reqs = data["reqs"]
		if not reqs is Array:
			reqs = [reqs]

		var final_req_texts = []
		for req in reqs:
			if not req is Dictionary: continue

			var args = req.get("args", [])
			var req_line = req.get("func", "Unknown").capitalize()
			if args.size() > 0:
				var arg_strs = []
				for a in args:
					arg_strs.append(str(a).capitalize())
				req_line += ": " + ", ".join(arg_strs)
			final_req_texts.append(req_line)

		if not final_req_texts.is_empty():
			var full_text = "\n".join(final_req_texts)
			info_text.text += "\n[color=red]Requirement: %s[/color]" % full_text


func _reset_info():
	info_text.text = "[center][color=gray]Hover over a node to see info[/color][/center]"


func _make_tooltip(data: Dictionary) -> String:
	var tt = str(data["title"])
	if str(data.get("desc", "")) != "":
		tt += "\n" + str(data["desc"])

	var reqs_text = str(data.get("reqs", []))
	if reqs_text != "" and reqs_text != "[]":
		tt += "\n\n[ Requirements ]\n" + reqs_text

	var action_text = str(data.get("action", []))
	if action_text != "" and action_text != "[]":
		tt += "\n\n[ On Finished ]\n" + action_text
	return tt


func _apply_node_style(node: GraphNode, data: Dictionary, player: CountryData):
	var body := node.get_node_or_null("Body") as Button
	var id = str(data["id"])

	var status = ""
	var disabled := false
	var style = StyleBoxFlat.new()
	style.corner_radius_top_left = 4
	style.corner_radius_top_right = 4
	style.corner_radius_bottom_left = 4
	style.corner_radius_bottom_right = 4
	style.border_width_bottom = 4

	if player.has_meta("finished_" + id):
		status = "[DONE]"
		style.bg_color = Color(0.1, 0.4, 0.1)
		disabled = true
	elif DecisionManager.is_in_progress(player, id):
		status = "⌛ %d Days" % DecisionManager.get_days_left(player, id)
		style.bg_color = Color(0.1, 0.2, 0.5) # Dark Blue
		disabled = true
	else:
		var is_mutually_locked = false
		for ex_id in _as_array(data.get("exclusive")):
			if player.has_meta("finished_" + str(ex_id)) or \
				DecisionManager.is_in_progress(player, str(ex_id)):
				is_mutually_locked = true
				break

		var is_prereq_locked = false
		for req_id in _as_array(data.get("prereq")):
			if not player.has_meta("finished_" + str(req_id)):
				is_prereq_locked = true
				break

		if is_mutually_locked:
			status = "[LOCKED]"
			style.bg_color = Color(0.4, 0.1, 0.1)
			disabled = true
		elif is_prereq_locked:
			style.bg_color = Color(0.241, 0.102, 0.101, 1.0)
			disabled = true
		else:
			status = "%d PP" % data["cost_pp"]
			style.bg_color = Color(0.2, 0.2, 0.2)
			# Also check if another decision is already running
			disabled = (
				player.political_power < data["cost_pp"] or DecisionManager.is_country_busy(player)
			)

	if body:
		# Keep bodies clickable in edit mode so they can be selected for editing.
		body.disabled = disabled and not edit_mode
		body.text = status
		body.add_theme_stylebox_override("normal", style)
		body.add_theme_stylebox_override("disabled", style)
		body.add_theme_stylebox_override("hover", style)
		body.add_theme_stylebox_override("pressed", style)

	var title_style = style.duplicate() as StyleBoxFlat
	title_style.border_width_bottom = 0
	node.title = str(data["title"])
	node.add_theme_stylebox_override("panel", style)
	node.add_theme_stylebox_override("panel_selected", style)
	node.add_theme_stylebox_override("titlebar", title_style)
	node.add_theme_stylebox_override("titlebar_selected", title_style)


func RefreshStatusOnly():
	if not visible:
		return
	var player: CountryData = CountryManager.player_country
	var nodes: Array = DecisionManager.get_country_categories(player.country_name).get(
		current_category, []
	)
	for node in graph_nodes.values():
		if is_instance_valid(node):
			var idx := int(node.get_meta("idx", -1))
			if idx >= 0 and idx < nodes.size():
				_apply_node_style(node, nodes[idx], player)
	_update_connections()
	call_deferred("_refresh_port_colors")


func _on_reload_decisions_pressed():
	var rel_path = "res://starts/" + GameState.current_start + "/decisions/"
	DecisionManager.load_decisions_from_path(rel_path)
	_load_category(current_category)


func _on_edit_mode_toggled(toggled_on: bool):
	edit_mode = toggled_on and SettingsManager.settings.get_value("Developer Options", "debug_mode")
	editing_buttons.visible = edit_mode

	if info_tab:
		info_tab.current_tab = 1 if edit_mode else 0

	var player = CountryManager.player_country
	var nodes: Array = []
	if player:
		nodes = DecisionManager.get_country_categories(player.country_name).get(
			current_category, []
		)
	for node in graph_nodes.values():
		if is_instance_valid(node):
			node.draggable = edit_mode
			var idx := int(node.get_meta("idx", -1))
			if player and idx >= 0 and idx < nodes.size():
				_apply_node_style(node, nodes[idx], player)

	if not edit_mode:
		_clear_selection()
	call_deferred("_refresh_port_colors")


func _clear_selection():
	selected_decision = {}
	selected_node = null
	edit_label.text = ""
	edit_desc.text = ""
	edit_days.value = 0
	edit_ppcost.value = 0
	_load_expression_blocks({})


func _select_node(node: GraphNode) -> void:
	if not edit_mode or not is_instance_valid(node):
		return
	var data := _data_for_node(node)
	if data.is_empty() or (selected_node == node and selected_decision == data):
		return

	selected_decision = data
	selected_node = node
	edit_label.text = str(data.get("title", ""))
	edit_desc.text = str(data.get("desc", ""))
	edit_days.value = data.get("days", 0)
	edit_ppcost.value = data.get("cost_pp", 0)
	_load_expression_blocks(data if edit_function.button_pressed else {})


func _on_save_decisions_pressed():
	if not selected_decision.is_empty() and is_instance_valid(selected_node):
		selected_decision["reqs"] = reqs_block.get_children().filter(
			func(x): return !x.is_queued_for_deletion()
		).map(func(x): return x.ToDict())
		selected_decision["action"] = finished_block.get_children().filter(
			func(x): return !x.is_queued_for_deletion()
		).map(func(x): return x.ToDict())
		_update_node_visuals(selected_node, selected_decision)
	DecisionManager.save_country_decisions(CountryManager.player_country.country_name)


# --- GRAPH EDIT INTERACTIONS ---
func _on_graph_node_selected(node: Node) -> void:
	if node is GraphNode:
		_select_node(node)


func _on_end_node_move() -> void:
	if not edit_mode:
		return
	var nodes := _current_nodes()
	for child in tree_graph.get_children():
		if not (child is GraphNode) or not child.selected:
			continue
		var idx := int(child.get_meta("idx", -1))
		if idx < 0 or idx >= nodes.size():
			continue
		var pos = (child.position_offset / GRID_SIZE).round() * GRID_SIZE
		child.position_offset = pos
		nodes[idx]["pos"] = [pos.x, pos.y]


func _on_connection_drag_started(
	from_node: StringName, from_port: int, is_output: bool
) -> void:
	# GraphEdit port indices overlap between sides; remember which side started the drag.
	_drag_origin = {"node": from_node, "port": from_port, "is_output": is_output}


func _on_connection_request(
	from_node: StringName, from_port: int, to_node: StringName, to_port: int
) -> void:
	if not edit_mode:
		return
	var relation := _resolve_connection(from_node, from_port, to_node, to_port)
	_drag_origin.clear()
	if relation.is_empty():
		return
	_apply_relation(
		str(relation["source_id"]), str(relation["target_id"]), str(relation["key"]), true
	)


func _on_disconnection_request(
	from_node: StringName, from_port: int, to_node: StringName, to_port: int
) -> void:
	if not edit_mode:
		return
	var relation := _resolve_connection(from_node, from_port, to_node, to_port)
	_drag_origin.clear()
	if relation.is_empty():
		return
	_apply_relation(
		str(relation["source_id"]), str(relation["target_id"]), str(relation["key"]), false
	)


func _on_delete_nodes_request(nodes: Array) -> void:
	if not edit_mode or nodes.is_empty():
		return

	var removed_ids := []
	for node_name in nodes:
		var id := _id_for_node_name(str(node_name))
		if id != "":
			removed_ids.append(id)
	if removed_ids.is_empty():
		return

	var categories = DecisionManager.get_country_categories_editable(
		CountryManager.player_country.country_name
	)
	var category_nodes: Array = categories.get(current_category, [])
	for node_data in category_nodes:
		for key in ["prereq", "exclusive"]:
			var values = _as_array(node_data.get(key))
			for removed_id in removed_ids:
				values.erase(removed_id)
			if values.is_empty():
				node_data.erase(key)
			else:
				node_data[key] = values

	var remove_indices := []
	for i in range(category_nodes.size()):
		if str(category_nodes[i]["id"]) in removed_ids:
			remove_indices.append(i)
	remove_indices.reverse()
	for i in remove_indices:
		category_nodes.remove_at(i)

	_clear_selection()
	_load_category(current_category)


func _port_type(node_name: StringName, port_idx: int, is_output: bool) -> int:
	var node := tree_graph.get_node_or_null(NodePath(str(node_name)))
	if not (node is GraphNode):
		return -1
	if is_output:
		if port_idx < node.get_output_port_count():
			return node.get_output_port_type(port_idx)
	elif port_idx < node.get_input_port_count():
		return node.get_input_port_type(port_idx)
	return -1


# Accepts both drag directions: output -> input and input -> output.
func _resolve_connection(
	from_node: StringName, from_port: int, to_node: StringName, to_port: int
) -> Dictionary:
	var from_is_output := true
	if not _drag_origin.is_empty() \
		and str(_drag_origin.get("node", "")) == str(from_node) \
		and int(_drag_origin.get("port", -1)) == from_port:
		from_is_output = bool(_drag_origin.get("is_output", true))

	var from_type := _port_type(from_node, from_port, from_is_output)
	var to_type := _port_type(to_node, to_port, not from_is_output)
	if from_type < 0 or to_type < 0:
		return {}

	var source_name: StringName = StringName()
	var target_name: StringName = StringName()
	var port_type := -1
	if from_is_output:
		if to_type != TYPE_INPUT:
			return {}
		source_name = from_node
		target_name = to_node
		port_type = from_type
	else:
		if from_type != TYPE_INPUT:
			return {}
		source_name = to_node
		target_name = from_node
		port_type = to_type

	if source_name == target_name:
		return {}
	var key := ""
	if port_type == TYPE_PREREQ:
		key = "prereq"
	elif port_type == TYPE_EXCLUSIVE:
		key = "exclusive"
	else:
		return {}

	var source_id := _id_for_node_name(source_name)
	var target_id := _id_for_node_name(target_name)
	if source_id == "" or target_id == "":
		return {}
	return {"source_id": source_id, "target_id": target_id, "key": key}


func _apply_relation(source_id: String, target_id: String, key: String, adding: bool) -> void:
	var target_data := _data_for_id(target_id)
	var source_data := _data_for_id(source_id)
	if target_data.is_empty() or source_data.is_empty():
		return

	var values = _as_array(target_data.get(key))
	if adding:
		if source_id not in values:
			values.append(source_id)
	else:
		values.erase(source_id)

	if values.is_empty():
		target_data.erase(key)
	else:
		target_data[key] = values

	# A relation is always either a prerequisite or an exclusion, never both.
	var other_key = "exclusive" if key == "prereq" else "prereq"
	_erase_relation(target_data, other_key, source_id)
	_erase_relation(source_data, other_key, target_id)

	# Keep mutual exclusion symmetrical so both nodes show the lock.
	if key == "exclusive":
		var reverse = _as_array(source_data.get("exclusive"))
		if adding:
			if target_id not in reverse:
				reverse.append(target_id)
		else:
			reverse.erase(target_id)
		if reverse.is_empty():
			source_data.erase("exclusive")
		else:
			source_data["exclusive"] = reverse

	_update_connections()


func _erase_relation(data: Dictionary, key: String, id: String) -> void:
	var values = _as_array(data.get(key))
	values.erase(id)
	if values.is_empty():
		data.erase(key)
	else:
		data[key] = values


# --- EDITING HELPERS ---
func _current_nodes() -> Array:
	var player = CountryManager.player_country
	var categories = DecisionManager.get_country_categories(player.country_name)
	return categories.get(current_category, [])


func _data_for_node(node: GraphNode) -> Dictionary:
	var nodes := _current_nodes()
	var idx := int(node.get_meta("idx", -1))
	if idx >= 0 and idx < nodes.size():
		return nodes[idx]
	return {}


func _data_for_id(id: String) -> Dictionary:
	for node_data in _current_nodes():
		if str(node_data["id"]) == id:
			return node_data
	return {}


func _id_for_node_name(node_name: StringName) -> String:
	for node in graph_nodes.values():
		if is_instance_valid(node) and str(node.name) == str(node_name):
			return str(node.get_meta("id", ""))
	return ""


func _node_name(idx: int) -> StringName:
	return StringName("Decision_%d" % idx)


func _as_array(value: Variant) -> Array:
	if value is Array:
		return value
	if value == null or value == "":
		return []
	return [value]


func _on_add_decision_pressed() -> void:
	var player: CountryData = CountryManager.player_country
	var country_cats: Dictionary = DecisionManager.get_country_categories_editable(player.country_name)

	if not country_cats.has(current_category):
		country_cats[current_category] = []

	var nodes = country_cats[current_category]
	var id: String = "%s_%d#%d" % [current_category, nodes.size(), randi_range(0, 100)]
	var new_decision = {
		"id": id,
		"title": id,
		"pos": [
			(tree_graph.scroll_offset.x + 100.0),
			(tree_graph.scroll_offset.y + 150.0)
		],
		"days": 20.0,
		"cost_pp": 25.0,
		"reqs": [],
		"action": [],
		"desc": ""
	}
	nodes.append(new_decision)
	var btn = _create_node(new_decision, nodes.size() - 1, player)

	# Automatically select the new decision
	selected_decision = new_decision
	selected_node = btn
	edit_label.text = new_decision.get("title", "")
	edit_desc.text = new_decision.get("desc", "")
	edit_days.value = new_decision.get("days", 0)
	edit_ppcost.value = new_decision.get("cost_pp", 0)
	_load_expression_blocks(new_decision)

	_update_connections()
	call_deferred("_refresh_port_colors")
	_on_save_decisions_pressed()


func _load_expression_blocks(data: Dictionary):
	_is_loading_expr = true
	for c in reqs_block.get_children():
		reqs_block.remove_child(c)
		c.queue_free()
	for c in finished_block.get_children():
		finished_block.remove_child(c)
		c.queue_free()

	for req in data.get("reqs", []):
		var expr = ExpressionBox.FromDict(req, true)
		reqs_block.add_child(expr)
		expr.changed.connect(_sync_expressions)

	for act in data.get("action", []):
		var expr = ExpressionBox.FromDict(act, true)
		finished_block.add_child(expr)
		expr.changed.connect(_sync_expressions)
	_is_loading_expr = false


func _on_add_req_pressed():
	_add_expr_to_block(reqs_block)


func _on_add_finished_pressed():
	_add_expr_to_block(finished_block)


func _add_expr_to_block(block: Control):
	if selected_decision.is_empty(): return
	var expr = expression_scene.instantiate()
	expr.is_child = true
	block.add_child(expr)
	expr.changed.connect(_sync_expressions)
	_sync_expressions()


func _sync_expressions():
	if _is_loading_expr or selected_decision.is_empty() or not is_instance_valid(selected_node): return
	selected_decision["reqs"] = reqs_block.get_children().filter(
		func(x): return !x.is_queued_for_deletion()
	).map(func(x): return x.ToDict())
	selected_decision["action"] = finished_block.get_children().filter(
		func(x): return !x.is_queued_for_deletion()
	).map(func(x): return x.ToDict())
	_update_node_visuals(selected_node, selected_decision)
	DecisionManager.save_country_decisions(CountryManager.player_country.country_name)


func _on_edit_label_text_changed() -> void:
	if selected_decision.is_empty() or not is_instance_valid(selected_node):
		return

	selected_decision["title"] = edit_label.text
	_update_node_visuals(selected_node, selected_decision)


func _on_edit_desc_text_changed() -> void:
	if selected_decision.is_empty() or not is_instance_valid(selected_node):
		return

	selected_decision["desc"] = edit_desc.text
	_update_node_visuals(selected_node, selected_decision)


func _on_edit_days_value_changed(value: float) -> void:
	if selected_decision.is_empty() or not is_instance_valid(selected_node):
		return

	selected_decision["days"] = int(value)
	_update_node_visuals(selected_node, selected_decision)


func _on_edit_ppcost_value_changed(value: float) -> void:
	if selected_decision.is_empty() or not is_instance_valid(selected_node):
		return

	selected_decision["cost_pp"] = int(value)
	_update_node_visuals(selected_node, selected_decision)


func _update_node_visuals(node: GraphNode, data: Dictionary):
	var player = CountryManager.player_country
	_apply_node_style(node, data, player)

	var tt = _make_tooltip(data)
	var body := node.get_node_or_null("Body") as Button
	if body:
		body.tooltip_text = tt
	node.tooltip_text = tt


func _on_edit_func_toggled(toggled_on: bool) -> void:
	add_req.visible = toggled_on
	add_finished.visible = toggled_on
	editor_tab.visible = toggled_on
	if toggled_on and not selected_decision.is_empty():
		_load_expression_blocks(selected_decision)
	elif not toggled_on:
		_load_expression_blocks({})
