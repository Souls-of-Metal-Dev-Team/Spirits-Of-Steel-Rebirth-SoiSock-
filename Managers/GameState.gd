extends Node

enum InteractionType {
	DEFAULT,
	SELECT_COUNTRY,
	BUILD_PROVINCE,
	DEPLOY_PROVINCE,
}

enum IndustryType {
	DEFAULT,
	FACTORY,
	PORT,
	INFRASTRUCTURE,
}

var current_world: World = World.new()
var current_start: String
# var current_scenario_path: String

var clock_cache: Dictionary

# var choosing_deploy_city := false
# var selectingCountry: bool = true

var interactionType := InteractionType.SELECT_COUNTRY


var industry_building := IndustryType.FACTORY
var instabuild: bool = false

var game_ui: GameUI

var decision_menu_open: bool = false
var in_peace_process: bool = false
var lostTerritory: bool = false

@export var showingTooltip: bool = false
@export var tooltip: Tooltip

func reset_industry_building():
	industry_building = IndustryType.FACTORY
	interactionType = InteractionType.DEFAULT
	MapManager.ShowCountriesMap()

var is_loading_game := false
var pending_load_save: String = ""
