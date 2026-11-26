using System;
using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

public partial class CountryManager : Node
{
	[Signal]
	public delegate void PlayerCountryChangedEventHandler();

	// Emitted when the player's country has no provinces left.
	[Signal]
	public delegate void PlayerEliminatedEventHandler(GodotObject country);

	public static CountryManager Instance => Autoload.Get<CountryManager>("CountryManager");

	public Godot.Collections.Dictionary countries = new();
	public string[] countryNames = [];
	public GodotObject playerCountry
	{
		get => PlayerController.Instance.Country;
		set => PlayerController.Instance.Country = value;
	}

	public CountryData Player => playerCountry as CountryData;

	public CountryData GetCountry(string aName)
		=> countries.TryGetValue(aName, out var value) ? value.As<CountryData>() : null;

	public List<ReleasableData> Releasables = new();
	public Godot.Collections.Dictionary deadCountries = new();

	public bool cleanupLock = false;

	public void _on_hour_passed()
	{
		foreach (var cName in countryNames)
		{
			if (countries.TryGetValue(cName, out var value))
				value.As<CountryData>().ProcessHour();
		}
	}

	public void _on_day_passed()
	{
		EconomyManager.Instance.ProcessEconomyDay();

		foreach (var cName in countryNames)
		{
			if (countries.TryGetValue(cName, out var value))
				value.As<CountryData>().ProcessDay();
		}

		EventManager.Instance.CheckSuperEvents();
	}

	public void InitializeCountries(Godot.Collections.Array aCountriesData)
	{
		// Clear existing data before adding
		countries.Clear();
		countryNames = [];
		deadCountries.Clear();
		// player_country = null

		if (GameState.Instance.isLoadingGame)
			GD.Print("CountryManager: Initializing from save data...");

		foreach (var countryVariant in aCountriesData)
			AddCountry(countryVariant.AsGodotDictionary());

		foreach (var cName in countryNames)
		{
			if (!countries.TryGetValue(cName, out var countryValue))
				continue;

			var country = countryValue.AsGodotObject();
			country.Call("UpdateRelations");
			foreach (var puppetedVariant in country.Get("puppets").AsGodotArray())
			{
				var puppeted = puppetedVariant.AsString();
				if (countries.TryGetValue(puppeted, out var puppetValue))
					InformPuppet(country, puppetValue.AsGodotObject());
			}
		}

		GD.Print($"CountryManager: Initialized {countries.Count} countries.");
	}

	// C# CountryData stores these as Array<string>; older saves/paths used PackedStringArray.
	private static Godot.Collections.Array<string> AsStringArray(Variant value)
	{
		var result = new Godot.Collections.Array<string>();
		if (value.VariantType == Variant.Type.PackedStringArray)
		{
			foreach (var name in value.AsStringArray())
				result.Add(name);
		}
		else if (value.VariantType == Variant.Type.Array)
		{
			foreach (var name in value.AsGodotArray())
				result.Add(name.AsString());
		}
		return result;
	}

	public void _ensure_country_has_leader(GodotObject country)
	{
		var countryName = country.Get("countryName").AsString();
		var positions = country.Get("governmentPositions").AsGodotDictionary();
		var leaders = positions.ContainsKey("Leader")
			? AsStringArray(positions["Leader"])
			: new Godot.Collections.Array<string>();
		if (leaders.Count > 0)
			return;

		var newFigure = ImportantFigureHost.FromRandom(countryName);
		newFigure.occupation = "Leader";
		MapManager.Instance.significantFigures[newFigure.name] = newFigure;

		var figures = AsStringArray(country.Get("figures"));
		figures.Add(newFigure.name);
		country.Set("figures", figures);

		leaders.Add(newFigure.name);
		positions["Leader"] = leaders;

		GD.Print($"CountryManager: Assigned random leader {newFigure.name} to {countryName}");
	}

	public void GenerateMissingLeaders()
	{
		foreach (var cName in countryNames)
		{
			if (!countries.TryGetValue(cName, out var value))
				continue;

			var country = value.AsGodotObject();
			_ensure_country_has_leader(country);
			country.Call("InitializeCabinet");
		}
	}

