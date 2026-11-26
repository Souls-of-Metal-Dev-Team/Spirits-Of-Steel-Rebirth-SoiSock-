using System.Collections.Generic;
using System.Linq;
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
	public Dictionary<string, ImportantFigure> Figures => figureManager.figures;

	// All managers are children of GameState. Other code reaches them through
	// the static Instance forwarders on each manager class. Construct them in
	// _Ready, not in field initializers: GameState.Instance is null until the
	// autoload node is in the tree.
	public FigureManager figureManager;
	public MapData mapData;
	public DivisionTemplates divisionTemplates;
	public MapManager mapManager;
	public ProvinceManager provinceManager;
	public PlayerController playerController;
	public CountryManager countryManager;
	public ActionDispatcher actionDispatcher;
	public WarManager warManager;
	public TroopManager troopManager;
	public MusicManager musicManager;
	public KeyboardManager keyboardManager;
	public ConsoleManager consoleManager;
	public DecisionManager decisionManager;
	public BuildingManager buildingManager;
	public InterpreterManager interpreterManager;
	public FactionManager factionManager;
	public IdeologyManager ideologyManager;
	public SceneManager sceneManager;
	public PlansManager plansManager;
	public EventManager eventManager;

	// Settings live here. Init is deferred so MusicManager has scanned radio folders first.
	public string SETTINGS_FILE = "res://settings.json";
	public ConfigFile settings = new ConfigFile();

	public override void _Ready()
	{
		// Create and add in dependency order. Godot calls each child's _Ready at AddChild.
		mapData = new MapData(); AddChild(mapData);
		divisionTemplates = new DivisionTemplates(); AddChild(divisionTemplates);
		mapManager = new MapManager(); AddChild(mapManager);
		provinceManager = new ProvinceManager(); AddChild(provinceManager);
		figureManager = new FigureManager(); AddChild(figureManager);
		playerController = new PlayerController(); AddChild(playerController);
		countryManager = new CountryManager(); AddChild(countryManager);
		actionDispatcher = new ActionDispatcher(); AddChild(actionDispatcher);
		warManager = new WarManager(); AddChild(warManager);
		troopManager = new TroopManager(); AddChild(troopManager);
		musicManager = new MusicManager(); AddChild(musicManager);
		keyboardManager = new KeyboardManager(); AddChild(keyboardManager);
		consoleManager = new ConsoleManager(); AddChild(consoleManager);
		decisionManager = new DecisionManager(); AddChild(decisionManager);
		buildingManager = new BuildingManager(); AddChild(buildingManager);
		interpreterManager = new InterpreterManager(); AddChild(interpreterManager);
		factionManager = new FactionManager(); AddChild(factionManager);
		ideologyManager = new IdeologyManager(); AddChild(ideologyManager);
		sceneManager = new SceneManager(); AddChild(sceneManager);
		plansManager = new PlansManager(); AddChild(plansManager);
		eventManager = new EventManager(); AddChild(eventManager);
		InitializeSettings();
	}

	public void InitializeSettings()
	{
		settings.SetValue("Developer Options", "debug_mode", false);
		settings.SetValue("Sound", "radios", MusicManager.Instance.radios);
		settings.SetValue("Sound", "sfx_volume", 0.1);
		settings.SetValue("Sound", "music_volume", 1.0);
		settings.SetValue("Graphics", "scanlines", 0.0);
		settings.SetValue("Graphics", "vignette_lower", 0.0);
		settings.SetValue("Graphics", "vignette_upper", 0.0);
		settings.SetValue("Graphics", "map_effects", 1.0);
		settings.SetValue("Graphics", "provinceBorderThickness", 0.5);
		settings.SetValue("Graphics", "province_borders", 0.8);
		settings.SetValue("Graphics", "ui_upper", 0.8);
		settings.SetValue("Graphics", "ui_lower", 0.8);
		settings.SetValue("Graphics", "ui_dirt", 0.8);
		settings.SetValue("Graphics", "daynight_contrast", 0.01);
		settings.SetValue("Graphics", "daynight_smoothness", 0.01);
		settings.SetValue("Graphics", "clouds", 0.01);
		settings.SetValue("Graphics", "clouds_edgeness", 0.01);
		LoadSettings();
	}

	public void LoadSettings()
	{
		if (FileAccess.FileExists(SETTINGS_FILE))
			settings.Load(SETTINGS_FILE);
		else
			settings.Save(SETTINGS_FILE);
	}

	public void ApplySettings(ConfigFile aSettings)
	{
		var music = MusicManager.Instance;
		music.SetSfxVolume(aSettings.GetValue("Sound", "sfx_volume").AsSingle());
		music.SetMusicVolume(aSettings.GetValue("Sound", "music_volume").AsSingle());
		var radios = aSettings.GetValue("Sound", "radios");
		if (radios.VariantType == Variant.Type.Array)
			music.radios = new Godot.Collections.Array<string>(radios.AsGodotArray().Select(r => r.AsString()));

		var world = currentWorld;
		if (world == null || !GodotObject.IsInstanceValid(world))
			return;
		var sprite = world.mapSprite;
		if (sprite == null || !GodotObject.IsInstanceValid(sprite))
			return;
		if (sprite.Material is not ShaderMaterial mat)
			return;

		var shaderMap = new Godot.Collections.Dictionary<string, string>
		{
			{ "scanlines", "scanlines" },
			{ "upper", "vignette_upper" },
			{ "lower", "vignette_lower" },
			{ "toggle", "map_effects" },
			{ "contrast", "daynight_contrast" },
			{ "smoothness", "daynight_smoothness" },
			{ "clouds", "clouds" },
			{ "edgeness", "clouds_edgeness" },
			{ "BorderThickness", "provinceBorderThickness" },
			{ "internal_border_darkness", "province_borders" },
		};
		foreach (var param in shaderMap.Keys)
			mat.SetShaderParameter(param, aSettings.GetValue("Graphics", shaderMap[param]));
	}

	public World currentWorld = new World();
	public string currentStart = "";
	public Godot.Collections.Dictionary clockCache = new Godot.Collections.Dictionary();

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
		industryBuilding = IndustryType.DEFAULT;
		interactionType = InteractionType.DEFAULT;
		MapManager.Instance.ShowCountriesMap();
	}

	public bool isLoadingGame = false;
	public string pendingLoadSave = "";
}
