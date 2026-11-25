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
	public GodotObject player_country = CountryData.FromDict(new Godot.Collections.Dictionary { { "name", "Sea" } });

	public CountryData Player => player_country as CountryData;

	public CountryData GetCountry(string a_name)
		=> countries.TryGetValue(a_name, out var value) ? value.As<CountryData>() : null;

	public Godot.Collections.Array<ReleasableData> Releasables = new();
	public Godot.Collections.Dictionary dead_countries = new();

	public bool cleanupLock = false;

	public void _on_hour_passed()
	{
		foreach (var cName in countryNames)
		{
			if (countries.TryGetValue(cName, out var value))
				value.AsGodotObject().Call("process_hour");
		}
	}

	public void _on_day_passed()
	{
		EconomyManager.Instance.process_economy_day();

		foreach (var cName in countryNames)
		{
			if (countries.TryGetValue(cName, out var value))
				value.AsGodotObject().Call("process_day");
		}

		EventManager.Instance.check_super_events();
	}

	public void initialize_countries(Godot.Collections.Array a_countriesData)
	{
		// Clear existing data before adding
		countries.Clear();
		countryNames = [];
		dead_countries.Clear();
		// player_country = null

		if (GameState.Instance.is_loading_game)
			GD.Print("CountryManager: Initializing from save data...");

		foreach (var countryVariant in a_countriesData)
			add_country(countryVariant.AsGodotDictionary());

		foreach (var cName in countryNames)
		{
			if (!countries.TryGetValue(cName, out var countryValue))
				continue;

			var country = countryValue.AsGodotObject();
			country.Call("update_relations");
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
		var countryName = country.Get("country_name").AsString();
		var positions = country.Get("governmentPositions").AsGodotDictionary();
		var leaders = positions.ContainsKey("Leader")
			? AsStringArray(positions["Leader"])
			: new Godot.Collections.Array<string>();
		if (leaders.Count > 0)
			return;

		var newFigure = ImportantFigure.FromRandom(countryName);
		newFigure.occupation = "Leader";
		MapManager.Instance.significantFigures[newFigure.name] = newFigure;

		var figures = AsStringArray(country.Get("figures"));
		figures.Add(newFigure.name);
		country.Set("figures", figures);

		leaders.Add(newFigure.name);
		positions["Leader"] = leaders;

		GD.Print($"CountryManager: Assigned random leader {newFigure.name} to {countryName}");
	}

	public void generate_missing_leaders()
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
		foreach (var value in dead_countries.Values)
			polities.Add(value.AsGodotObject().Call("ToDict").AsGodotDictionary());
		return polities;
	}

	public Color GetCountryColor(string a_country, Color a_defaultColor)
	{
		return countries.TryGetValue(a_country, out var value)
			? value.AsGodotObject().Get("country_color").AsColor()
			: a_defaultColor;
	}

	// GDScript call sites use both arities. Godot 4.3+ supports overloads by argument count.
	public Color GetCountryColor(string a_country) => GetCountryColor(a_country, Colors.Black);

	public void set_player_country(string country_name)
	{
		if (!countries.TryGetValue(country_name, out var value))
		{
			GD.PushError($"CountryManager: Requested non-existent country '{country_name}'");
			return;
		}

		var country = value.AsGodotObject();
		if (country == null || !GodotObject.IsInstanceValid(country))
		{
			GD.PushError($"CountryManager: Requested non-existent country '{country_name}'");
			return;
		}

		if (player_country != null && GodotObject.IsInstanceValid(player_country))
			player_country.Set("is_player", false);

		player_country = country;
		player_country.Set("is_player", true);

		GD.Print("Player is now playing as: ", country_name);
		EmitSignal(SignalName.PlayerCountryChanged);
	}

	public GodotObject add_country(Godot.Collections.Dictionary a_countryData)
	{
		var tempName = a_countryData["name"].AsString();
		if (tempName == "Sea")
			return null;

		// 1. Check if it already exists
		if (countries.ContainsKey(tempName))
		{
			GD.PushWarning($"CountryManager: Country '{tempName}' already exists!");
			return countries[tempName].AsGodotObject();
		}

		if (dead_countries.ContainsKey(tempName))
		{
			var revived = dead_countries[tempName].AsGodotObject();
			dead_countries.Remove(tempName);
			countries[tempName] = revived;
			AppendCountryName(tempName);
			if (revived.Get("is_player").AsBool())
				player_country = revived;
			GD.Print($"CountryManager: Revived country '{tempName}'.");
			return revived;
		}

		// 3. If flag exists, create and store the country
		var newCountry = CountryData.FromDict(a_countryData);
		countries[tempName] = newCountry;
		AppendCountryName(tempName);

		if (newCountry.Get("is_player").AsBool())
			player_country = newCountry;

		return newCountry;
	}

	// HELPER FUNCTIONS ==========================================

	public int get_country_population(string country_name)
	{
		var countryToProvinces = MapManager.Instance.country_to_provinces;
		if (!countryToProvinces.TryGetValue(country_name, out var pids))
			return 0;

		var provinceObjects = MapManager.Instance.province_objects;
		var totalPop = 0;
		foreach (var pid in pids)
		{
			if (provinceObjects.TryGetValue(pid, out var province))
				totalPop += province.GetPopulation();
		}
		return totalPop;
	}

	public int get_country_gdp(string country_name)
	{
		var countryToProvinces = MapManager.Instance.country_to_provinces;
		if (!countryToProvinces.TryGetValue(country_name, out var pids))
			return 0;

		var provinceObjects = MapManager.Instance.province_objects;
		var totalGdp = 0;
		foreach (var pid in pids)
		{
			if (provinceObjects.TryGetValue(pid, out var province))
				totalGdp += province.gdp;
		}
		return totalGdp;
	}

	public int get_factories_amount(string country_name)
	{
		var countryToProvinces = MapManager.Instance.country_to_provinces;
		if (!countryToProvinces.TryGetValue(country_name, out var pids))
			return 0;

		var provinceObjects = MapManager.Instance.province_objects;
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
	public static int get_country_used_manpower(GodotObject country_obj)
	{
		var totalUsed = 0;
		var toUse = new Dictionary<string, double>();

		// 2. Ongoing Training (Already using templates, but cleaned up)
		foreach (var trainingVariant in country_obj.Get("ongoing_training").AsGodotArray())
		{
			var training = trainingVariant.AsGodotObject();
			var type = training.Get("division_type").AsString();
			toUse[type] = toUse.GetValueOrDefault(type) + training.Get("divisions_count").AsInt32();
		}

		// 3. Troops in the "Ready" queue (deployment pool)
		foreach (var batchVariant in country_obj.Get("ready_troops").AsGodotArray())
		{
			var batch = batchVariant.AsGodotObject();
			var type = batch.Get("division").AsGodotObject().Get("type").AsString();
			toUse[type] = toUse.GetValueOrDefault(type) + batch.Get("count").AsInt32();
		}

		foreach (var entry in toUse)
			totalUsed += (int)(entry.Value * _get_manpower_from_template(entry.Key));

		// 1. Active Troops on the field
		var troops = TroopManager.Instance.get_troops_for_country(country_obj.Get("country_name").AsString());
		foreach (var troopVariant in troops)
		{
			var troop = troopVariant.AsGodotObject();
			foreach (var divVariant in troop.Get("stored_divisions").AsGodotArray())
			{
				var div = divVariant.AsGodotObject();
				totalUsed += (int)(div.Get("hp").AsDouble() * div.Get("manpowerPerHP").AsDouble());
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
		return template["manpower"].AsInt32();
	}

	public void cleanup_empty_countries()
	{
		var toRemove = new List<string>();

		foreach (var cName in countryNames)
		{
			var countryToProvinces = MapManager.Instance.country_to_provinces;
			var noProvinces = !countryToProvinces.TryGetValue(cName, out var pids) || pids.Count == 0;
			if (noProvinces && !countries[cName].AsGodotObject().Get("is_exiled").AsBool())
				toRemove.Add(cName);
		}

		foreach (var cName in toRemove)
		{
			GD.Print($"CountryManager: Removing '{cName}' (No provinces found).");
			// So here, we offer options: Government In Exile, or Dissolution
			var country = countries[cName].AsGodotObject();
			if (country.Get("is_player").AsBool())
			{
				// Uh oh.
				EmitSignal(SignalName.PlayerEliminated, country);
			}
			else
			{
				var deleting = country;
				dead_countries[cName] = deleting;
				//CountryManager.Releasables.append(ReleasableData.FromDict({}))
				var owner = deleting.Get("owner").AsString();
				if (Array.IndexOf(countryNames, owner) >= 0 && countries.TryGetValue(owner, out var ownerValue))
					release_puppet(ownerValue.AsGodotObject(), deleting);

				foreach (var puppetVariant in deleting.Get("puppets").AsGodotArray().Duplicate())
				{
					var puppet = puppetVariant.AsString();
					if (countries.TryGetValue(puppet, out var puppetValue))
						release_puppet(deleting, puppetValue.AsGodotObject());
				}

				FactionManager.Instance.clear_faction(deleting);

				var map = MapManager.Instance;
				foreach (var lookup in new[]
				{
					map.country_to_provinces,
					map.country_to_occupied_provinces,
					map.country_to_rightful_provinces,
					map.country_to_owned_provinces,
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
		var puppeteerName = puppeter.Get("country_name").AsString();
		var puppeteeName = puppetee.Get("country_name").AsString();
		puppeter.Get("allowedCountries").AsGodotArray().Add(puppeteeName);
		puppetee.Get("allowedCountries").AsGodotArray().Add(puppeteerName);
		puppetee.Set("is_puppet", true);
		puppetee.Set("owner", puppeteerName);
		puppetee.Set("ideology", puppeter.Get("ideology"));
		puppetee.Get("relations").AsGodotDictionary()[puppeteerName] = 200;
	}

	public void make_puppet(GodotObject puppeter, GodotObject puppetee)
	{
		var puppeteerName = puppeter.Get("country_name").AsString();
		var puppeteeName = puppetee.Get("country_name").AsString();
		puppeter.Get("puppets").AsGodotArray().Add(puppeteeName);
		puppeter.Get("allowedCountries").AsGodotArray().Add(puppeteeName);
		puppetee.Get("allowedCountries").AsGodotArray().Add(puppeteerName);
		MapManager.Instance.AllowPids(puppeter, puppetee);
		MapManager.Instance.AllowPids(puppetee, puppeter);
		puppetee.Set("is_puppet", true);
		puppetee.Set("owner", puppeteerName);
		puppetee.Set("ideology", puppeter.Get("ideology"));
		puppetee.Get("relations").AsGodotDictionary()[puppeteerName] = 200;
		FactionManager.Instance.clear_faction(puppetee);
		puppetee.Set("factions", puppeter.Get("factions"));
		FactionManager.Instance.invite_faction(puppeter, puppetee);
		MapManager.Instance.ShowCountriesMap();
	}

	public void release_puppet(GodotObject puppeter, GodotObject puppetee)
	{
		var puppeteerName = puppeter.Get("country_name").AsString();
		var puppeteeName = puppetee.Get("country_name").AsString();
		MapManager.Instance.UnallowPids(puppeter, puppetee);
		puppeter.Get("puppets").AsGodotArray().Remove(puppeteeName);
		puppeter.Get("allowedCountries").AsGodotArray().Remove(puppeteeName);
		puppetee.Get("allowedCountries").AsGodotArray().Remove(puppeteerName);
		puppetee.Set("is_puppet", false);
		puppetee.Set("owner", "");
		puppetee.Set("factions", new Godot.Collections.Array());
		MapManager.Instance.ShowCountriesMap();
	}

	public void MakeHost(GodotObject a_host, GodotObject a_hosted)
	{
		var hostName = a_host.Get("country_name").AsString();
		var hostedName = a_hosted.Get("country_name").AsString();
		a_host.Get("hostedGovernments").AsGodotArray().Add(hostedName);
		a_host.Get("allowedCountries").AsGodotArray().Add(hostedName);
		a_hosted.Get("allowedCountries").AsGodotArray().Add(hostName);
		a_hosted.Set("is_exiled", true);
		a_hosted.Set("host", hostName);
		MapManager.Instance.ShowCountriesMap();
	}

	public void FreeHost(GodotObject a_host, GodotObject a_hosted)
	{
		var hostName = a_host.Get("country_name").AsString();
		var hostedName = a_hosted.Get("country_name").AsString();
		a_host.Get("puppets").AsGodotArray().Remove(hostedName);
		a_host.Get("allowedCountries").AsGodotArray().Remove(hostedName);
		a_hosted.Get("allowedCountries").AsGodotArray().Remove(hostName);
		a_hosted.Set("is_exiled", false);
		a_hosted.Set("host", "");
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
