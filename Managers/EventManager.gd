extends Node

var active_dialogs: Array = []
var scheduled_events: Array = []
var super_events: Array = []

var ui_layer: CanvasLayer = CanvasLayer.new()


func _ready():
	ui_layer.layer = 100
	add_child(ui_layer)

	var vp = get_viewport()
	if vp:
		vp.gui_embed_subwindows = true

	WarManager.warDeclared.connect(
		func(warer: CountryData, waree: CountryData): show_alert(
			{
				"event": "war",
				"c1": warer,
				"c2": waree,
			}
		)
	)
	load_super_events("res://superevents.json")


# UNIFIED ENTRY POINT
func show_alert(data: Dictionary):
	var dialog: Window
	match data.get("event", "default"):
		"super":
			dialog = _create_super_event_dialog(data)
		"custom":
			dialog = _create_custom_event_dialog(data)
		_:
			dialog = _create_default_alert_dialog(data)
	_add_dialog(dialog)


func _add_dialog(dialog: Window):
	ui_layer.add_child(dialog)
	active_dialogs.append(dialog)
	_setup_middle_click_close(dialog)
	dialog.close_requested.connect(func():
		if is_instance_valid(dialog):
			dialog.queue_free()
	)
	dialog.tree_exited.connect(func():
		active_dialogs.erase(dialog)
	)
	dialog.popup_centered()


func _setup_middle_click_close(dialog: Window):
	dialog.window_input.connect(func(event: InputEvent):
		if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_MIDDLE and event.pressed:
			if is_instance_valid(dialog):
				dialog.queue_free()
	)
	dialog.set_process_input(true)
	var timer = Timer.new()
	timer.wait_time = 0.05
	timer.autostart = true
	timer.timeout.connect(func():
		if not is_instance_valid(dialog):
			return
		if dialog.visible and Input.is_mouse_button_pressed(MOUSE_BUTTON_MIDDLE):
			var mp = dialog.get_mouse_position()
			if Rect2(Vector2.ZERO, Vector2(dialog.size)).has_point(mp):
				dialog.queue_free()
	)
	dialog.add_child(timer)


func _create_default_alert_dialog(data: Dictionary) -> Window:
	var dialog = Window.new()
	dialog.title = _get_default_title(data)
	dialog.min_size = Vector2i(320, 140)
	dialog.size = Vector2i(340, 160)

	var margin = MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 10)
	margin.add_theme_constant_override("margin_right", 10)
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_bottom", 10)
	dialog.add_child(margin)

	var body = VBoxContainer.new()
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 8)
	margin.add_child(body)

	var c1 = data.get("c1")
	var c2 = data.get("c2")
	if c1 or c2:
		var flags = HBoxContainer.new()
		flags.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		if c1:
			flags.add_child(_create_flag(c1.country_name, c1.ideology_name))
			var spacer = Control.new()
			spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			flags.add_child(spacer)
		if c2:
			if not c1:
				var spacer_left = Control.new()
				spacer_left.size_flags_horizontal = Control.SIZE_EXPAND_FILL
				flags.add_child(spacer_left)
			flags.add_child(_create_flag(c2.country_name, c2.ideology_name))
		body.add_child(flags)

	var label = Label.new()
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.size_flags_vertical = Control.SIZE_EXPAND_FILL
	label.text = _get_default_text(data)
	body.add_child(label)

	var ok_button = Button.new()
	ok_button.text = "Ok"
	ok_button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	ok_button.pressed.connect(func(): dialog.queue_free())
	body.add_child(ok_button)

	dialog.close_requested.connect(func(): dialog.queue_free())
	return dialog


func _get_default_title(data: Dictionary) -> String:
	match data.get("event", "event"):
		"war":
			return "War Declared"
		"capitulated":
			return "Capitulation"
		"game_over":
			return "Game Over"
		_:
			return "Alert"


func _get_default_text(data: Dictionary) -> String:
	var c1 = data.get("c1")
	var c2 = data.get("c2")
	var custom_text = data.get("text", "")
	if custom_text != "":
		return custom_text

	match data.get("event", "event"):
		"war":
			if c1 and c2:
				return "%s has declared war on %s!" % [
					c1.country_name.capitalize(),
					c2.country_name.capitalize()
				]
		"capitulated":
			if c1:
				return "%s has capitulated." % [c1.country_name.capitalize()]
		"game_over":
			return "Game Over"
		var type:
			return str(type)
	return ""


