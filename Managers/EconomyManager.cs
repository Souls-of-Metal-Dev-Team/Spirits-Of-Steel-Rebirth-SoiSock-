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
	public Godot.Collections.Dictionary constructionQueue = new();
	public Godot.Collections.Dictionary buildingFunctions = new();
	public Godot.Collections.Dictionary<string, Godot.Collections.Dictionary> buildingDesigns = new();

	public void initialize(string path)
	{
		var parsed = MapData.Instance.LoadJSON(path);
		if (parsed.VariantType == Variant.Type.Dictionary)
			buildingFunctions = parsed.AsGodotDictionary();
		else
			buildingFunctions = new Godot.Collections.Dictionary();
	}

	public void ProcessEconomyDay()
	{
		var finishedProjects = new List<int>();
		foreach (var pidValue in constructionQueue.Keys)
		{
			var pid = pidValue.AsInt32();
			var project = constructionQueue[pidValue].AsGodotDictionary();
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
					finishedProjects.Add(pid);
					_complete_construction(pid, project);
				}
			}
			else if (country.Get("isPlayer").AsBool())
			{
				//#print("Construction stalled in %d: Need %d money" % [pid, cost])
			}
		}

		foreach (var pid in finishedProjects)
			constructionQueue.Remove(pid);

		var buildingCounts = MapManager.Instance.buildingTypesToCountryCount;
		foreach (var keyValue in buildingCounts.Keys)
		{
			var parts = keyValue.AsString().Split(":");
			var ownerName = parts[0];
			var building = parts[1];
			var template = BuildingTemplate.FromDict(buildingDesigns[ownerName][building].AsGodotDictionary().AsState());
			foreach (var countryValue in buildingCounts[keyValue].AsGodotDictionary().Keys)
			{
				var country = CountryManager.Instance.GetCountry(countryValue.AsString());
				var amount = buildingCounts[keyValue].AsGodotDictionary()[countryValue].AsDouble();
				country.money += template.produces * amount;
			}
		}
	}

	public void LoadBuildings(Godot.Collections.Dictionary aGlobohomo)
	{
		foreach (var countryValue in aGlobohomo.Keys)
		{
			var country = countryValue.AsString();
			buildingDesigns[country] = new Godot.Collections.Dictionary();
			foreach (var templateValue in aGlobohomo[countryValue].AsGodotArray())
			{
				var buildingTemplate = BuildingTemplate.FromDict(templateValue.AsGodotDictionary().AsState());
				buildingDesigns[country][buildingTemplate.name] = VariantState.ToVariant(buildingTemplate.ToDict());
			}
		}
	}

	public void StartInfrastructureConstruction(int aProvinceID, int aTotalDays, float aDailyCost, GodotObject aCountry)
	{
		if (IsSame(aCountry, CountryManager.Instance.playerCountry))
			MusicManager.Instance.PlaySfx(SfxBuild);

		constructionQueue[aProvinceID] = new Godot.Collections.Dictionary
		{
			{ "type", "Infrastructure" },
			{ "index", -1 },
			{ "days", aTotalDays },
			{ "daily_cost", aDailyCost },
			{ "country", aCountry },
		};
		EmitSignal(SignalName.ConstructionStarted, aProvinceID, aCountry);

		if (GameState.Instance.instabuild)
		{
			_complete_construction(aProvinceID, constructionQueue[aProvinceID].AsGodotDictionary());
			constructionQueue.Remove(aProvinceID);
		}
	}

	public void StartConstruction(int pid, string type, int totalDays, float dailyCost, GodotObject country)
	{
		// Set the province enum to BUILDING state immediately
		var province = ProvinceObject(pid);
		var buildings = province.buildings;
		buildings.Add(BuildingData.FromValues(type, BuildingData.BuildingState.CONSTRUCTION));

		if (IsSame(country, CountryManager.Instance.playerCountry))
			MusicManager.Instance.PlaySfx(SfxBuild);

		constructionQueue[pid] = new Godot.Collections.Dictionary
		{
			{ "type", type },
			{ "index", buildings.Count - 1 },
			{ "days", totalDays },
			{ "daily_cost", dailyCost },
			{ "country", country },
		};
		EmitSignal(SignalName.ConstructionStarted, pid, country);

		if (GameState.Instance.instabuild)
		{
			_complete_construction(pid, constructionQueue[pid].AsGodotDictionary());
			constructionQueue.Remove(pid);
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
			CountryManager.Instance.GetCountry(province.country).factoriesAmount += 1;
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

		var projectCountry = project["country"].AsGodotObject();
		if (projectCountry.Get("isPlayer").AsBool())
		{
			//#EventManager.show_alert("economy", country, null, "Construction of %s complete!" % type.capitalize())
			if (GameState.Instance.industryBuilding != GameState.IndustryType.DEFAULT)
				MusicManager.Instance.PlaySfx(SfxClapping);
			MapManager.Instance.ProvinceUpdated();
		}

		// Deferred so callers erase the project from construction_queue first.
		CallDeferred("emit_signal", SignalName.ConstructionFinished, pid, project["country"]);
	}

	public bool IsProvinceBuilding(int pid)
	{
		return constructionQueue.ContainsKey(pid);
	}

	public string GetProgressString(int pid)
	{
		if (constructionQueue.ContainsKey(pid))
		{
			var p = constructionQueue[pid].AsGodotDictionary();
			var type = p["type"].AsString().Capitalize();
			return $"{type}: {p["days"].AsInt32()} days left ({p["dailyCost"].AsInt32()}/day)";
		}
		return "";
	}

	private static Province ProvinceObject(int pid)
		=> MapManager.Instance.provinceObjects[pid];

	private static bool IsSame(GodotObject a, GodotObject b)
	{
		if (ReferenceEquals(a, b))
			return true;
		if (a is null || b is null)
			return false;
		return a.Equals(b); // GodotObject.Equals compares the underlying Godot instance.
	}
}
