using Godot;
using Sosr;
using System.Collections.Generic;

namespace Sosr;

public partial class EconomyManager : Node
{
	public static EconomyManager Instance => Autoload.Get<EconomyManager>("EconomyManager");

	// C# source generators expose these as ConstructionStarted / ConstructionFinished.
	// GDScript call sites use those exact names.
	[Signal]
	public delegate void ConstructionStartedEventHandler(int pid, GodotObject country);

	[Signal]
	public delegate void ConstructionFinishedEventHandler(int pid, GodotObject country);

	// MusicManager.SFX enum order (BUILD = 9, CLAPPING = 10).
	private const int SfxBuild = 9;
	private const int SfxClapping = 10;

	// Format: { province_id: { "type": "factory", "days": 20, "daily_cost": 50, "country": CountryData } }
	public Godot.Collections.Dictionary construction_queue = new();
	public Godot.Collections.Dictionary building_functions = new();
	public Godot.Collections.Dictionary<string, Godot.Collections.Dictionary> building_designs = new();

	public void initialize(string path)
	{
		var parsed = MapData.Instance.LoadJSON(path);
		if (parsed.VariantType == Variant.Type.Dictionary)
			building_functions = parsed.AsGodotDictionary();
		else
			building_functions = new Godot.Collections.Dictionary();
	}

	public void process_economy_day()
	{
		var finished_projects = new List<int>();
		foreach (var pidValue in construction_queue.Keys)
		{
			var pid = pidValue.AsInt32();
			var project = construction_queue[pidValue].AsGodotDictionary();
			var country = project["country"].AsGodotObject();
			var cost = project["daily_cost"].AsDouble();

			// 1. Deduct daily cost if affordable
			if (country.Get("money").AsDouble() >= cost)
			{
				//#if country.is_player:
				//#	print("Spending %d on %s in %d" % [cost, project.get("type", "UNKNOWN"), pid])
				country.Set("money", country.Get("money").AsDouble() - cost);
				project["days"] = project["days"].AsInt32() - 1;

				// 2. Check for completion
				if (project["days"].AsInt32() <= 0)
				{
					finished_projects.Add(pid);
					_complete_construction(pid, project);
				}
			}
			else if (country.Get("is_player").AsBool())
			{
				//#print("Construction stalled in %d: Need %d money" % [pid, cost])
			}
		}

		foreach (var pid in finished_projects)
			construction_queue.Remove(pid);

		var building_counts = MapManager.Instance.buildingTypesToCountryCount;
		foreach (var keyValue in building_counts.Keys)
		{
			var parts = keyValue.AsString().Split(":");
			var owner_name = parts[0];
			var building = parts[1];
			var template = building_designs[owner_name][building].AsGodotObject() as BuildingTemplate;
			foreach (var countryValue in building_counts[keyValue].AsGodotDictionary().Keys)
			{
				var country = CountryManager.Instance.GetCountry(countryValue.AsString());
				var amount = building_counts[keyValue].AsGodotDictionary()[countryValue].AsDouble();
				country.money += template.produces * amount;
			}
		}
	}

	public void LoadBuildings(Godot.Collections.Dictionary a_globohomo)
	{
		foreach (var countryValue in a_globohomo.Keys)
		{
			var country = countryValue.AsString();
			building_designs[country] = new Godot.Collections.Dictionary();
			foreach (var templateValue in a_globohomo[countryValue].AsGodotArray())
			{
				var buildingTemplate = BuildingTemplate.FromDict(templateValue.AsGodotDictionary());
				building_designs[country][buildingTemplate.name] = buildingTemplate;
			}
		}
	}

	public void StartInfrastructureConstruction(int a_provinceID, int a_totalDays, float a_dailyCost, GodotObject a_country)
	{
		if (IsSame(a_country, CountryManager.Instance.player_country))
			MusicManager.Instance.play_sfx(SfxBuild);

		construction_queue[a_provinceID] = new Godot.Collections.Dictionary
		{
			{ "type", "Infrastructure" },
			{ "index", -1 },
			{ "days", a_totalDays },
			{ "daily_cost", a_dailyCost },
			{ "country", a_country },
		};
		EmitSignal(SignalName.ConstructionStarted, a_provinceID, a_country);

		if (GameState.Instance.instabuild)
		{
			_complete_construction(a_provinceID, construction_queue[a_provinceID].AsGodotDictionary());
			construction_queue.Remove(a_provinceID);
		}
	}

	public void start_construction(int pid, string type, int total_days, float daily_cost, GodotObject country)
	{
		// Set the province enum to BUILDING state immediately
		var province = ProvinceObject(pid);
		var buildings = province.buildings;
		buildings.Add(BuildingData.FromValues(type, BuildingData.BuildingState.CONSTRUCTION));

		if (IsSame(country, CountryManager.Instance.player_country))
			MusicManager.Instance.play_sfx(SfxBuild);

		construction_queue[pid] = new Godot.Collections.Dictionary
		{
			{ "type", type },
			{ "index", buildings.Count - 1 },
			{ "days", total_days },
			{ "daily_cost", daily_cost },
			{ "country", country },
		};
		EmitSignal(SignalName.ConstructionStarted, pid, country);

		if (GameState.Instance.instabuild)
		{
			_complete_construction(pid, construction_queue[pid].AsGodotDictionary());
			construction_queue.Remove(pid);
		}
	}

	private void _complete_construction(int pid, Godot.Collections.Dictionary project)
	{
		var province = ProvinceObject(pid);
		var index = project["index"].AsInt32();
		var buildings = province.buildings;

		// Update enum to BUILT state
		if (index != -1 && buildings.Count > index)
		{
			buildings[index].state = BuildingData.BuildingState.FUNCTIONAL;
			CountryManager.Instance.GetCountry(province.country).factories_amount += 1;
		}
		else
		{
			switch (project["type"].AsString())
			{
				case "Infrastructure":
					province.infrastructure += 1;
					break;
			}
		}

		var project_country = project["country"].AsGodotObject();
		if (project_country.Get("is_player").AsBool())
		{
			//#EventManager.show_alert("economy", country, null, "Construction of %s complete!" % type.capitalize())
			if (GameState.Instance.industry_building != GameState.IndustryType.DEFAULT)
				MusicManager.Instance.play_sfx(SfxClapping);
			MapManager.Instance.ProvinceUpdated();
		}

		// Deferred so callers erase the project from construction_queue first.
		CallDeferred("emit_signal", SignalName.ConstructionFinished, pid, project["country"]);
	}

	public bool is_province_building(int pid)
	{
		return construction_queue.ContainsKey(pid);
	}

	public string get_progress_string(int pid)
	{
		if (construction_queue.ContainsKey(pid))
		{
			var p = construction_queue[pid].AsGodotDictionary();
			var type = p["type"].AsString().Capitalize();
			return $"{type}: {p["days"].AsInt32()} days left ({p["daily_cost"].AsInt32()}/day)";
		}
		return "";
	}

	private static Province ProvinceObject(int pid)
		=> MapManager.Instance.province_objects[pid];

	private static bool IsSame(GodotObject a, GodotObject b)
	{
		if (ReferenceEquals(a, b))
			return true;
		if (a is null || b is null)
			return false;
		return a.Equals(b); // GodotObject.Equals compares the underlying Godot instance.
	}
}
