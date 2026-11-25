using Godot;

namespace Sosr;

// Autoload singleton. No [GlobalClass]: GDScript call sites use `MapData.X`,
// which resolves to the registered autoload instance once it is registered.
// Inherits Node because Godot autoloads must be nodes.
public partial class MapData : Node
{
	public static MapData Instance => Autoload.Get<MapData>("MapData");

	// NOTE(soi): 0 clue what we could even need these for
	public Image id_map_image;
	public Godot.Collections.Dictionary adjacency_list = new();
	public Godot.Collections.Dictionary province_centers = new();

	public Godot.Collections.Dictionary province_to_country = new();
	public Godot.Collections.Dictionary country_to_provinces = new();
	public int max_province_id;
	public Godot.Collections.Dictionary province_objects = new();

	// Keys shared by a scenario's start state and a unified game-state save.
	public Godot.Collections.Array<string> STATE_KEYS = new()
	{
		"clock", "resources", "biomes", "buildings", "provinces",
		"polities", "ideologies", "factions", "wars",
		"original_territories", "scheduled_events",
	};

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
	public string ScenarioDirFromPath(string a_scenario_path)
	{
		if (a_scenario_path.EndsWith(".json"))
			return a_scenario_path.GetBaseDir();
		return a_scenario_path.TrimSuffix("/");
	}

	// Build the start state in the same shape as World.SaveGame's save_data.
	public Godot.Collections.Dictionary LoadStartState(string a_start_dir)
	{
		var paths = new Godot.Collections.Dictionary
		{
			{ "map_data", $"{a_start_dir}/map_data.json" },
			{ "provinces", $"{a_start_dir}/provinces.json" },
			{ "scenario_data", $"{a_start_dir}/scenario_data.json" },
		};
		foreach (var key in paths.Keys)
		{
			if (!FileAccess.FileExists(paths[key].AsString()))
			{
				GD.PushError("MapData: Start file missing: " + paths[key].AsString());
				return new Godot.Collections.Dictionary();
			}
		}

		var map_data = AsDict(LoadJSON(paths["map_data"].AsString()));
		var provinces = AsDict(LoadJSON(paths["provinces"].AsString()));
		var scenario_data = AsDict(LoadJSON(paths["scenario_data"].AsString()));

		var combined = new Godot.Collections.Dictionary();
		MergeInto(combined, map_data, true);
		combined["provinces"] = provinces;
		MergeInto(combined, scenario_data, true);

		var state = new Godot.Collections.Dictionary();
		foreach (var key in STATE_KEYS)
		{
			if (combined.ContainsKey(key))
				state[key] = combined[key];
		}
		return state;
	}

	// Resolve a save file into a full game state. Diff saves are merged with their start.
	public Godot.Collections.Dictionary LoadSaveState(string a_save_path)
	{
		var save_json = LoadJSON(a_save_path);
		if (save_json.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError("MapData: Failed to parse save: " + a_save_path);
			return new Godot.Collections.Dictionary();
		}

		var save_dict = save_json.AsGodotDictionary();
		if (!save_dict.ContainsKey("diff"))
		{
			// Legacy full-state save.
			return save_dict;
		}

		var scenario_path = save_dict.ContainsKey("scenario_path")
			? save_dict["scenario_path"].AsString()
			: "";
		if (scenario_path == "")
		{
			GD.PushError("MapData: Diff save missing scenario_path: " + a_save_path);
			return new Godot.Collections.Dictionary();
		}

		var state_diff = save_dict["diff"];
		if (state_diff.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError("MapData: Invalid diff in save: " + a_save_path);
			return new Godot.Collections.Dictionary();
		}

		var start_state = LoadStartState(ScenarioDirFromPath(scenario_path));
		if (start_state.Count == 0)
			return new Godot.Collections.Dictionary();

		var state = SettingsManager.Instance.merge(start_state, state_diff.AsGodotDictionary());
		state["scenario_path"] = scenario_path;
		return state;
	}

	public void SaveMapData(string path)
	{
		var fig_dicts = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		foreach (var figure in MapManager.Instance.significantFigures.Values)
			fig_dicts.Add(figure.ToDict());

		var export_map_data = new Godot.Collections.Dictionary
		{
			{ "ideologies", IdeologyManager.Instance.ideologies },
			{ "buildings", MapManager.Instance.SaveBuildingData() },
			{ "resources", MapManager.Instance.SaveResourcesData() },
			{ "biomes", MapManager.Instance.SaveBiomeData() },
			{ "clock", ClockDict() },
		};

		var export_scenario = new Godot.Collections.Dictionary
		{
			{ "significant_figures", fig_dicts },
			{ "polities", CountryManager.Instance.SaveCountries() },
			{ "factions", FactionManager.Instance.SaveFactions() },
		};

		var export_province = MapManager.Instance.SaveProvinces();

		SaveJSON(path + "map_data.json", export_map_data);
		SaveJSON(path + "provinces.json", export_province);
		SaveJSON(path + "scenario_data.json", export_scenario);
	}

	private static Godot.Collections.Dictionary ClockDict()
	{
		var clock = GameState.Instance.current_world?.clock;
		return GodotObject.IsInstanceValid(clock)
			? clock.ToDict()
			: new Godot.Collections.Dictionary();
	}

	private static void MergeInto(Godot.Collections.Dictionary target, Godot.Collections.Dictionary source, bool overwrite)
	{
		foreach (var key in source.Keys)
		{
			if (overwrite || !target.ContainsKey(key))
				target[key] = source[key];
		}
	}

