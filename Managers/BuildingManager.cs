using Godot;
using Sosr;
using System.Collections.Generic;

namespace Sosr;

// Handles building placement requests and the construction queue. Building income
// payout stays in the daily economy tick on CountryManager.
public partial class BuildingManager : Node
{
	public static BuildingManager Instance => GameState.Instance.buildingManager;

	[Signal]
	public delegate void ConstructionStartedEventHandler(int pid, GodotObject country);

	[Signal]
	public delegate void ConstructionFinishedEventHandler(int pid, GodotObject country);

	private const int SfxBuild = 9;
	private const int SfxClapping = 10;

	public Godot.Collections.Dictionary constructionQueue = new();
	public Godot.Collections.Dictionary buildingFunctions = new();
	public Godot.Collections.Dictionary<string, Godot.Collections.Dictionary> buildingDesigns = new();

	// Request from a controller: validate province capacity, then enqueue work.
	public bool PlaceBuilding(int pid, string countryName, int type)
	{
		var province = MapManager.Instance.provinceObjects[pid];
		var country = CountryManager.Instance.GetCountry(countryName);
		switch (type)
		{
			case 1: // FACTORY
				if (province.buildings.Count >= 4)
					return false;
				StartConstruction(pid, "Factory", 10, 150.0f, country);
				break;
			case 2: // PORT
				var allPorts = true;
				foreach (var building in province.buildings)
				{
					if (building.type != "Port")
					{
						allPorts = false;
						break;
					}
				}
				if (province.buildings.Count >= 4 && allPorts)
					return false;
				if (System.Array.IndexOf(MapManager.Instance.GetProvincesNearSea(countryName), pid) >= 0)
					StartConstruction(pid, "Port", 10, 150.0f, country);
				else
				{
					GD.Print("Action Failed: Port must be on a coast!");
					return false;
				}
				break;
			case 3: // INFRASTRUCTURE
				if (province.infrastructure >= province.maxInfrastructure)
					return false;
				StartInfrastructureConstruction(pid, 10, 150.0f, country);
				break;
		}
		return true;
	}

	public void initialize(string path)
	{
		var parsed = MapData.Instance.LoadJSON(path);
		buildingFunctions = parsed.VariantType == Variant.Type.Dictionary
			? parsed.AsGodotDictionary()
			: new Godot.Collections.Dictionary();
	}

	public void ProcessConstructionDay()
	{
		var finishedProjects = new List<int>();
		foreach (var pidValue in constructionQueue.Keys)
		{
			var pid = pidValue.AsInt32();
			var project = constructionQueue[pidValue].AsGodotDictionary();
			var country = project["country"].AsGodotObject();
			var cost = project["daily_cost"].AsDouble();
			if (country.Get("money").AsDouble() >= cost)
			{
				country.Set("money", country.Get("money").AsDouble() - cost);
				project["days"] = project["days"].AsInt32() - 1;
				if (project["days"].AsInt32() <= 0)
				{
					finishedProjects.Add(pid);
					_complete_construction(pid, project);
				}
			}
		}
		foreach (var pid in finishedProjects)
			constructionQueue.Remove(pid);
	}

	public void LoadBuildings(Godot.Collections.Dictionary aGlobohomo)
	{
		foreach (var countryValue in aGlobohomo.Keys)
		{
			var country = countryValue.AsString();
			buildingDesigns[country] = new Godot.Collections.Dictionary();
			foreach (var templateValue in aGlobohomo[countryValue].AsGodotArray())
			{
				var template = BuildingTemplate.FromDict(templateValue.AsGodotDictionary().AsState());
				buildingDesigns[country][template.name] = VariantState.ToVariant(template.ToDict());
			}
		}
	}

	public void StartInfrastructureConstruction(int aProvinceID, int aTotalDays, float aDailyCost, GodotObject aCountry)
	{
		if (IsSame(aCountry, CountryManager.Instance.playerCountry))
			MusicManager.Instance.PlaySfx(SfxBuild);

		constructionQueue[aProvinceID] = new Godot.Collections.Dictionary
		{
			{ "type", "Infrastructure" }, { "index", -1 }, { "days", aTotalDays },
			{ "daily_cost", aDailyCost }, { "country", aCountry },
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
		var buildings = ProvinceObject(pid).buildings;
		buildings.Add(BuildingData.FromValues(type, BuildingData.BuildingState.CONSTRUCTION));
		if (IsSame(country, CountryManager.Instance.playerCountry))
			MusicManager.Instance.PlaySfx(SfxBuild);

		constructionQueue[pid] = new Godot.Collections.Dictionary
		{
			{ "type", type }, { "index", buildings.Count - 1 }, { "days", totalDays },
			{ "daily_cost", dailyCost }, { "country", country },
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
		if (index != -1 && province.buildings.Count > index)
		{
			province.buildings[index].state = BuildingData.BuildingState.FUNCTIONAL;
			CountryManager.Instance.GetCountry(province.country).factoriesAmount += 1;
		}
		else if (project["type"].AsString() == "Infrastructure")
			province.infrastructure += 1;

		if (project["country"].AsGodotObject().Get("isPlayer").AsBool())
		{
			if (GameState.Instance.industryBuilding != GameState.IndustryType.DEFAULT)
				MusicManager.Instance.PlaySfx(SfxClapping);
			MapManager.Instance.ProvinceUpdated();
		}

		// Deferred so callers erase the project from the queue first.
		CallDeferred("emit_signal", SignalName.ConstructionFinished, pid, project["country"]);
	}

	public bool IsProvinceBuilding(int pid) => constructionQueue.ContainsKey(pid);

	public string GetProgressString(int pid)
	{
		if (!constructionQueue.TryGetValue(pid, out var value))
			return "";
		var project = value.AsGodotDictionary();
		return $"{project["type"].AsString()} {project["days"].AsInt32()}d";
	}

	private static Province ProvinceObject(int pid) => MapManager.Instance.provinceObjects[pid];

	private static bool IsSame(GodotObject a, GodotObject b)
	{
		if (ReferenceEquals(a, b))
			return true;
		if (a is null || b is null)
			return false;
		return a.Equals(b);
	}
}
