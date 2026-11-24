extends Node

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
		var clock := GameState.current_world.clock

		if a_event.is_action_pressed("deselect_troops"):
			settings.visible = !settings.visible
			match SceneSwitcher._current_type:
				SceneSwitcher.Type.WORLD:
					if !TroopManager.troop_selection.selected_troops.is_empty():
						TroopManager.troop_selection.deselect_all()
					elif GameState.game_ui.is_open:
						MapManager.close_sidemenu.emit()
					else:
						pass
						# get_tree().root.find_child("Menu", true, false).toggle_menu()

		if a_event.is_action_pressed("open_menu"):
			if SceneSwitcher._current_type != SceneSwitcher.Type.EDITOR:
				if GameState.interactionType != GameState.InteractionType.SELECT_COUNTRY:
					# NOTE(soi): This swtiches the sidemenu while its closing instead of swithcing after its out of view
					GameState.game_ui.ChangeContextCategory(
						GameState.game_ui.Context.PLAYER,
						GameState.game_ui.Category.GENERAL,
					)
					GameState.game_ui.selected_country = CountryManager.player_country
					GameState.game_ui.DoUpdateSidemenuVisuals()
				GameState.game_ui.ToggleSideMenu()

		# --- 2. MAP MODE CYCLING (Independent of Menu) ---
		if a_event.is_action_pressed("cycle_map_mode"): _cycle_map_mode(a_event.is_shift_pressed())

		if GameState.current_world && SceneSwitcher._current_type == SceneSwitcher.Type.WORLD:
			if a_event.is_action_pressed("pause_game"): clock.toggle_pause()
			elif a_event.is_action_pressed("increase_speed"): clock.increase_speed()
			elif a_event.is_action_pressed("decrease_speed"): clock.decrease_speed()


func _cycle_map_mode(shift: bool) -> void:
	MapManager.CycleMapMode(shift)

func _is_mouse_over_ui() -> bool:
	return get_viewport().gui_get_hovered_control() != null

func HandleClickDown(_global_pos: Vector2, _map_sprite: Sprite2D) -> void:
	if _is_mouse_over_ui() or Console.is_visible():
		return

	TroopManager.troop_selection.deselect_all()

func HandleHover(global_pos: Vector2, map_sprite: Sprite2D) -> void:
	if _is_mouse_over_ui() or GameState.in_peace_process:
		MapManager.ClearHover()
		return

	var pid: int = MapManager.GetProvinceAtPos(global_pos, map_sprite)
	MapManager.current_hovered_pid = pid
	if pid == MapManager.last_hovered_pid:
		return

	var highlightColor: Color = MapManager.GetContextualHighlight(pid)
	if GameState.interactionType == GameState.InteractionType.SELECT_COUNTRY:
		MapManager.HoverCountry(pid, highlightColor)
	else:
		MapManager.HoverProvince(pid, highlightColor)

func HandleClick(global_pos: Vector2, map_sprite: Sprite2D) -> void:
	if _is_mouse_over_ui() or Console.is_visible() or GameState.in_peace_process:
		return

	var pid: int = MapManager.GetProvinceWithRadius(global_pos, map_sprite, 5)
	if !MapManager.IsLandProvince(pid):
		MapManager.ClickSea()
		return

	match GameState.interactionType:
		GameState.InteractionType.SELECT_COUNTRY:
			MapManager.SelectCountryAt(pid)
		GameState.InteractionType.DEPLOY_PROVINCE:
			MapManager.DeployAt(pid)
		GameState.InteractionType.BUILD_PROVINCE:
			MapManager.BuildAt(pid)
		_:
			# Prevent menu from spawning when selecting troops (annoying)
			if TroopManager.troop_selection.selected_troops.is_empty():
				MapManager.country_clicked.emit(MapManager.province_objects[pid].GetFunctionalOwner())
