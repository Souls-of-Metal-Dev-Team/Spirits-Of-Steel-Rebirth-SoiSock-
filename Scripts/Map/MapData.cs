using Godot;

namespace Sosr;

// Autoload singleton. No [GlobalClass]: GDScript call sites use `MapData.X`,
// which resolves to the registered autoload instance once it is registered.
// Inherits Node because Godot autoloads must be nodes.
public partial class MapData : Node
{
	public static MapData Instance => GameState.Instance.mapData;

	// NOTE(soi): 0 clue what we could even need these for
	public Image idMapImage;
	public Godot.Collections.Dictionary adjacencyList = new();
	public Godot.Collections.Dictionary provinceCenters = new();

	public Godot.Collections.Dictionary provinceToCountry = new();
	public Godot.Collections.Dictionary countryToProvinces = new();
	public int maxProvinceId;
	public Godot.Collections.Dictionary provinceObjects = new();

	// Keys shared by a scenario's start state and a unified game-state save.
	public Godot.Collections.Array<string> STATE_KEYS = new()
	{
		"clock", "resources", "biomes", "buildings", "provinces",
		"polities", "ideologies", "factions", "wars",
		"original_territories", "scheduled_events", "significant_figures",
	};

	// Start folder of the last LoadMapData, so World.DoSetup can tell when a different start was picked.
	public string loadedStart = "";

	private static Godot.Collections.Dictionary AsDict(Variant value)
	{
		return value.VariantType == Variant.Type.Dictionary
			? value.AsGodotDictionary()
			: new Godot.Collections.Dictionary();
	}

	private static Godot.Collections.Array AsArray(Variant value)
	{
		return value.VariantType == Variant.Type.Array
			? value.AsGodotArray()
			: new Godot.Collections.Array();
	}

	// Shared JSON I/O. All scenario/save JSON reads and writes go through these.
	public Variant LoadJSON(string path)
	{
		if (!FileAccess.FileExists(path))
			return new Variant();

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushError("MapData: Failed to open JSON file: " + path);
			return new Variant();
		}

		var text = file.GetAsText();
		file.Close();
		if (string.IsNullOrEmpty(text))
			return new Variant();

