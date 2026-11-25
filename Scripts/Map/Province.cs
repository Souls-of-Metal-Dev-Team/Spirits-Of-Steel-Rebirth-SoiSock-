using Godot;

namespace Sosr;

[GlobalClass]
public partial class Province : Resource
{
	public const int SEA = 0;
	public const int LAND = 1;
	public const int NO_FACTORY = 0;
	public const int NO_PORT = 0;
	public const int FACTORY_BUILDING = 1;
	public const int PORT_BUILDING = 1;
	public const int FACTORY_BUILT = 2;
	public const int PORT_BUILT = 2;

	public int type = LAND;
	public int id;
	public string name = "";
	public string country = "";
	public string occupier = "";
	public string biome = "";
	public Godot.Collections.Array<ResourceNode> resources = new();
	public string city = "";
	public Godot.Collections.Array<BuildingData> buildings = new();
	public Godot.Collections.Array<PopulationData> populations = new();
	public int gdp = 1000;
	public Vector2 center;
	// var neighbors: PackedInt32Array = []
	public string[] claims = System.Array.Empty<string>();
	public int infrastructure;
	public int maxInfrastructure = 3;

	public Godot.Collections.Dictionary ToDict()
	{
		return ToDictWithTroops(new Godot.Collections.Array());
	}

	public Godot.Collections.Dictionary ToDictWithTroops(Godot.Collections.Array troops_data)
	{
		var resource_array = new Godot.Collections.Array();
		foreach (var resourceData in resources)
		{
			resource_array.Add(resourceData.ToDict());
		}

		var population_array = new Godot.Collections.Array();
		foreach (var populationData in populations)
		{
			population_array.Add(populationData.ToDict());
		}

		var building_array = new Godot.Collections.Array();
		foreach (var buildingData in buildings)
		{
			building_array.Add(buildingData.ToDict());
		}

		// Variant has no implicit conversion from PackedStringArray; string[] becomes a packed string Variant.
		var claim_values = new string[0];
		if (!(claims.Length == 1 && claims[0] == country))
		{
			claim_values = new string[claims.Length];
			for (var i = 0; i < claims.Length; i++)
			{
				claim_values[i] = claims[i];
			}
		}

		var province_dict = new Godot.Collections.Dictionary
		{
			{ "type", type },
			{ "name", name },
			{ "polity", country },
			{ "occupier", occupier },
			{ "biome", biome },
			{ "resources", resource_array },
			{ "city", city },
			{ "buildings", building_array },
			{ "populations", population_array },
			{ "gdp", gdp },
			{ "claims", claim_values },
			{ "infrastructure", infrastructure },
			{ "max_infrastructure", maxInfrastructure },
			{ "troops", troops_data ?? new Godot.Collections.Array() },
		};
		return province_dict;
	}

	public static Province FromDict(Godot.Collections.Dictionary a_data)
	{
		var province = new Province();

		var type_value = a_data.ContainsKey("type") ? a_data["type"].AsInt32() : SEA;
		if (type_value == SEA)
		{
			// SEA LOGIC: Unique ID, but 0 stats
			province.type = SEA;
			province.name = "Sea";
			province.country = "Sea";
			province.occupier = "";
			province.biome = a_data.ContainsKey("biome") ? (string)a_data["biome"] : "Sea";
			foreach (var resourceData in a_data.ContainsKey("resources") ? a_data["resources"].AsGodotArray() : new Godot.Collections.Array())
			{
				province.resources.Add(ResourceNode.FromDict(resourceData.AsGodotDictionary()));
			}
			province.city = "";
			province.buildings = new Godot.Collections.Array<BuildingData>();
			province.populations = new Godot.Collections.Array<PopulationData>();
			province.gdp = 0;
			province.infrastructure = a_data.ContainsKey("infrastructure") ? a_data["infrastructure"].AsInt32() : 0;
			province.maxInfrastructure = a_data.ContainsKey("max_infrastructure") ? a_data["max_infrastructure"].AsInt32() : 0;
		}
		else
		{
			// LAND LOGIC
			province.type = LAND;
			province.name = (string)a_data["name"];
			province.country = (string)a_data["polity"];
			province.occupier = a_data.ContainsKey("occupier") ? (string)a_data["occupier"] : "";
			province.biome = a_data.ContainsKey("biome") ? (string)a_data["biome"] : "Plains";
			foreach (var resourceData in a_data.ContainsKey("resources") ? a_data["resources"].AsGodotArray() : new Godot.Collections.Array())
			{
				province.resources.Add(ResourceNode.FromDict(resourceData.AsGodotDictionary()));
			}
			province.city = a_data.ContainsKey("city") ? (string)a_data["city"] : "";
			foreach (var buildingData in a_data.ContainsKey("buildings") ? a_data["buildings"].AsGodotArray() : new Godot.Collections.Array())
			{
				province.buildings.Add(BuildingData.FromDict(buildingData.AsGodotDictionary()));
			}
			foreach (var populationData in a_data.ContainsKey("populations") ? a_data["populations"].AsGodotArray() : new Godot.Collections.Array())
			{
				province.populations.Add(PopulationData.FromDict(populationData.AsGodotDictionary()));
			}
			var raw_claims = a_data.ContainsKey("claims") ? a_data["claims"].AsGodotArray() : new Godot.Collections.Array();
			if (raw_claims.Count == 0)
			{
				province.claims = new[] { province.country };
			}
			else
			{
				var claim_list = new System.Collections.Generic.List<string>();
				foreach (var claim in raw_claims)
				{
					claim_list.Add(claim.AsString());
				}
				province.claims = claim_list.ToArray();
			}
			province.gdp = a_data.ContainsKey("gdp") ? a_data["gdp"].AsInt32() : 1000;
			province.infrastructure = a_data.ContainsKey("infrastructure") ? a_data["infrastructure"].AsInt32() : 0;
			province.maxInfrastructure = a_data.ContainsKey("max_infrastructure") ? a_data["max_infrastructure"].AsInt32() : 5;
		}

		return province;
	}

	public int GetPopulation()
	{
		var totalPopulation = 0;
		foreach (var subpopulation in populations)
		{
			totalPopulation += subpopulation.amount;
		}
		return totalPopulation;
	}

	public int GetFactories()
	{
		var totalFactories = 0;
		foreach (var building in buildings)
		{
			if (building.state == BuildingData.BuildingState.FUNCTIONAL)
			{
				totalFactories += 1;
			}
		}
		return totalFactories;
	}

	public string GetFunctionalOwner()
	{
		if (occupier == "")
		{
			return country;
		}
		else
		{
			return occupier;
		}
	}

	public string GetResources()
	{
		var counts = new Godot.Collections.Dictionary();
		foreach (var resource in resources)
		{
			if (counts.ContainsKey(resource.type))
			{
				counts[resource.type] = counts[resource.type].AsInt32() + resource.amount;
			}
			else
			{
				counts[resource.type] = resource.amount;
			}
		}

		var list = "";
		foreach (var resource_type in counts.Keys)
		{
			if (list != "")
			{
				list += "\n";
			}
			list += $"{resource_type}: {counts[resource_type].AsInt32()}";
		}
		return list;
	}
}
