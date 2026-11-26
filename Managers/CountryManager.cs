using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

	// Emitted after every daily phase finishes. UI waits on this instead of GameClock.
	[Signal]
	public delegate void DayProcessedEventHandler();

	public static CountryManager Instance => GameState.Instance.countryManager;

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

	private const int CountriesPerFrame = 25;
	private readonly Queue<string[]> _pendingDays = new();
	private string[] _activeDay = Array.Empty<string>();
	private int _dayCursor;

	public override void _Ready() => SetProcess(true);

	public void _on_hour_passed()
	{
		foreach (var cName in countryNames)
		{
			if (countries.TryGetValue(cName, out var value))
				value.As<CountryData>().ProcessHour();
		}
	}

	// Clock signal only enqueues work. Order stays fixed: economy, countries, events.
	public void _on_day_passed() => _pendingDays.Enqueue((string[])countryNames.Clone());

	public override void _Process(double delta)
	{
		if (_activeDay.Length == 0)
		{
			if (_pendingDays.Count == 0)
				return;
			_activeDay = _pendingDays.Dequeue();
			_dayCursor = 0;
			BuildingManager.Instance.ProcessConstructionDay();
			ApplyBuildingIncome();
		}

		var end = Math.Min(_dayCursor + CountriesPerFrame, _activeDay.Length);
		for (; _dayCursor < end; _dayCursor++)
		{
			if (countries.TryGetValue(_activeDay[_dayCursor], out var value))
				value.As<CountryData>().ProcessDay();
		}

		if (_dayCursor < _activeDay.Length)
			return;

		RunAiDay(_activeDay);
		_activeDay = Array.Empty<string>();
		EventManager.Instance.CheckSuperEvents();
		EventManager.Instance._on_day_passed();
		EmitSignal(SignalName.DayProcessed);
	}

	private void RunAiDay(string[] names)
	{
		var controllers = new List<CountryAI>();
		foreach (var name in names)
		{
			if (!countries.TryGetValue(name, out var value) || value.As<CountryData>().isPlayer)
				continue;
			var controller = value.As<CountryData>().AiController;
			if (controller != null)
				controllers.Add(controller);
		}
		if (controllers.Count == 0)
			return;

		var snapshot = AiSnapshot.Capture();
		var indices = new int[controllers.Count];
		for (var i = 0; i < controllers.Count; i++)
		{
			if (!snapshot.index.TryGetValue(controllers[i].CountryName, out indices[i]))
			{
				GD.PushError("AI plan skipped: country missing from snapshot: " + controllers[i].CountryName);
				indices[i] = -1;
			}
		}
		var plans = new AiPlan[controllers.Count];
		Parallel.For(0, controllers.Count,
			new ParallelOptions { MaxDegreeOfParallelism = Math.Min(System.Environment.ProcessorCount, 4) },
			i => { if (indices[i] >= 0) plans[i] = controllers[i].PlanDay(snapshot, indices[i]); });

		for (var i = 0; i < plans.Length; i++)
		{
			var plan = plans[i];
			if (plan == null)
				continue;
			if (plan.TensionDelta != 0)
				MapManager.Instance.IncreaseWorldTension(plan.TensionDelta);
			foreach (var action in plan.Actions)
				ActionDispatcher.Instance.Submit(action);
		}
	}

	// Save must never capture a half-processed day.
	public void FlushDayQueue()
	{
		while (_activeDay.Length > 0 || _pendingDays.Count > 0)
			_Process(0);
	}

	private void ApplyBuildingIncome()
	{
		var buildingCounts = MapManager.Instance.buildingTypesToCountryCount;
		foreach (var keyValue in buildingCounts.Keys)
		{
			var parts = keyValue.AsString().Split(":");
			var template = BuildingTemplate.FromDict(
				BuildingManager.Instance.buildingDesigns[parts[0]][parts[1]].AsGodotDictionary().AsState());
			foreach (var countryValue in buildingCounts[keyValue].AsGodotDictionary().Keys)
			{
				var country = GetCountry(countryValue.AsString());
				var amount = buildingCounts[keyValue].AsGodotDictionary()[countryValue].AsDouble();
				country.money += template.produces * amount;
			}
		}
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

		var newFigure = FigureManager.Instance.Generate(countryName);
		newFigure.occupation = "Leader";

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