	public Godot.Collections.Array SaveCountries()
	{
		var polities = new Godot.Collections.Array();
		foreach (var value in countries.Values)
			polities.Add(value.AsGodotObject().Call("ToDict").AsGodotDictionary());
		foreach (var value in deadCountries.Values)
			polities.Add(value.AsGodotObject().Call("ToDict").AsGodotDictionary());
		return polities;
	}

	public Color GetCountryColor(string aCountry, Color aDefaultColor)
	{
		return countries.TryGetValue(aCountry, out var value)
			? value.AsGodotObject().Get("countryColor").AsColor()
			: aDefaultColor;
	}

	// GDScript call sites use both arities. Godot 4.3+ supports overloads by argument count.
	public Color GetCountryColor(string aCountry) => GetCountryColor(aCountry, Colors.Black);

	public void SetPlayerCountry(string countryName)
	{
		if (!countries.TryGetValue(countryName, out var value))
		{
			GD.PushError($"CountryManager: Requested non-existent country '{countryName}'");
			return;
		}

		var country = value.AsGodotObject();
		if (country == null || !GodotObject.IsInstanceValid(country))
		{
			GD.PushError($"CountryManager: Requested non-existent country '{countryName}'");
			return;
		}

		if (playerCountry != null && GodotObject.IsInstanceValid(playerCountry))
			playerCountry.Set("isPlayer", false);

		playerCountry = country;
		playerCountry.Set("isPlayer", true);

		GD.Print("Player is now playing as: ", countryName);
		EmitSignal(SignalName.PlayerCountryChanged);
	}

	public GodotObject AddCountry(Godot.Collections.Dictionary aCountryData)
	{
		var tempName = aCountryData["name"].AsString();
		if (tempName == "Sea")
			return null;

		// 1. Check if it already exists
		if (countries.ContainsKey(tempName))
		{
			GD.PushWarning($"CountryManager: Country '{tempName}' already exists!");
			return countries[tempName].AsGodotObject();
		}

		if (deadCountries.ContainsKey(tempName))
		{
			var revived = deadCountries[tempName].AsGodotObject();
			deadCountries.Remove(tempName);
			countries[tempName] = revived;
			AppendCountryName(tempName);
			if (revived.Get("isPlayer").AsBool())
				playerCountry = revived;
			GD.Print($"CountryManager: Revived country '{tempName}'.");
			return revived;
		}

		// 3. If flag exists, create and store the country
		var newCountry = CountryData.FromDict(aCountryData);
		countries[tempName] = newCountry;
		AppendCountryName(tempName);

		if (newCountry.Get("isPlayer").AsBool())
			playerCountry = newCountry;

		return newCountry;
	}

	// HELPER FUNCTIONS ==========================================

	public int GetCountryPopulation(string countryName)
	{
		var countryToProvinces = MapManager.Instance.countryToProvinces;
		if (!countryToProvinces.TryGetValue(countryName, out var pids))
			return 0;

		var provinceObjects = MapManager.Instance.provinceObjects;
		var totalPop = 0;
		foreach (var pid in pids)
		{
			if (provinceObjects.TryGetValue(pid, out var province))
				totalPop += province.GetPopulation();
		}
		return totalPop;
	}

	public int GetCountryGdp(string countryName)
	{
		var countryToProvinces = MapManager.Instance.countryToProvinces;
		if (!countryToProvinces.TryGetValue(countryName, out var pids))
			return 0;

		var provinceObjects = MapManager.Instance.provinceObjects;
		var totalGdp = 0;
		foreach (var pid in pids)
		{
			if (provinceObjects.TryGetValue(pid, out var province))
				totalGdp += province.gdp;
		}
		return totalGdp;
	}

	public int GetFactoriesAmount(string countryName)
	{
		var countryToProvinces = MapManager.Instance.countryToProvinces;
		if (!countryToProvinces.TryGetValue(countryName, out var pids))
			return 0;

		var provinceObjects = MapManager.Instance.provinceObjects;
		var factoryCount = 0;
		foreach (var pid in pids)
		{
			if (!provinceObjects.TryGetValue(pid, out var province))
				continue;

			foreach (var building in province.buildings)
			{
				if (building.type == "Factory" && building.state == BuildingData.BuildingState.FUNCTIONAL)
					factoryCount++;
			}
		}
		return factoryCount;
	}