	public void LoadMapData(string a_start_path, Godot.Collections.Array a_percent = null, Godot.Collections.Dictionary a_state_override = null)
	{
		a_percent ??= new Godot.Collections.Array { 0 };
		a_state_override ??= new Godot.Collections.Dictionary();

		var map_data = AsDict(LoadJSON($"{a_start_path}/map_data.json"));
		var provinces = AsDict(LoadJSON($"{a_start_path}/provinces.json"));
		var scenario_data = AsDict(LoadJSON($"{a_start_path}/scenario_data.json"));

		// Loading a save: keep the start's static data (regions, decisions, flags, ...)
		// but replace every dynamic section with the saved state.
		if (a_state_override.Count > 0)
		{
			foreach (var key in new Godot.Collections.Array<string>
				{
					"biomes", "resources", "buildings", "ideologies", "clock", "wars", "original_territories",
				})
			{
				if (a_state_override.ContainsKey(key))
					map_data[key] = a_state_override[key];
			}
			if (a_state_override.ContainsKey("provinces"))
				provinces = AsDict(a_state_override["provinces"]);
			if (a_state_override.ContainsKey("polities"))
				scenario_data["polities"] = a_state_override["polities"];
			if (a_state_override.ContainsKey("factions"))
				scenario_data["factions"] = a_state_override["factions"];
		}

		// print(map_data)
		// print(provinces)
		// print(scenario_data)

		var regions_tex = GD.Load<Texture2D>($"{a_start_path}/regions.png");
		GD.Print($"{a_start_path}/regions.png");

		// GameState.current_start = a_text
		a_percent[0] = 0.01;
		EconomyManager.Instance.initialize(a_start_path + "/building_functions.json");
		a_percent[0] = 0.02;
		PlansManager.Instance.load_plans_from_path(a_start_path + "plans.json");
		a_percent[0] = 0.06;
		EventManager.Instance.load_super_events(a_start_path + "superevents.json");
		a_percent[0] = 0.18;

		Variant b_data = new Godot.Collections.Array();
		if (map_data.ContainsKey("biomes"))
			b_data = map_data["biomes"];
		MapManager.Instance.LoadBiomes(b_data.AsGodotArray());

		Variant r_data = new Godot.Collections.Array();
		if (map_data.ContainsKey("resources"))
			r_data = map_data["resources"];
		MapManager.Instance.LoadResources(r_data.AsGodotArray());

		Variant buildingData = new Godot.Collections.Dictionary();
		if (map_data.ContainsKey("buildings"))
			buildingData = map_data["buildings"];
		if (buildingData.VariantType != Variant.Type.Dictionary)
			buildingData = new Godot.Collections.Dictionary();
		EconomyManager.Instance.LoadBuildings(buildingData.AsGodotDictionary());

		Variant ideologyData = new Godot.Collections.Dictionary();
		if (map_data.ContainsKey("ideologies"))
			ideologyData = map_data["ideologies"];
		IdeologyManager.Instance.Initialize(ideologyData.AsGodotDictionary());
		a_percent[0] = 0.3;

		DecisionManager.Instance.load_decisions_from_path($"{a_start_path}/decisions/");
		a_percent[0] = 0.2;
		TroopManager.Instance.set_custom_flag_path($"{a_start_path}/flags/");
		a_percent[0] = 0.12;
		Variant polities = new Godot.Collections.Array();
		if (scenario_data.ContainsKey("polities"))
			polities = scenario_data["polities"];
		CountryManager.Instance.initialize_countries(polities.AsGodotArray());
		a_percent[0] = 0.75;
		MapManager.Instance.LoadProvinces((CompressedTexture2D)regions_tex, provinces, a_percent);
		a_percent[0] = 0.6;

		// Restore Clock
		var clock = GameState.Instance.current_world?.clock;
		if (GodotObject.IsInstanceValid(clock))
		{
			if (map_data.ContainsKey("clock"))
			{
				clock.Call("FromDict", map_data["clock"]);
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
		if (map_data.ContainsKey("wars"))
			wars = map_data["wars"];
		WarManager.Instance.load_wars(wars.AsGodotArray());

		Variant originalTerritories = new Godot.Collections.Dictionary();
		if (map_data.ContainsKey("original_territories"))
			originalTerritories = map_data["original_territories"];
		WarManager.Instance.load_original_territories(originalTerritories.AsGodotDictionary());
		WarManager.Instance.check_for_new_battles();

		MapManager.Instance.BuildLookupTexture();
		a_percent[0] = 0.85;
		Variant factions = new Godot.Collections.Array();
		if (scenario_data.ContainsKey("factions"))
			factions = scenario_data["factions"];
		FactionManager.Instance.Initialize(factions.AsGodotArray());
		a_percent[0] = 0.9;

		Variant significantFigureData = new Godot.Collections.Array();
		if (scenario_data.ContainsKey("significant_figures"))
			significantFigureData = scenario_data["significant_figures"];

		var significantFigures = MapManager.Instance.significantFigures;
		foreach (var figVariant in AsArray(significantFigureData))
		{
			var fig = figVariant.AsGodotDictionary();
			significantFigures[fig["name"].AsString()] = ImportantFigure.FromDict(fig);
		}
		CountryManager.Instance.generate_missing_leaders();
		CountryManager.Instance.cleanup_empty_countries();

		if (a_state_override.ContainsKey("scheduled_events"))
			EventManager.Instance.load_events(a_state_override["scheduled_events"].AsGodotArray());

		Autoload.Get("Console")?.Call("add_command_autocomplete_list", "tag", new Godot.Collections.Array(CountryManager.Instance.countries.Keys));
	}
}
