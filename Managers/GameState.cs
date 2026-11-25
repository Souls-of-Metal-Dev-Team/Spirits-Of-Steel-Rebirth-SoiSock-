using Godot;
using Sosr;

namespace Sosr;

public partial class GameState : Node
{
	public static GameState Instance => Autoload.Get<GameState>("GameState");

	public enum InteractionType
	{
		DEFAULT,
		SELECT_COUNTRY,
		BUILD_PROVINCE,
		DEPLOY_PROVINCE,
	}

	public enum IndustryType
	{
		DEFAULT,
		FACTORY,
		PORT,
		INFRASTRUCTURE,
	}

	public World current_world = new World();
	public string current_start = "";
	// var current_scenario_path: String (unused in the GDScript original)

	public Godot.Collections.Dictionary clock_cache = new Godot.Collections.Dictionary();

	// var choosing_deploy_city := false
	// var selectingCountry: bool = true

	public InteractionType interactionType = InteractionType.SELECT_COUNTRY;

	public IndustryType industry_building = IndustryType.FACTORY;
	public bool instabuild = false;

	public GameUI game_ui = null;

	public bool decision_menu_open = false;
	public bool in_peace_process = false;
	public bool lostTerritory = false;

	[Export] public bool showingTooltip = false;
	[Export] public Tooltip tooltip = null;

	public void reset_industry_building()
	{
		industry_building = IndustryType.FACTORY;
		interactionType = InteractionType.DEFAULT;
		MapManager.Instance.ShowCountriesMap();
	}

	public bool is_loading_game = false;
	public string pending_load_save = "";
}
