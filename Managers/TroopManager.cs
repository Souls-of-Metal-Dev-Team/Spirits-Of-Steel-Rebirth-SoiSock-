using Godot;
using Sosr;
using System;
using System.Collections.Generic;

namespace Sosr;

public partial class TroopManager : Node
{
	public static TroopManager Instance => GameState.Instance.troopManager;

	// Emitted when a country's troops or division counts change.
	// countryName is "" when every troop was cleared.
	[Signal]
	public delegate void TroopsChangedEventHandler(string countryName);

	public bool AUTO_MERGE = true;

	public bool emptyLatch = false;

	public List<TroopData> toRemove = new();
	public List<TroopData> troops = new();
	public List<TroopData> movingTroops = new();
	public Dictionary<int, List<TroopData>> troopsByProvince = new(); // { province_id: [TroopData, ...] }
	public Dictionary<string, List<TroopData>> troopsByCountry = new(); // { country_name: [TroopData, ...] }

	public Dictionary<string, List<object?>> pathCache = new(); // { cache_key: path_array }
	public Dictionary<string, Texture2D> flagCache = new(); // { cache_key: texture }

	public TroopSelection troopSelection;

	// Used by popup for now
	public Godot.Collections.Dictionary flagRedirects = new();

	public string customFlagPath = "";

	// Training and ready-division queues. Static so CountryData can forward to them
	// before autoloads finish initializing.
	public static readonly Dictionary<string, List<TroopTraining>> TrainingByCountry = new();
	public static readonly Dictionary<string, List<ReadyTroop>> ReadyByCountry = new();

	public static List<TroopTraining> TrainingFor(string country)
	{
		if (!TrainingByCountry.TryGetValue(country, out var list))
			TrainingByCountry[country] = list = new List<TroopTraining>();
		return list;
	}

	public static List<ReadyTroop> ReadyFor(string country)
	{
		if (!ReadyByCountry.TryGetValue(country, out var list))
			ReadyByCountry[country] = list = new List<ReadyTroop>();
		return list;
	}

	// Controller requests. Movement and battle ticking stay internal to this manager.
	public bool QueueTraining(CountryData country, int count, string type)
	{
		if (!DivisionData.TEMPLATES.TryGetValue(type, out var template))
		{
			GD.PushError($"Unknown division type: {type}");
			return false;
		}
		var needed = count * template.Get("manpower").Int();
		if (country.manpower < needed || country.money < count * template.Get("cost").Double() * country.divCostMod)
			return false;

		country.manpower -= needed;
		country.mobilized += needed;
		TrainingFor(country.countryName).Add(TroopTraining.FromValues(
			count, type, template.Get("days").Int(), template.Get("cost").Double()));
		return true;
	}

	public void ProcessTrainingDay(CountryData country)
	{
		var queue = TrainingFor(country.countryName);
		for (var i = queue.Count - 1; i >= 0; i--)
		{
			var training = queue[i];
			var dailyCost = training.divisionsCount * training.dailyCost * country.divCostMod;
			if (country.money >= dailyCost)
			{
				country.money -= dailyCost;
				training.daysLeft -= 1;
			}
			if (training.daysLeft <= 0)
			{
				ReadyFor(country.countryName).Add(ReadyTroop.FromValues(
					DivisionData.CreateDivision(training.divisionType), training.divisionsCount));
				country.EmitSignal(CountryData.SignalName.TrainingFinished);
				queue.RemoveAt(i);
			}
		}
	}

	public bool PlaceTroop(CountryData country, ReadyTroop batch, int provinceId) =>
		DeployReady(country.countryName, batch, provinceId) != null;

	public void ClearAllTroops()
	{
		foreach (var troopValue in troops)
		{
			var troop = AsTroop(troopValue);
			if (troop != null)
				_stop_leg(troop);
		}
		troops.Clear();
		movingTroops.Clear();
		troopsByProvince.Clear();
		troopsByCountry.Clear();
		pathCache.Clear();
		flagCache.Clear();
		EmitSignal(SignalName.TroopsChanged, "");
	}