		var parsed = Json.ParseString(text);
		if (parsed.VariantType == Variant.Type.Nil)
			GD.PushError("MapData: Failed to parse JSON: " + path);
		return parsed;
	}

	public bool SaveJSON(string path, Variant data)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PushError("MapData: Failed to write JSON file: " + path + " (error " + FileAccess.GetOpenError() + ")");
			return false;
		}

		file.StoreString(Json.Stringify(data, "\t"));
		file.Close();
		return true;
	}

	// scenario_path may be a folder (res://starts/Name) or the map_data.json file.
	public string ScenarioDirFromPath(string aScenarioPath)
	{
		if (aScenarioPath.EndsWith(".json"))
			return aScenarioPath.GetBaseDir();
		return aScenarioPath.TrimSuffix("/");
	}

	// Build the start state in the same shape as World.SaveGame's save_data.
	public Godot.Collections.Dictionary LoadStartState(string aStartDir)
	{
		var paths = new Godot.Collections.Dictionary
		{
			{ "map_data", $"{aStartDir}/map_data.json" },
			{ "provinces", $"{aStartDir}/provinces.json" },
			{ "scenario_data", $"{aStartDir}/scenario_data.json" },
		};
		foreach (var key in paths.Keys)
		{
			if (!FileAccess.FileExists(paths[key].AsString()))
			{
				GD.PushError("MapData: Start file missing: " + paths[key].AsString());
				return new Godot.Collections.Dictionary();
			}
		}

		var mapData = AsDict(LoadJSON(paths["map_data"].AsString()));
		var provinces = AsDict(LoadJSON(paths["provinces"].AsString()));
		var scenarioData = AsDict(LoadJSON(paths["scenario_data"].AsString()));

		return VariantState.ToVariant(SaveService.BuildStartState(
			mapData.AsState(), provinces.AsState(), scenarioData.AsState()));
	}

	// Resolve a save file into a full game state. Diff saves are merged with their start.
	public Godot.Collections.Dictionary LoadSaveState(string aSavePath)
	{
		var saveJson = LoadJSON(aSavePath);
		if (saveJson.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError("MapData: Failed to parse save: " + aSavePath);
			return new Godot.Collections.Dictionary();
		}

		var saveDict = saveJson.AsGodotDictionary();
		if (!saveDict.ContainsKey("diff"))
		{
			// Legacy full-state save.
			return saveDict;
		}

		var scenarioPath = saveDict.ContainsKey("scenario_path")
			? saveDict["scenario_path"].AsString()
			: "";
		if (scenarioPath == "")
		{
			GD.PushError("MapData: Diff save missing scenario_path: " + aSavePath);
			return new Godot.Collections.Dictionary();
		}

		if (saveDict["diff"].VariantType != Variant.Type.Dictionary)
		{
			GD.PushError("MapData: Invalid diff in save: " + aSavePath);
			return new Godot.Collections.Dictionary();
		}

		var startState = LoadStartState(ScenarioDirFromPath(scenarioPath));
		if (startState.Count == 0)
			return new Godot.Collections.Dictionary();

		return VariantState.ToVariant(SaveService.ApplySave(startState.AsState(), saveDict.AsState()));
	}

	public void SaveMapData(string path)
	{
		var figDicts = FigureManager.Instance.Serialize();

		var exportMapData = new Godot.Collections.Dictionary
		{
			{ "ideologies", IdeologyManager.Instance.ideologies },
			{ "buildings", MapManager.Instance.SaveBuildingData() },
			{ "resources", MapManager.Instance.SaveResourcesData() },
			{ "biomes", MapManager.Instance.SaveBiomeData() },
			{ "clock", ClockDict() },
		};

		var exportScenario = new Godot.Collections.Dictionary
		{
			{ "significant_figures", figDicts },
			{ "polities", CountryManager.Instance.SaveCountries() },
			{ "factions", FactionManager.Instance.SaveFactions() },
		};

		var exportProvince = MapManager.Instance.SaveProvinces();

		SaveJSON(path + "map_data.json", exportMapData);
		SaveJSON(path + "provinces.json", exportProvince);
		SaveJSON(path + "scenario_data.json", exportScenario);
	}

	private static Godot.Collections.Dictionary ClockDict()
	{
		var clock = GameState.Instance.currentWorld?.clock;
		return GodotObject.IsInstanceValid(clock)
			? clock.ToDict()
			: new Godot.Collections.Dictionary();
	}

	public void LoadMapData(string aStartPath, Godot.Collections.Array aPercent = null, Godot.Collections.Dictionary aStateOverride = null)
	{
		aPercent ??= new Godot.Collections.Array { 0 };
		aStateOverride ??= new Godot.Collections.Dictionary();
		loadedStart = aStartPath;
		if (aStateOverride.ContainsKey("sim_seed") &&
			ulong.TryParse(aStateOverride["sim_seed"].AsString(), out var simSeed))
			GameState.Instance.SimSeed = simSeed;

		var mapData = AsDict(LoadJSON($"{aStartPath}/map_data.json"));
		var provinces = AsDict(LoadJSON($"{aStartPath}/provinces.json"));
		var scenarioData = AsDict(LoadJSON($"{aStartPath}/scenario_data.json"));

		// Loading a save: keep the start's static data (regions, decisions, flags, ...)
		// but replace every dynamic section with the saved state.
		if (aStateOverride.Count > 0)
		{
			foreach (var key in new Godot.Collections.Array<string>
				{
					"biomes", "resources", "buildings", "ideologies", "clock", "wars", "original_territories",
				})
			{
				if (aStateOverride.ContainsKey(key))
					mapData[key] = aStateOverride[key];
			}
			if (aStateOverride.ContainsKey("provinces"))
				provinces = AsDict(aStateOverride["provinces"]);
			if (aStateOverride.ContainsKey("polities"))
				scenarioData["polities"] = aStateOverride["polities"];
			if (aStateOverride.ContainsKey("factions"))
				scenarioData["factions"] = aStateOverride["factions"];
			if (aStateOverride.ContainsKey("significant_figures"))
				scenarioData["significant_figures"] = aStateOverride["significant_figures"];
		}

		// print(map_data)
		// print(provinces)
		// print(scenario_data)

		var regionsTex = GD.Load<Texture2D>($"{aStartPath}/regions.png");
		GD.Print($"{aStartPath}/regions.png");

		// GameState.current_start = a_text
		aPercent[0] = 0.01;
		BuildingManager.Instance.initialize(aStartPath + "/building_functions.json");
		aPercent[0] = 0.02;
		PlansManager.Instance.LoadPlansFromPath(aStartPath + "/plans.json");
		aPercent[0] = 0.06;
		EventManager.Instance.LoadSuperEvents(aStartPath + "/superevents.json");
		aPercent[0] = 0.18;

		Variant bData = new Godot.Collections.Array();
		if (mapData.ContainsKey("biomes"))
			bData = mapData["biomes"];
		MapManager.Instance.LoadBiomes(bData.AsGodotArray());

		Variant rData = new Godot.Collections.Array();
		if (mapData.ContainsKey("resources"))
			rData = mapData["resources"];
		MapManager.Instance.LoadResources(rData.AsGodotArray());

		Variant buildingData = new Godot.Collections.Dictionary();
		if (mapData.ContainsKey("buildings"))
			buildingData = mapData["buildings"];
		if (buildingData.VariantType != Variant.Type.Dictionary)
			buildingData = new Godot.Collections.Dictionary();
		BuildingManager.Instance.LoadBuildings(buildingData.AsGodotDictionary());

		Variant ideologyData = new Godot.Collections.Dictionary();
		if (mapData.ContainsKey("ideologies"))
			ideologyData = mapData["ideologies"];
		IdeologyManager.Instance.Initialize(ideologyData.AsGodotDictionary());
		aPercent[0] = 0.3;

		DecisionManager.Instance.LoadDecisionsFromPath($"{aStartPath}/decisions/");
		aPercent[0] = 0.2;
		TroopManager.Instance.SetCustomFlagPath($"{aStartPath}/flags/");
		aPercent[0] = 0.12;
		Variant polities = new Godot.Collections.Array();
		if (scenarioData.ContainsKey("polities"))
			polities = scenarioData["polities"];
		CountryManager.Instance.InitializeCountries(polities.AsGodotArray());
		aPercent[0] = 0.75;
		MapManager.Instance.LoadProvinces((CompressedTexture2D)regionsTex, provinces, aPercent);
		aPercent[0] = 0.6;

		// Restore Clock
		var clock = GameState.Instance.currentWorld?.clock;
		if (GodotObject.IsInstanceValid(clock))
		{
			if (mapData.ContainsKey("clock"))
			{
				clock.Call("FromDict", mapData["clock"]);
			}
			else
			{
				// Default start date for scenarios/new games
				clock.Call("FromDict", new Godot.Collections.Dictionary
				{
					{ "hour", 0 },
					{ "date", new Godot.Collections.Dictionary { { "year", 2010 }, { "month", 1 }, { "day", 1 } } },
				});
			}
		}

		Variant wars = new Godot.Collections.Array();
		if (mapData.ContainsKey("wars"))
			wars = mapData["wars"];
		WarManager.Instance.LoadWars(wars.AsGodotArray());

		Variant originalTerritories = new Godot.Collections.Dictionary();
		if (mapData.ContainsKey("original_territories"))
			originalTerritories = mapData["original_territories"];
		WarManager.Instance.LoadOriginalTerritories(originalTerritories.AsGodotDictionary());
		WarManager.Instance.CheckForNewBattles();

		MapManager.Instance.BuildLookupTexture();
		aPercent[0] = 0.85;
		Variant factions = new Godot.Collections.Array();
		if (scenarioData.ContainsKey("factions"))
			factions = scenarioData["factions"];
		FactionManager.Instance.Initialize(factions.AsGodotArray());
		aPercent[0] = 0.9;

		Variant significantFigureData = new Godot.Collections.Array();
		if (scenarioData.ContainsKey("significant_figures"))
			significantFigureData = scenarioData["significant_figures"];

		FigureManager.Instance.Load(AsArray(significantFigureData));
		CountryManager.Instance.GenerateMissingLeaders();
		CountryManager.Instance.CleanupEmptyCountries();

		if (aStateOverride.ContainsKey("scheduled_events"))
			EventManager.Instance.LoadEvents(aStateOverride["scheduled_events"].AsGodotArray());

		Autoload.Get("Console")?.Call("add_command_autocomplete_list", "tag", new Godot.Collections.Array(CountryManager.Instance.countries.Keys));
	}
}