	// NOTE(pol): We should keep track of the manpower used instead of recalculating.
	public static int GetCountryUsedManpower(GodotObject countryObj)
	{
		var totalUsed = 0;
		var toUse = new Dictionary<string, double>();

		// 2. Ongoing Training (Already using templates, but cleaned up)
		foreach (var trainingVariant in countryObj.Get("ongoingTraining").AsGodotArray())
		{
			var training = trainingVariant.AsGodotObject();
			var type = training.Get("divisionType").AsString();
			toUse[type] = toUse.GetValueOrDefault(type) + training.Get("divisionsCount").AsInt32();
		}

		// 3. Troops in the "Ready" queue (deployment pool)
		foreach (var batchVariant in countryObj.Get("readyTroops").AsGodotArray())
		{
			var batch = batchVariant.AsGodotObject();
			var type = batch.Get("division").AsGodotObject().Get("type").AsString();
			toUse[type] = toUse.GetValueOrDefault(type) + batch.Get("count").AsInt32();
		}

		foreach (var entry in toUse)
			totalUsed += (int)(entry.Value * _get_manpower_from_template(entry.Key));

		// 1. Active Troops on the field
		var troops = TroopManager.Instance.GetTroopsForCountry(countryObj.Get("countryName").AsString());
		foreach (var troopVariant in troops)
		{
			var troop = troopVariant;
			foreach (var div in troop.storedDivisions)
			{
				totalUsed += (int)(div.hp * div.manpowerPerHP);
			}
		}

		return totalUsed;
	}

	// Helper to keep the code DRY (Don't Repeat Yourself)
	public static int _get_manpower_from_template(string type)
	{
		var templates = DivisionData.TEMPLATES;
		var template = templates.ContainsKey(type)
			? templates[type]
			: templates["infantry"];
		return template.Get("manpower").Int();
	}

	public void CleanupEmptyCountries()
	{
		var toRemove = new List<string>();

		foreach (var cName in countryNames)
		{
			var countryToProvinces = MapManager.Instance.countryToProvinces;
			var noProvinces = !countryToProvinces.TryGetValue(cName, out var pids) || pids.Count == 0;
			if (noProvinces && !countries[cName].AsGodotObject().Get("isExiled").AsBool())
				toRemove.Add(cName);
		}

		foreach (var cName in toRemove)
		{
			GD.Print($"CountryManager: Removing '{cName}' (No provinces found).");
			// So here, we offer options: Government In Exile, or Dissolution
			var country = countries[cName].AsGodotObject();
			if (country.Get("isPlayer").AsBool())
			{
				// Uh oh.
				EmitSignal(SignalName.PlayerEliminated, country);
			}
			else
			{
				var deleting = country;
				deadCountries[cName] = deleting;
				//CountryManager.Releasables.append(ReleasableData.FromDict({}))
				var owner = deleting.Get("owner").AsString();
				if (Array.IndexOf(countryNames, owner) >= 0 && countries.TryGetValue(owner, out var ownerValue))
					ReleasePuppet(ownerValue.AsGodotObject(), deleting);

				foreach (var puppetVariant in deleting.Get("puppets").AsGodotArray().Duplicate())
				{
					var puppet = puppetVariant.AsString();
					if (countries.TryGetValue(puppet, out var puppetValue))
						ReleasePuppet(deleting, puppetValue.AsGodotObject());
				}

				FactionManager.Instance.ClearFaction(deleting);

				var map = MapManager.Instance;
				foreach (var lookup in new[]
				{
					map.countryToProvinces,
					map.countryToOccupiedProvinces,
					map.countryToRightfulProvinces,
					map.countryToOwnedProvinces,
				})
				{
					lookup.Remove(cName);
				}

				countries.Remove(cName);
				RemoveCountryName(cName);
			}
		}
	}