	// One fixed simulation step per game hour. Visual position moves with the same
	// step; speed_scale and frame delta no longer affect simulation progress.
	public void OnHourPassed()
	{
		if (movingTroops.Count == 0)
			return;

		foreach (var troop in movingTroops.ToArray())
		{
			if (troop.countryObj == null)
				troop.countryObj = CountryObject(troop.countryName);

			if (troop.path.Count == 0)
			{
				_stop_troop(troop);
				continue;
			}

			var targetPid = troop.path[0].Int();
			var enemies = new List<TroopData>();
			foreach (var localValue in ProvinceList(targetPid))
			{
				var local = AsTroop(localValue);
				if (local != null && IsAtWar(local.countryName, troop.countryName))
					enemies.Add(local);
			}

			if (enemies.Count > 0)
			{
				WarManager.Instance.StartBattle(troop.provinceId, targetPid);
				PauseTroop(troop);
				foreach (var enemy in enemies)
					PauseTroop(enemy);
				continue;
			}

			if (!troop.isMoving)
			{
				_start_next_leg(troop);
				continue;
			}

			troop.progress += 1.0f / Mathf.Max(troop.legHours, 1.0f);
			if (troop.progress >= 1.0f)
			{
				troop.position = troop.targetPosition;
				_arrive_at_leg_end(troop);
			}
			else
			{
				troop.position = troop.legStart.Lerp(troop.targetPosition, troop.progress);
			}
		}
	}

	public void _stop_leg(TroopData troop)
	{
		troop.progress = 0.0f;
	}

	public void _start_leg(TroopData troop)
	{
		_stop_leg(troop);
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			return;
		}

		troop.legStart = troop.position;
		if (troop.legStart.DistanceTo(troop.targetPosition) < 0.5f)
		{
			troop.position = troop.targetPosition;
			_arrive_at_leg_end(troop);
			return;
		}

