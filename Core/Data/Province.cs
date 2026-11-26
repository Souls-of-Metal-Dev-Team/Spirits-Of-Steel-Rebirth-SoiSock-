using System;
using System.Collections.Generic;

namespace Sosr;

public sealed class Province
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
	public List<ResourceNode> resources = new();
	public string city = "";
	public List<BuildingData> buildings = new();
	public List<PopulationData> populations = new();
	public int gdp = 1000;
	public Vec2 center = Vec2.Zero;
	public string[] claims = Array.Empty<string>();
	public int infrastructure;
	public int maxInfrastructure = 3;

	public Dictionary<string, object?> ToDict() => ToDictWithTroops(new List<object?>());

	public Dictionary<string, object?> ToDictWithTroops(List<object?> troopsData)
	{
		var resourceArray = new List<object?>();
		foreach (var resourceData in resources)
			resourceArray.Add(resourceData.ToDict());

		var populationArray = new List<object?>();
		foreach (var populationData in populations)
			populationArray.Add(populationData.ToDict());

		var buildingArray = new List<object?>();
		foreach (var buildingData in buildings)
			buildingArray.Add(buildingData.ToDict());

		// Mirror the old collapse of a single own-claim to an empty array.
		var claimValues = new List<object?>();
		if (!(claims.Length == 1 && claims[0] == country))
			foreach (var claim in claims)
				claimValues.Add(claim);

		return new Dictionary<string, object?>
		{
			{ "type", (double)type },
			{ "name", name },
			{ "polity", country },
			{ "occupier", occupier },
			{ "biome", biome },
			{ "resources", resourceArray },
			{ "city", city },
			{ "buildings", buildingArray },
			{ "populations", populationArray },
			{ "gdp", (double)gdp },
			{ "claims", claimValues },
			{ "infrastructure", (double)infrastructure },
			{ "max_infrastructure", (double)maxInfrastructure },
			{ "troops", troopsData ?? new List<object?>() },
		};
	}

	public static Province FromDict(Dictionary<string, object?> aData)
	{
		var province = new Province();
		var typeValue = aData.ContainsKey("type") ? aData["type"].Int() : SEA;

		foreach (var resourceData in aData.Get("resources").List())
			province.resources.Add(ResourceNode.FromDict(resourceData.Dict()));

		if (typeValue == SEA)
		{
			province.type = SEA;
			province.name = "Sea";
			province.country = "Sea";
			province.occupier = "";
			province.biome = aData.ContainsKey("biome") ? aData["biome"].Str("Sea") : "Sea";
			province.city = "";
			province.gdp = 0;
			province.infrastructure = aData.Get("infrastructure").Int();
			province.maxInfrastructure = aData.Get("max_infrastructure").Int();
		}
		else
		{
			province.type = LAND;
			province.name = aData.Get("name").Str();
			province.country = aData.Get("polity").Str();
			province.occupier = aData.ContainsKey("occupier") ? aData["occupier"].Str() : "";
			province.biome = aData.ContainsKey("biome") ? aData["biome"].Str("Plains") : "Plains";
			province.city = aData.ContainsKey("city") ? aData["city"].Str() : "";
			foreach (var buildingData in aData.Get("buildings").List())
				province.buildings.Add(BuildingData.FromDict(buildingData.Dict()));
			foreach (var populationData in aData.Get("populations").List())
				province.populations.Add(PopulationData.FromDict(populationData.Dict()));

			var rawClaims = aData.Get("claims").List();
			if (rawClaims.Count == 0)
			{
				province.claims = new[] { province.country };
			}
			else
			{
				var claimList = new List<string>();
				foreach (var claim in rawClaims)
					claimList.Add(claim.Str());
				province.claims = claimList.ToArray();
			}

			province.gdp = aData.ContainsKey("gdp") ? aData["gdp"].Int() : 1000;
			province.infrastructure = aData.Get("infrastructure").Int();
			province.maxInfrastructure = aData.ContainsKey("max_infrastructure") ? aData["max_infrastructure"].Int() : 5;
		}

		return province;
	}

	public int GetPopulation()
	{
		var total = 0;
		foreach (var subpopulation in populations)
			total += subpopulation.amount;
		return total;
	}

	public int GetFactories()
	{
		var total = 0;
		foreach (var building in buildings)
			if (building.state == BuildingData.BuildingState.FUNCTIONAL)
				total++;
		return total;
	}

	public string GetFunctionalOwner() => occupier == "" ? country : occupier;

}