	public void InformPuppet(GodotObject puppeter, GodotObject puppetee)
	{
		var puppeteerName = puppeter.Get("countryName").AsString();
		var puppeteeName = puppetee.Get("countryName").AsString();
		puppeter.Get("allowedCountries").AsGodotArray().Add(puppeteeName);
		puppetee.Get("allowedCountries").AsGodotArray().Add(puppeteerName);
		puppetee.Set("isPuppet", true);
		puppetee.Set("owner", puppeteerName);
		puppetee.Set("ideology", puppeter.Get("ideology"));
		puppetee.Get("relations").AsGodotDictionary()[puppeteerName] = 200;
	}

	public void MakePuppet(GodotObject puppeter, GodotObject puppetee)
	{
		var puppeteerName = puppeter.Get("countryName").AsString();
		var puppeteeName = puppetee.Get("countryName").AsString();
		puppeter.Get("puppets").AsGodotArray().Add(puppeteeName);
		puppeter.Get("allowedCountries").AsGodotArray().Add(puppeteeName);
		puppetee.Get("allowedCountries").AsGodotArray().Add(puppeteerName);
		MapManager.Instance.AllowPids(puppeter, puppetee);
		MapManager.Instance.AllowPids(puppetee, puppeter);
		puppetee.Set("isPuppet", true);
		puppetee.Set("owner", puppeteerName);
		puppetee.Set("ideology", puppeter.Get("ideology"));
		puppetee.Get("relations").AsGodotDictionary()[puppeteerName] = 200;
		FactionManager.Instance.ClearFaction(puppetee);
		puppetee.Set("factions", puppeter.Get("factions"));
		FactionManager.Instance.InviteFaction(puppeter, puppetee);
		MapManager.Instance.ShowCountriesMap();
	}

	public void ReleasePuppet(GodotObject puppeter, GodotObject puppetee)
	{
		var puppeteerName = puppeter.Get("countryName").AsString();
		var puppeteeName = puppetee.Get("countryName").AsString();
		MapManager.Instance.UnallowPids(puppeter, puppetee);
		puppeter.Get("puppets").AsGodotArray().Remove(puppeteeName);
		puppeter.Get("allowedCountries").AsGodotArray().Remove(puppeteeName);
		puppetee.Get("allowedCountries").AsGodotArray().Remove(puppeteerName);
		puppetee.Set("isPuppet", false);
		puppetee.Set("owner", "");
		puppetee.Set("factions", new Godot.Collections.Array());
		MapManager.Instance.ShowCountriesMap();
	}

	public void MakeHost(GodotObject aHost, GodotObject aHosted)
	{
		var hostName = aHost.Get("countryName").AsString();
		var hostedName = aHosted.Get("countryName").AsString();
		aHost.Get("hostedGovernments").AsGodotArray().Add(hostedName);
		aHost.Get("allowedCountries").AsGodotArray().Add(hostedName);
		aHosted.Get("allowedCountries").AsGodotArray().Add(hostName);
		aHosted.Set("isExiled", true);
		aHosted.Set("host", hostName);
		MapManager.Instance.ShowCountriesMap();
	}

	public void FreeHost(GodotObject aHost, GodotObject aHosted)
	{
		var hostName = aHost.Get("countryName").AsString();
		var hostedName = aHosted.Get("countryName").AsString();
		aHost.Get("puppets").AsGodotArray().Remove(hostedName);
		aHost.Get("allowedCountries").AsGodotArray().Remove(hostedName);
		aHosted.Get("allowedCountries").AsGodotArray().Remove(hostName);
		aHosted.Set("isExiled", false);
		aHosted.Set("host", "");
		MapManager.Instance.ShowCountriesMap();
	}

	public void RenameCountry(string _a_old, string _a_new)
	{
		// pass
	}

	private void AppendCountryName(string name)
	{
		var names = new List<string>(countryNames) { name };
		countryNames = names.ToArray();
	}

	private void RemoveCountryName(string name)
	{
		var names = new List<string>(countryNames);
		names.Remove(name);
		countryNames = names.ToArray();
	}
}