		var speedMod = troop.countryObj != null ? ((GodotObject)troop.countryObj).Get("troopSpeedModifier").AsSingle() : 1.0f;
		troop.legHours = Mathf.Max(troop.legStart.DistanceTo(troop.targetPosition) /
			Mathf.Max(speedMod, 0.001f), 1.0f);
		troop.isMoving = true;
		if (!movingTroops.Contains(troop))
			movingTroops.Add(troop);
	}

	public void _start_next_leg(TroopData troop)
	{
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			return;
		}

		var nextPid = troop.path[0].Int();

		// Check for Combat (WarManager logic)
		var enemies = new List<TroopData>();
		foreach (var localValue in ProvinceList(nextPid))
		{
			var local = AsTroop(localValue);
			if (local != null && IsAtWar(local.countryName, troop.countryName))
				enemies.Add(local);
		}

		var clock = CurrentClock();
		if (enemies.Count > 0 && (clock == null || !clock.paused))
		{
			WarManager.Instance.StartBattle(troop.provinceId, nextPid);
			PauseTroop(troop);
			foreach (var enemy in enemies)
				PauseTroop(enemy);
			return;
		}

		var centers = MapManager.Instance.provinceCenters;
		troop.targetPosition = centers.TryGetValue(nextPid, out var center) ? center.ToCore() : troop.position;
		troop.isMoving = true;
		if (!movingTroops.Contains(troop))
			movingTroops.Add(troop);
		_start_leg(troop);
	}

	public void _arrive_at_leg_end(TroopData troop)
	{
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			return;
		}

		// Logic move: Update which province the troop is 'officially' in
		var arrivedPid = troop.path[0].Int();
		troop.path.RemoveAt(0);
		_move_troop_to_province_logically(troop, arrivedPid);

		// Trigger occupation/events
		WarManager.Instance.ResolveProvinceArrival(arrivedPid, troop);

		// Check if we keep going or stop
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			if (AUTO_MERGE)
				_auto_merge_in_province(troop.provinceId, troop.countryName);
		}
		else
			_start_next_leg(troop);
	}

	public void _stop_troop(TroopData troop)
	{
		_stop_leg(troop);
		movingTroops.Remove(troop);
		troop.isMoving = false;
		troop.path.Clear();
		troop.targetPosition = troop.position;
	}

	// Pause a troop along its path
	public void PauseTroop(TroopData troop)
	{
		_stop_leg(troop);
		movingTroops.Remove(troop);
		troop.targetPosition = troop.position;
		troop.isMoving = false;
	}

	public void CommandMoveAssigned(List<Dictionary<string, object?>> payload)
	{
		if (payload.Count == 0)
			return;

		// 1. Group the payload by troop
		// We need this because one source troop might be the "parent" for 5 different moves
		var troopToMoves = new Dictionary<TroopData, List<Dictionary<string, object?>>>();
		foreach (var entryValue in payload)
		{
			var entry = entryValue;
			var troop = entry.ContainsKey("troop") ? AsTroop(entry["troop"]) : null;
			if (troop == null)
				continue;
			if (!troopToMoves.TryGetValue(troop, out var moves))
			{
				moves = new List<Dictionary<string, object?>>();
				troopToMoves[troop] = moves;
			}
			moves.Add(entry);
		}

		// 2. Process each source troop
		foreach (var pair in troopToMoves)
		{
			var troop = pair.Key;
			var moves = pair.Value;

			// Sort moves so that the one requiring the MOST divisions happens last
			// This allows us to keep the original troop node for the "main" objective
			moves.Sort((a, b) => DivisionCount(a).CompareTo(DivisionCount(b)));

			for (var i = 0; i < moves.Count; i++)
			{
				var moveData = moves[i];
				var requestedCount = DivisionCount(moveData);

				// If this is the last move in the list OR we are requesting everything left
				if (i == moves.Count - 1 || requestedCount >= troop.storedDivisions.Count)
				{
					// No splitting needed for the final move; just move the original troop
					_apply_movement_path(troop, moveData["province_id"].Int());
					break;
				}

				// Splitting logic:
				var batch = new List<DivisionData>();
				for (var j = 0; j < requestedCount; j++)
				{
					if (troop.storedDivisions.Count == 0)
						continue;
					var last = troop.storedDivisions[troop.storedDivisions.Count - 1];
					troop.storedDivisions.RemoveAt(troop.storedDivisions.Count - 1); // Take from the end
					batch.Add(last);
				}

				if (batch.Count == 0)
					continue;

				// Create a new troop node for this small "detachment"
				_apply_movement_path(_create_new_split_troop(troop, batch), moveData["province_id"].Int());
			}
		}
	}

	// Helper to keep your code clean
	public void _apply_movement_path(TroopData troop, int targetPid)
	{
		if (troop.provinceId == targetPid)
		{
			_stop_troop(troop);
			return;
		}

		var allowed = new Godot.Collections.Array<string>();
		if (troop.countryObj != null)
		{
			foreach (var nameValue in ((GodotObject)troop.countryObj).Call("GetAllAllowedCountries").AsGodotArray())
				allowed.Add(nameValue.AsString());
		}
		var path = _get_cached_path(troop.provinceId, targetPid, allowed);

		if (path.Count > 0)
		{
			troop.path = CopyArray(path);
			if (troop.path[0].Int() == troop.provinceId)
				troop.path.RemoveAt(0);

			_start_next_leg(troop);
		}
		else
			_stop_troop(troop);
	}

	public List<object?> _get_cached_path(int startId, int targetId, Godot.Collections.Array<string> allowedCountries)
	{
		if (startId == targetId)
			return new List<object?>();

		// Sort and hash allowed_countries to ensure the cache key is unique per set of permissions.
		var sortedAllowed = new List<string>();
		foreach (var name in allowedCountries)
			sortedAllowed.Add(name);
		sortedAllowed.Sort(StringComparer.Ordinal);
		var allowedHash = string.Join(",", sortedAllowed).GetHashCode();
		var key = $"{startId}_{targetId}_{allowedHash}";

		if (pathCache.TryGetValue(key, out var cachedPath))
			return CopyArray(cachedPath);

		var pathResult = MapManager.Instance.FindPath(startId, targetId, (Godot.Collections.Array)allowedCountries);
		var path = new List<object?>();
		foreach (var pid in pathResult)
			path.Add(pid);

		if (path.Count > 0 && path[0].Int() == startId)
			path.RemoveAt(0);

		if (path.Count > 0)
			pathCache[key] = CopyArray(path);

		return path;
	}

	public TroopData _create_new_split_troop(TroopData original, List<DivisionData> specificDivisions)
	{
		var pos = original.position;

		var newTroop = TroopData.Create(original.countryName, original.provinceId, 0, pos);

		// FIX: Ensure the new split troop knows which country it belongs to
		newTroop.countryObj = original.countryObj;

		// Immediately overwrite the empty array with our specific divisions
		foreach (var division in specificDivisions)
			newTroop.storedDivisions.Add(division);

		// Copy runtime metadata for new troop
		newTroop.isMoving = false;
		newTroop.path = new List<object?>();

		// Register the new troop in all indexes
		troops.Add(newTroop);
		_add_troop_to_indexes(newTroop);

		return newTroop;
	}

	public void _auto_merge_in_province(int provinceId, string country)
	{
		if (!AUTO_MERGE)
			return;

		var candidates = new List<TroopData>();

		// 1. Collect Valid Candidates
		foreach (var troopValue in ProvinceList(provinceId))
		{
			var troop = AsTroop(troopValue);
			if (troop != null && troop.countryName == country && !troop.isMoving)
				candidates.Add(troop);
		}

		if (candidates.Count <= 1)
			return;

		// 2. Pick the BEST Primary (The one to keep)
		var primary = candidates[0];
		TroopData currentSelection = null;

		// Check selection safely
		if (troopSelection != null && HasProperty(troopSelection, "selected_troop"))
			currentSelection = System.Linq.Enumerable.FirstOrDefault(troopSelection.selectedTroops);

		for (var i = 1; i < candidates.Count; i++)
		{
			var current = candidates[i];

			// Prioritize keeping the selected unit
			if (currentSelection != null && currentSelection == current)
			{
				primary = current;
				break;
			}

			// Keep the one with the most divisions
			if (current.divisionsCount > primary.divisionsCount)
				primary = current;
		}

		// 3. Merge others into Primary
		foreach (var troop in candidates)
		{
			if (troop == primary)
				continue;

			// MERGE ARRAYS: Transfer divisions from 'troop' to 'primary'
			foreach (var division in troop.storedDivisions)
				primary.storedDivisions.Add(division);

			// Clear 'troop' divisions so they don't get messy during deletion
			troop.storedDivisions.Clear();

			RemoveTroop(troop);

			// Update selection if we just merged the selected unit into another
			if (currentSelection != null && currentSelection == troop)
			{
				if (troopSelection != null)
					troopSelection.selectedTroops = new List<TroopData> { primary };
			}
		}
	}

	public void RemoveTroop(TroopData troop)
	{
		_stop_leg(troop);
		troops.Remove(troop);
		movingTroops.Remove(troop);

		var pid = troop.provinceId;
		var countryName = troop.countryName;
		var country = CountryObject(countryName);

		if (troopsByProvince.TryGetValue(pid, out var provinceTroops))
		{
			provinceTroops.Remove(troop);
			if (provinceTroops.Count == 0)
				troopsByProvince.Remove(pid);
		}

		if (troopsByCountry.TryGetValue(countryName, out var countryTroops))
			countryTroops.Remove(troop);

		if (country != null)
		{
			foreach (var division in troop.storedDivisions)
			{
				var mobilized = country.Get("mobilized").AsInt32() - (int)(division.hp * division.manpowerPerHP);
				country.Set("mobilized", mobilized);
			}
		}

		EmitSignal(SignalName.TroopsChanged, countryName);
	}

	// Adds a troop reference to the spatial and country dictionaries.
	public void _add_troop_to_indexes(TroopData troop)
	{
		var pid = troop.provinceId;
		var country = troop.countryName;

		// Province Index
		if (!troopsByProvince.TryGetValue(pid, out var provinceTroops))
			troopsByProvince[pid] = provinceTroops = new List<TroopData>();
		provinceTroops.Add(troop);

		// Country Index
		if (!troopsByCountry.TryGetValue(country, out var countryTroops))
			troopsByCountry[country] = countryTroops = new List<TroopData>();
		countryTroops.Add(troop);

		EmitSignal(SignalName.TroopsChanged, country);
	}

	// Updates the troop's location in the spatial index (troops_by_province).
	public void _move_troop_to_province_logically(TroopData troop, int newPid)
	{
		var oldPid = troop.provinceId;
		if (oldPid == newPid)
			return;

		// Remove from old province list
		if (troopsByProvince.TryGetValue(oldPid, out var oldList))
		{
			oldList.Remove(troop);
			if (oldList.Count == 0)
				troopsByProvince.Remove(oldPid);
		}

		// Add to new province list and update troop object
		troop.provinceId = newPid;
		if (!troopsByProvince.TryGetValue(newPid, out var newList))
			troopsByProvince[newPid] = newList = new List<TroopData>();
		newList.Add(troop);
	}

	// Careful using this
	public void TeleportTroopToProvince(TroopData troop, int targetPid)
	{
		_stop_leg(troop);
		movingTroops.Remove(troop);
		// Remove from old province index
		var oldPid = troop.provinceId;
		if (troopsByProvince.TryGetValue(oldPid, out var oldList))
		{
			oldList.Remove(troop);
			if (oldList.Count == 0)
				troopsByProvince.Remove(oldPid);
		}

		// Update troop province
		troop.provinceId = targetPid;

		// Update troop position immediately to center of target province
		var centers = MapManager.Instance.provinceCenters;
		troop.position = centers.TryGetValue(targetPid, out var center) ? center.ToCore() : Vec2.Zero;
		troop.targetPosition = troop.position;
		troop.path.Clear();
		troop.isMoving = false;

		// Add to new province index
		if (!troopsByProvince.TryGetValue(targetPid, out var targetList))
			troopsByProvince[targetPid] = targetList = new List<TroopData>();
		targetList.Add(troop);
	}

	public Godot.Collections.Array GetSerializedTroopsForProvince(int pid)
	{
		var troopData = new Godot.Collections.Array();
		foreach (var troopValue in ProvinceList(pid))
		{
			var troop = AsTroop(troopValue);
			if (troop != null)
				troopData.Add(VariantState.ToVariant(troop.ToDict()));
		}
		return troopData;
	}

	public void LoadTroopsForProvince(int pid, Godot.Collections.Array troopData)
	{
		// Clear existing troops in this province if any (might not be needed if loading fresh)
		// But generally we should probably clear first
		if (troopsByProvince.TryGetValue(pid, out var existingTroops))
		{
			foreach (var troop in existingTroops.ToArray())
				RemoveTroop(troop);
		}

		var centers = MapManager.Instance.provinceCenters;
		foreach (var dataValue in troopData)
		{
			var data = dataValue.AsGodotDictionary().AsState();
			var troop = TroopData.FromDict(data);
			troop.provinceId = pid;
			troop.position = centers.TryGetValue(pid, out var center) ? center.ToCore() : Vec2.Zero;
			if (troop.isMoving)
			{
				var target = data["target_position"].List();
				troop.targetPosition = new Vec2(target[0].Single(), target[1].Single());
			}
			else
				troop.targetPosition = troop.position;

			// Ensure country_obj is assigned
			var countryRef = CountryObject(troop.countryName);
			if (countryRef == null)
			{
				GD.PushError($"TroopManager: Failed to load troop in province {pid} - country '{troop.countryName}' not found!");
				continue;
			}
			troop.countryObj = countryRef;

			// Add to managers
			troops.Add(troop);
			_add_troop_to_indexes(troop);

			if (troop.isMoving && !movingTroops.Contains(troop))
				movingTroops.Add(troop);
		}
	}

	// extra helper functions. Not made by AI
	public List<TroopData> GetTroopsForCountry(string country)
		=> CountryList(country);

	public List<TroopData> GetTroopsInProvince(int provinceId)
		=> ProvinceList(provinceId);

	public int GetProvinceStrength(int pid, string country)
	{
		var total = 0;
		foreach (var troopValue in ProvinceList(pid))
		{
			var troop = AsTroop(troopValue);
			if (troop != null && troop.countryName == country)
				total += troop.divisionsCount;
		}
		return total;
	}

	public TroopData DeployReady(string country, ReadyTroop readyTroops, int provId)
	{
		var countryData = CountryObject(country);
		var centers = MapManager.Instance.provinceCenters;
		var pos = centers.TryGetValue(provId, out var center) ? center.ToCore() : Vec2.Zero;
		var ideology = countryData != null ? countryData.Get("ideologyName").AsString() : "";

		// 1. Create the container (TroopData) with 0 divisions initially
		var troop = TroopData.Create(country, provId, 0, pos);

		// 2. Inject the specific divisions we trained
		for (var i = 0; i < readyTroops.count; i++)
			troop.storedDivisions.Add(readyTroops.division);

		// 3. Setup Runtime Metadata
		troop.isMoving = false;
		troop.path = new List<object?>();
		troop.provinceId = provId;

		// 4. Register
		troops.Add(troop);
		_add_troop_to_indexes(troop);

		if (AUTO_MERGE)
			_auto_merge_in_province(provId, country);

		return troop;
	}

	public TroopData DeploySpecificDivisions(string country, List<DivisionData> divisionsToDeploy, int provId)
	{
		if (divisionsToDeploy.Count == 0)
			return null;

		var countryData = CountryObject(country);
		var centers = MapManager.Instance.provinceCenters;
		var pos = centers.TryGetValue(provId, out var center) ? center.ToCore() : Vec2.Zero;
		var ideology = countryData != null ? countryData.Get("ideologyName").AsString() : "";

		// 1. Create the container (TroopData) with 0 divisions initially
		var troop = TroopData.Create(country, provId, 0, pos);

		// 2. Inject the specific divisions we trained
		foreach (var divisionValue in divisionsToDeploy)
		{
			var division = divisionValue;
			if (division != null)
				troop.storedDivisions.Add(division);
		}

		// 3. Setup Runtime Metadata
		troop.isMoving = false;
		troop.path = new List<object?>();
		troop.provinceId = provId;

		// 4. Register
		troops.Add(troop);
		_add_troop_to_indexes(troop);

		if (AUTO_MERGE)
			_auto_merge_in_province(provId, country);

		return troop;
	}

	public override void _Ready()
	{
		_load_flag_redirects();
	}

	public void _load_flag_redirects()
	{
		var path = "res://assets/flags/flag_redirects.json";
		var data = MapData.Instance.LoadJSON(path);
		if (data.VariantType == Variant.Type.Dictionary)
			flagRedirects = data.AsGodotDictionary();
	}

	public void SetCustomFlagPath(string path)
	{
		if (path != "" && !path.EndsWith("/", StringComparison.Ordinal))
			path += "/";
		customFlagPath = path;
		flagCache.Clear(); // Clear cache to force reload from new path
	}

	// The GDScript default argument is split into an overload: Godot C# does not
	// register optional parameter defaults, so 1-argument call sites need this.
	public Texture2D GetFlag(string country) => GetFlag(country, "");

	public Texture2D GetFlag(string country, string ideology)
	{
		// Normalize the keys
		country = country.ToLowerInvariant();
		ideology = ideology.ToLowerInvariant();

		// Cache key needs to include ideology if provided
		var cacheKey = ideology != "" ? $"{country}_{ideology}" : country;

		// If already cached -> return it
		if (flagCache.TryGetValue(cacheKey, out var cachedFlag))
			return cachedFlag;

		// 0. Check Redirects
		if (flagRedirects.ContainsKey(country))
		{
			var redirect = flagRedirects[country].AsGodotDictionary();
			var targetCountry = redirect["target"].AsString();
			var targetIdeology = redirect["ideology"].AsString();

			return ideology != ""
				? GetFlag(targetCountry, ideology)
				: GetFlag(targetCountry, targetIdeology);
		}

		// Helper to check both custom and default locations
		string FindResource(string subPath)
		{
			if (customFlagPath != "")
			{
				var fullCustom = customFlagPath + subPath;
				if (ResourceLoader.Exists(fullCustom))
					return fullCustom;
			}
			var fullDefault = $"res://assets/flags/{subPath}";
			return ResourceLoader.Exists(fullDefault) ? fullDefault : "";
		}

		Texture2D LoadFlag(string resourcePath)
		{
			var tex = ResourceLoader.Load<Texture2D>(resourcePath);
			flagCache[cacheKey] = tex;
			return tex;
		}

		// 1. Try Specific Ideology Flag: {path}/country/ideology_flag.png
		var path = "";
		if (ideology != "")
		{
			path = FindResource($"{country}/{ideology}_flag.png");
			if (path != "")
				return LoadFlag(path);
		}

		// 2. Try Neutral/Default Flag in new structure: {path}/country/neutral_flag.png
		path = FindResource($"{country}/neutral_flag.png");
		if (path != "")
			return LoadFlag(path);

		// 3. Fallback: Suffix Stripping / Semantic Fallback
		var suffixes = new Godot.Collections.Dictionary
		{
			{ "_kingdom", "monarchist" },
			{ "_empire", "monarchist" },
			{ "_republic", "liberal" },
			{ "_commune", "communist" },
			{ "_union", "communist" },
			{ "_socialist", "communist" },
			{ "_fascist", "facist" },
			{ "_national", "facist" },
			{ "_democratic", "liberal" }
		};

		foreach (var suffixValue in suffixes.Keys)
		{
			var suffix = suffixValue.AsString();
			if (!country.EndsWith(suffix, StringComparison.Ordinal))
				continue;

			var baseCountry = country.Substring(0, country.Length - suffix.Length);
			var fallbackIdeology = suffixes[suffixValue].AsString();
			var checkIdeology = ideology != "" ? ideology : fallbackIdeology;

			var tex = GetFlag(baseCountry, checkIdeology);
			if (tex != null)
			{
				flagCache[cacheKey] = tex;
				return tex;
			}

			if (ideology != "")
			{
				tex = GetFlag(baseCountry, "");
				if (tex != null)
				{
					flagCache[cacheKey] = tex;
					return tex;
				}
			}
		}

		// 4. Fallback to old flat structure (just in case): {path}/country_flag.png
		path = FindResource($"{country}_flag.png");
		if (path == "")
			path = FindResource("fallback_flag.png");

		if (path != "")
			return LoadFlag(path);

		return null;
	}

	public TroopData FindTroopOwningDivision(DivisionData divToFind)
	{
		foreach (var troopValue in troops)
		{
			var troop = AsTroop(troopValue);
			if (troop == null)
				continue;
			foreach (var division in troop.storedDivisions)
			{
				if (ReferenceEquals(division, divToFind))
					return troop;
			}
		}
		return null;
	}

	private static TroopData? AsTroop(object? value) => value as TroopData;

	private List<TroopData> ProvinceList(int pid)
	{
		var result = new List<TroopData>();
		if (troopsByProvince.TryGetValue(pid, out var troops))
			foreach (var troop in troops)
				result.Add(troop);
		return result;
	}

	private List<TroopData> CountryList(string country)
	{
		var result = new List<TroopData>();
		if (troopsByCountry.TryGetValue(country, out var troops))
			foreach (var troop in troops)
				result.Add(troop);
		return result;
	}

	private static RefCounted CountryObject(string name)
	{
		return CountryManager.Instance.GetCountry(name);
	}

	private static GameClock CurrentClock()
	{
		var clock = GameState.Instance.currentWorld?.clock;
		return clock != null && GodotObject.IsInstanceValid(clock) ? clock : null;
	}

	private static bool IsAtWar(string a, string b)
		=> WarManager.Instance.IsAtWarNames(a, b);

	private static int DivisionCount(Dictionary<string, object?> entry)
		=> entry.ContainsKey("divisions") ? entry["divisions"].Int() : 1;

	private static List<object?> CopyArray(object source)
	{
		var copy = new List<object?>();
		if (source is Godot.Collections.Array array)
			foreach (var value in array)
				copy.Add(value.AsState());
		else if (source is List<object?> list)
			copy.AddRange(list);
		return copy;
	}


	private static bool HasProperty(GodotObject obj, string name)
	{
		foreach (var propertyValue in obj.GetPropertyList())
		{
			var property = propertyValue;
			if (property.ContainsKey("name") && property["name"].AsString() == name)
				return true;
		}
		return false;
	}
}
