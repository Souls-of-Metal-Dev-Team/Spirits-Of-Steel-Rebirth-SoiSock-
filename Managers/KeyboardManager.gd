extends Node

# Escape pressed with no troops selected. GameUI closes the side menu if open.
signal sidemenuCloseRequested
# Open-menu key pressed. GameUI toggles the side menu.
signal sidemenuToggleRequested

# Use the same names as your MapManager functions for clarity
var settings: CanvasLayer

func _ready() -> void:
	settings = $/root/Main/SettingsLayer

# NOTE(soi): What was this even for
# func OnWorldLoad() -> void:
# 	settings = get_tree().root.find_child("Settings", true, false)
# 	print(settings)

func _unhandled_input(
	a_event: InputEvent
) -> void:
	if Console.is_visible():
		return
	if a_event is InputEventKey:
		var clock = GameState.current_world.clock

		if a_event.is_action_pressed("deselect_troops"):
			settings.visible = !settings.visible
			match SceneSwitcher._current_type:
				SceneSwitcher.Type.WORLD:
					if !TroopManager.troop_selection.selected_troops.is_empty():
						TroopManager.troop_selection.deselect_all()
					else:
						sidemenuCloseRequested.emit()
						# get_tree().root.find_child("Menu", true, false).toggle_menu()

		if a_event.is_action_pressed("open_menu"):
			if SceneSwitcher._current_type != SceneSwitcher.Type.EDITOR:
				sidemenuToggleRequested.emit()

		# --- 2. MAP MODE CYCLING (Independent of Menu) ---
		if a_event.is_action_pressed("cycle_map_mode"): _cycle_map_mode(a_event.is_shift_pressed())

		if GameState.current_world && SceneSwitcher._current_type == SceneSwitcher.Type.WORLD:
			if a_event.is_action_pressed("pause_game"): clock.toggle_pause()
			elif a_event.is_action_pressed("increase_speed"): clock.increase_speed()
			elif a_event.is_action_pressed("decrease_speed"): clock.decrease_speed()


func _cycle_map_mode(shift: bool) -> void:
	MapManager.CycleMapMode(shift)
