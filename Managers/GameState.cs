using System.Collections.Generic;
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

	public ulong SimSeed
	{
		get => SimRandom.Shared.State;
		set => SimRandom.Shared.Reset(value);
	}

	// Simulation state hub. Managers operate on these collections; rendering reads signals.
	public Dictionary<int, Province> Provinces => ProvinceManager.Instance.Provinces;
	public Godot.Collections.Dictionary Countries => CountryManager.Instance.countries;
	public List<TroopData> Troops => TroopManager.Instance.troops;
	public Dictionary<string, HashSet<string>> Wars => WarManager.Instance.wars;

	public World currentWorld = new World();
	public string currentStart = "";
	// var current_scenario_path: String (unused in the GDScript original)

	public Godot.Collections.Dictionary clockCache = new Godot.Collections.Dictionary();

	// var choosing_deploy_city := false
	// var selectingCountry: bool = true

	public InteractionType interactionType
	{
		get => PlayerController.Instance.InteractionMode;
		set => PlayerController.Instance.InteractionMode = value;
	}

	public IndustryType industryBuilding
	{
		get => PlayerController.Instance.Industry;
		set => PlayerController.Instance.Industry = value;
	}
	public bool instabuild = false;

	public GameUI gameUi = null;

	public bool decisionMenuOpen = false;
	public bool inPeaceProcess = false;
	public bool lostTerritory = false;

	[Export] public bool showingTooltip = false;
	[Export] public Tooltip tooltip = null;

	public void ResetIndustryBuilding()
	{
		industryBuilding = IndustryType.FACTORY;
		interactionType = InteractionType.DEFAULT;
		MapManager.Instance.ShowCountriesMap();
	}

	public bool isLoadingGame = false;
	public string pendingLoadSave = "";
}