func _create_custom_event_dialog(data: Dictionary) -> Window:
	var dialog = EventDialog.new()
	dialog.title = str(data.get("title", "Event"))
	dialog.min_size = Vector2(420, 0)

	var margin = MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 10)
	margin.add_theme_constant_override("margin_right", 10)
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_bottom", 10)
	dialog.add_child(margin)

	var content_list = VBoxContainer.new()
	content_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content_list.add_theme_constant_override("separation", 10)
	margin.add_child(content_list)

	if data.has("contents") and data["contents"] is Array:
		for element in data["contents"]:
			var count_before = content_list.get_child_count()
			InterpreterManager.get_element(element, content_list, CountryManager.player_country)
			var count_after = content_list.get_child_count()
			if count_after > count_before:
				var new_node = content_list.get_child(count_after - 1)
				_connect_buttons_to_close(new_node, dialog)

	dialog.content_root = content_list
	return dialog


func _create_super_event_dialog(data: Dictionary) -> Window:
	var dialog = Window.new()
	dialog.title = "Super Event"
	dialog.size = Vector2i(440, 320)
	dialog.min_size = Vector2i(440, 320)
	dialog.unresizable = false

	var panel = PanelContainer.new()
	panel.set_anchors_preset(Control.PRESET_FULL_RECT)
	dialog.add_child(panel)

	var root = VBoxContainer.new()
	root.add_theme_constant_override("separation", 8)
	panel.add_child(root)

	var background_rect = TextureRect.new()
	background_rect.custom_minimum_size = Vector2(0, 200)
	background_rect.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	background_rect.size_flags_vertical = Control.SIZE_EXPAND_FILL
	background_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	background_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	if data.has("background"):
		var path = "res://assets/superevents/" + str(data["background"]) + ".png"
		if FileAccess.file_exists(path) or ResourceLoader.exists(path):
			background_rect.texture = load(path)
		else:
			push_warning("SuperEvent: Background image not found at %s" % path)
	root.add_child(background_rect)

	var desc_label = Label.new()
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	desc_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	desc_label.text = str(data.get("desc", ""))
	root.add_child(desc_label)

	var button = Button.new()
	button.text = str(data.get("button", "Ok"))
	button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	button.pressed.connect(func(): dialog.queue_free())
	root.add_child(button)

	dialog.close_requested.connect(func(): dialog.queue_free())

	if data.has("music"):
		var base_path = "res://assets/music/superevents/" + str(data["music"])
		if FileAccess.file_exists(base_path + ".mp3"):
			MusicManager.play_custom_file(base_path + ".mp3")
		elif FileAccess.file_exists(base_path + ".ogg"):
			MusicManager.play_custom_file(base_path + ".ogg")
	else:
		MusicManager.play_sfx(MusicManager.SFX.POPUP)

	return dialog


func _connect_buttons_to_close(node: Node, dialog: Window):
	if node is Button:
		node.pressed.connect(func():
			if is_instance_valid(dialog):
				dialog.queue_free()
		)
	for child in node.get_children():
		_connect_buttons_to_close(child, dialog)


func _create_flag(country: String, ideology: String = "") -> TextureRect:
	var tr = TextureRect.new()
	tr.texture = TroopManager.get_flag(country, ideology)
	tr.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	tr.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	tr.custom_minimum_size = Vector2(50, 25)
	return tr


# SUPER EVENT MIGRATED LOGIC
func load_super_events(path: String):
	super_events.clear()
	var json: Variant = MapData.LoadJSON(path)
	if json is Dictionary:
		for event_data in json.get("events", []):
			if event_data.has("cause") and not event_data["cause"].is_empty():
				super_events.append(event_data)


func check_super_events():
	var remove: PackedInt32Array = []
	var index_cur: int = 0
	for event in super_events:
		if InterpreterManager.get_function(event.get("cause", {})):
			_add_dialog(_create_super_event_dialog(event))
			remove.append(index_cur)
		index_cur += 1
	remove.reverse()
	for index in remove:
		super_events.remove_at(index)


func schedule_event(days: int, data: Dictionary):
	if !GameState.current_world or !GameState.current_world.clock:
		return
	var target_time = GameState.current_world.clock.get_absolute_days() + days
	scheduled_events.append({
		"trigger_time": target_time,
		"event_data": data
	})


func _on_day_passed():
	if !GameState.current_world or !GameState.current_world.clock:
		return
	var current = GameState.current_world.clock.get_absolute_days()
	var to_trigger = []

	for i in range(scheduled_events.size() - 1, -1, -1):
		if current >= scheduled_events[i]["trigger_time"]:
			to_trigger.append(scheduled_events[i]["event_data"])
			scheduled_events.remove_at(i)

	for ev in to_trigger:
		show_alert(ev)


func save_events() -> Array:
	return scheduled_events.duplicate(true)


func load_events(data: Array):
	scheduled_events = data.duplicate(true)


class EventDialog extends Window:
	var content_root: VBoxContainer

	func _ready():
		close_requested.connect(queue_free)

	func _process(_delta):
		if content_root and is_instance_valid(content_root):
			InterpreterManager.refresh_buttons(content_root)
