using Godot;
using Sosr;
using System;
using System.Collections.Generic;

namespace Sosr;

public partial class TroopManager : Node
{
	public static TroopManager Instance => Autoload.Get<TroopManager>("TroopManager");

	// Emitted when a country's troops or division counts change.
	// countryName is "" when every troop was cleared.
	[Signal]
	public delegate void TroopsChangedEventHandler(string countryName);

	public bool AUTO_MERGE = true;

	public bool emptyLatch = false;

	public Godot.Collections.Array to_remove = new();
	public Godot.Collections.Array troops = new();
	public List<TroopData> moving_troops = new();
	public Dictionary<int, List<TroopData>> troops_by_province = new(); // { province_id: [TroopData, ...] }
	public Dictionary<string, List<TroopData>> troops_by_country = new(); // { country_name: [TroopData, ...] }

	public Dictionary<string, Godot.Collections.Array> path_cache = new(); // { cache_key: path_array }
	public Dictionary<string, Texture2D> flag_cache = new(); // { cache_key: texture }

	// TroopSelection is still a GDScript node; accessed dynamically.
	public TroopSelection troop_selection;

	// Used by popup for now
	public Godot.Collections.Dictionary flag_redirects = new();

	public string custom_flag_path = "";

	public void clear_all_troops()
	{
		foreach (var troopValue in troops)
		{
			var troop = AsTroop(troopValue);
			if (troop != null)
				_kill_move_tween(troop);
		}
		troops.Clear();
		moving_troops.Clear();
		troops_by_province.Clear();
		troops_by_country.Clear();
		path_cache.Clear();
		flag_cache.Clear();
		EmitSignal(SignalName.TroopsChanged, "");
	}

	public override void _Process(double delta)
	{
		if (moving_troops.Count == 0)
			return;

		var clock = CurrentClock();
		var paused = clock == null || clock.paused;
		var timeScale = clock != null ? Mathf.Max(clock.time_scale, 0.001f) : 1.0f;

		foreach (var troop in moving_troops.ToArray())
		{
			if (!GodotObject.IsInstanceValid(troop))
			{
				moving_troops.Remove(troop);
				continue;
			}

			if (troop.country_obj == null)
				troop.country_obj = CountryObject(troop.country_name);

			if (troop.path.Count == 0)
			{
				_stop_troop(troop);
				continue;
			}

			var targetPid = troop.path[0].AsInt32();
			var enemies = new List<TroopData>();
			foreach (var localValue in ProvinceList(targetPid))
			{
				var local = AsTroop(localValue);
				if (local != null && IsAtWar(local.country_name, troop.country_name))
					enemies.Add(local);
			}

			if (enemies.Count > 0 && !paused)
			{
				WarManager.Instance.start_battle(troop.province_id, targetPid);
				pause_troop(troop);
				foreach (var enemy in enemies)
					pause_troop(enemy);
				continue;
			}

			var tw = troop.move_tween;
			// A Tween that just finished still reports is_valid() == true while its
			// finished signal is being processed, but is_running() is already false.
			// Treat that as unusable or play() would be called on a finished Tween.
			if (tw == null || !GodotObject.IsInstanceValid(tw) || !tw.IsValid() || !tw.IsRunning())
			{
				if (paused)
					continue;
				_start_leg_tween(troop);
				tw = troop.move_tween;
			}

			if (tw != null && GodotObject.IsInstanceValid(tw) && tw.IsValid())
			{
				// Pause via speed_scale rather than pause()/play(): a Tween at speed
				// 0 still counts as running, so resuming never calls play() on one
				// that may have finished in the meantime.
				tw.SetSpeedScale(paused ? 0.0f : timeScale);
			}
		}
	}

	public void _kill_move_tween(TroopData troop)
	{
		var tw = troop.move_tween;
		// Clear the reference first, so a finished callback from this Tween can no
		// longer be mistaken for the current leg.
		troop.move_tween = null;
		if (tw != null && GodotObject.IsInstanceValid(tw) && tw.IsValid())
			tw.Kill();
	}

	public void _start_leg_tween(TroopData troop)
	{
		_kill_move_tween(troop);
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			return;
		}

		var start = troop.position;
		var end = troop.target_position;
		var totalDist = start.DistanceTo(end);
		if (totalDist < 0.5f)
		{
			troop.position = end;
			_arrive_at_leg_end(troop);
			return;
		}

		var speedMod = troop.country_obj != null ? troop.country_obj.Get("troop_speed_modifier").AsSingle() : 1.0f;
		var duration = Mathf.Max(totalDist / Mathf.Max(1.0f * speedMod, 0.001f), 0.001f);

		var tw = CreateTween();
		tw.SetProcessMode(Tween.TweenProcessMode.Idle);
		tw.TweenProperty(troop, "position", end, duration);
		tw.Connect(Tween.SignalName.Finished, Callable.From(() => _on_leg_tween_finished(troop, tw)));
		troop.move_tween = tw;

		var clock = CurrentClock();
		if (clock != null)
			tw.SetSpeedScale(clock.paused ? 0.0f : Mathf.Max(clock.time_scale, 0.001f));
	}

	public void _on_leg_tween_finished(TroopData troop, Tween sourceTween)
	{
		if (!GodotObject.IsInstanceValid(troop))
			return;
		// While the finished signal is being emitted the Tween still reports
		// is_valid() == true, so is_valid() cannot be used to tell a genuine finish
		// from a stale callback. Identity is the reliable check here.
		if (troop.move_tween != sourceTween)
			return;
		troop.move_tween = null;
		if (!troop.is_moving)
			return;
		troop.position = troop.target_position;
		_arrive_at_leg_end(troop);
	}

	public void _start_next_leg(TroopData troop)
	{
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			return;
		}

		var nextPid = troop.path[0].AsInt32();

		// Check for Combat (WarManager logic)
		var enemies = new List<TroopData>();
		foreach (var localValue in ProvinceList(nextPid))
		{
			var local = AsTroop(localValue);
			if (local != null && IsAtWar(local.country_name, troop.country_name))
				enemies.Add(local);
		}

		var clock = CurrentClock();
		if (enemies.Count > 0 && (clock == null || !clock.paused))
		{
			WarManager.Instance.start_battle(troop.province_id, nextPid);
			pause_troop(troop);
			foreach (var enemy in enemies)
				pause_troop(enemy);
			return;
		}

		var centers = MapManager.Instance.province_centers;
		troop.target_position = centers.TryGetValue(nextPid, out var center) ? center : troop.position;
		troop.is_moving = true;
		if (!moving_troops.Contains(troop))
			moving_troops.Add(troop);
		_start_leg_tween(troop);
	}

	public void _arrive_at_leg_end(TroopData troop)
	{
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			return;
		}

		// Logic move: Update which province the troop is 'officially' in
		var arrivedPid = troop.path[0].AsInt32();
		troop.path.RemoveAt(0);
		_move_troop_to_province_logically(troop, arrivedPid);

		// Trigger occupation/events
		WarManager.Instance.resolve_province_arrival(arrivedPid, troop);

		// Check if we keep going or stop
		if (troop.path.Count == 0)
		{
			_stop_troop(troop);
			if (AUTO_MERGE)
				_auto_merge_in_province(troop.province_id, troop.country_name);
		}
		else
			_start_next_leg(troop);
	}

	public void _stop_troop(TroopData troop)
	{
		_kill_move_tween(troop);
		moving_troops.Remove(troop);
		troop.is_moving = false;
		troop.path.Clear();
		troop.target_position = troop.position;
	}

	// Pause a troop along its path
	public void pause_troop(TroopData troop)
	{
		_kill_move_tween(troop);
		moving_troops.Remove(troop);
		troop.target_position = troop.position;
		troop.is_moving = false;
	}

	public void command_move_assigned(Godot.Collections.Array payload)
	{
		if (payload.Count == 0)
			return;

		// 1. Group the payload by troop
		// We need this because one source troop might be the "parent" for 5 different moves
		var troopToMoves = new Dictionary<TroopData, List<Godot.Collections.Dictionary>>();
		foreach (var entryValue in payload)
		{
			var entry = entryValue.AsGodotDictionary();
			var troop = entry.ContainsKey("troop") ? AsTroop(entry["troop"]) : null;
			if (troop == null)
				continue;
			if (!troopToMoves.TryGetValue(troop, out var moves))
			{
				moves = new List<Godot.Collections.Dictionary>();
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
				if (i == moves.Count - 1 || requestedCount >= troop.stored_divisions.Count)
				{
					// No splitting needed for the final move; just move the original troop
					_apply_movement_path(troop, moveData["province_id"].AsInt32());
					break;
				}

				// Splitting logic:
				var batch = new Godot.Collections.Array();
				for (var j = 0; j < requestedCount; j++)
				{
					if (troop.stored_divisions.Count == 0)
						continue;
					var last = troop.stored_divisions[troop.stored_divisions.Count - 1];
					troop.stored_divisions.RemoveAt(troop.stored_divisions.Count - 1); // Take from the end
					batch.Add(last);
				}

				if (batch.Count == 0)
					continue;

				// Create a new troop node for this small "detachment"
				_apply_movement_path(_create_new_split_troop(troop, batch), moveData["province_id"].AsInt32());
			}
		}
	}

	// Helper to keep your code clean
	public void _apply_movement_path(TroopData troop, int targetPid)
	{
		if (troop.province_id == targetPid)
		{
			_stop_troop(troop);
			return;
		}

		var allowed = new Godot.Collections.Array<string>();
		if (troop.country_obj != null)
		{
			foreach (var nameValue in troop.country_obj.Call("get_all_allowed_countries").AsGodotArray())
				allowed.Add(nameValue.AsString());
		}
		var path = _get_cached_path(troop.province_id, targetPid, allowed);

		if (path.Count > 0)
		{
			troop.path = CopyArray(path);
			if (troop.path[0].AsInt32() == troop.province_id)
				troop.path.RemoveAt(0);

			troop.SetMeta("start_pos", troop.position);
			_start_next_leg(troop);
		}
		else
			_stop_troop(troop);
	}

	public Godot.Collections.Array _get_cached_path(int startId, int targetId, Godot.Collections.Array<string> allowedCountries)
	{
		if (startId == targetId)
			return new Godot.Collections.Array();

		// Sort and hash allowed_countries to ensure the cache key is unique per set of permissions.
		var sortedAllowed = new List<string>();
		foreach (var name in allowedCountries)
			sortedAllowed.Add(name);
		sortedAllowed.Sort(StringComparer.Ordinal);
		var allowedHash = string.Join(",", sortedAllowed).GetHashCode();
		var key = $"{startId}_{targetId}_{allowedHash}";

		if (path_cache.TryGetValue(key, out var cachedPath))
			return CopyArray(cachedPath);

		var pathResult = MapManager.Instance.find_path(startId, targetId, (Godot.Collections.Array)allowedCountries);
		var path = new Godot.Collections.Array();
		foreach (var pid in pathResult)
			path.Add(pid);

		if (path.Count > 0 && path[0].AsInt32() == startId)
			path.RemoveAt(0);

		if (path.Count > 0)
			path_cache[key] = CopyArray(path);

		return path;
	}

	public void _split_and_send_troop(TroopData troop, Godot.Collections.Array targetPids, Godot.Collections.Dictionary paths)
	{
		var numTargets = targetPids.Count;
		var totalDivs = troop.divisions_count;

		if (numTargets == 0 || totalDivs < numTargets)
			return;

		// 1. Sort targets by distance to move the "heaviest" part of the stack the shortest distance
		var targetDistances = new List<(int pid, float dist)>();
		var graph = MapManager.Instance.province_graph;
		var troopPos = graph.GetPointPosition(troop.province_id);
		foreach (var pidValue in targetPids)
		{
			var pid = pidValue.AsInt32();
			var dist = troopPos.DistanceTo(graph.GetPointPosition(pid));
			targetDistances.Add((pid, dist));
		}
		targetDistances.Sort((a, b) => a.dist.CompareTo(b.dist));

		// 2. Calculate distribution
		var baseCount = totalDivs / numTargets;
		var remainder = totalDivs % numTargets;

		var originalUsed = false;
		var currentDivIndex = 0;

		// We duplicate the array reference so we can slice it safely
		var allDivisions = new List<DivisionData>();
		foreach (var div in troop.stored_divisions)
			allDivisions.Add(div);

		for (var i = 0; i < numTargets; i++)
		{
			var pid = targetDistances[i].pid;

			// Determine how many divisions go to this specific target
			var countForThisLeg = baseCount;
			if (i < remainder)
				countForThisLeg += 1;

			// SLICE: Take the specific objects for this batch
			var divisionsForThisLeg = new Godot.Collections.Array<DivisionData>();
			for (var d = 0; d < countForThisLeg && currentDivIndex < allDivisions.Count; d++, currentDivIndex++)
				divisionsForThisLeg.Add(allDivisions[currentDivIndex]);

			TroopData troopToMove;
			if (!originalUsed)
			{
				// The original troop instance stays alive and takes the first batch
				troopToMove = troop;
				troopToMove.stored_divisions = divisionsForThisLeg;
				originalUsed = true;
			}
			else
			{
				// Create a brand new TroopData for the other batches
				// This function must handle country_obj assignment!
				troopToMove = _create_new_split_troop(troop, ToUntyped(divisionsForThisLeg));
			}

			// 3. Assign movement
			if (pid == troopToMove.province_id)
			{
				// This part of the split is staying in the current province
				troopToMove.path.Clear();
				_stop_troop(troopToMove);
				if (AUTO_MERGE)
					_auto_merge_in_province(pid, troopToMove.country_name);
			}
			else
			{
				// This part of the split is moving to a new target
				var path = paths.ContainsKey(pid) ? paths[pid].AsGodotArray() : new Godot.Collections.Array();
				if (path.Count > 0)
				{
					var newPath = CopyArray(path);
					// If the path starts with current province, remove it
					if (newPath[0].AsInt32() == troopToMove.province_id)
						newPath.RemoveAt(0);

					troopToMove.path = newPath;
					_start_next_leg(troopToMove);
				}
				else
					_stop_troop(troopToMove);
			}
		}
	}

	public TroopData _create_new_split_troop(TroopData original, Godot.Collections.Array specific_divisions)
	{
		var pos = original.position;

		var newTroop = TroopData.Create(
			original.country_name, original.province_id, 0, pos, get_flag(original.country_name)
		);

		// FIX: Ensure the new split troop knows which country it belongs to
		newTroop.country_obj = original.country_obj;

		// Immediately overwrite the empty array with our specific divisions
		foreach (var divisionValue in specific_divisions)
		{
			var division = divisionValue.AsGodotObject() as DivisionData;
			if (division != null)
				newTroop.stored_divisions.Add(division);
		}

		// Copy runtime metadata for new troop
		newTroop.is_moving = false;
		newTroop.path = new Godot.Collections.Array();

		// Register the new troop in all indexes
		troops.Add(newTroop);
		_add_troop_to_indexes(newTroop);

		return newTroop;
	}

	public TroopData create_troop(string country, int divs, int provId)
	{
		if (divs <= 0)
			return null;

		var countryData = CountryObject(country);
		var centers = MapManager.Instance.province_centers;
		var pos = centers.TryGetValue(provId, out var center) ? center : Vector2.Zero;
		var ideology = countryData != null ? countryData.Get("ideology_name").AsString() : "";

		var troop = TroopData.Create(
			country, provId, divs, pos, get_flag(country, ideology)
		);

		// FIX: Assign the country object reference
		troop.country_obj = countryData;

		// Initialize runtime metadata
		troop.is_moving = false;
		troop.path = new Godot.Collections.Array();
		troop.province_id = provId;

		troops.Add(troop);
		_add_troop_to_indexes(troop);

		if (AUTO_MERGE)
			_auto_merge_in_province(provId, country);

		return troop;
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
			if (troop != null && troop.country_name == country && !troop.is_moving)
				candidates.Add(troop);
		}

		if (candidates.Count <= 1)
			return;

		// 2. Pick the BEST Primary (The one to keep)
		var primary = candidates[0];
		TroopData currentSelection = null;

		// Check selection safely
		if (troop_selection != null && HasProperty(troop_selection, "selected_troop"))
			currentSelection = troop_selection.Get("selected_troop").AsGodotObject() as TroopData;

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
			if (current.divisions_count > primary.divisions_count)
				primary = current;
		}

		// 3. Merge others into Primary
		foreach (var troop in candidates)
		{
			if (troop == primary)
				continue;

			// MERGE ARRAYS: Transfer divisions from 'troop' to 'primary'
			foreach (var division in troop.stored_divisions)
				primary.stored_divisions.Add(division);

			// Clear 'troop' divisions so they don't get messy during deletion
			troop.stored_divisions.Clear();

			RemoveTroop(troop);

			// Update selection if we just merged the selected unit into another
			if (currentSelection != null && currentSelection == troop)
			{
				if (troop_selection != null && troop_selection.HasMethod("select_troop"))
					troop_selection.Call("select_troop", primary);
				else if (troop_selection != null && HasProperty(troop_selection, "selected_troop"))
					troop_selection.Set("selected_troop", primary);
			}
		}
	}

	public void RemoveTroop(TroopData troop)
	{
		_kill_move_tween(troop);
		troops.Remove(troop);
		moving_troops.Remove(troop);

		var pid = troop.province_id;
		var countryName = troop.country_name;
		var country = CountryObject(countryName);

		if (troops_by_province.TryGetValue(pid, out var provinceTroops))
		{
			provinceTroops.Remove(troop);
			if (provinceTroops.Count == 0)
				troops_by_province.Remove(pid);
		}

		if (troops_by_country.TryGetValue(countryName, out var countryTroops))
			countryTroops.Remove(troop);

		if (country != null)
		{
			foreach (var division in troop.stored_divisions)
			{
				var mobilized = country.Get("mobilized").AsInt32() - (int)(division.hp * division.manpowerPerHP);
				country.Set("mobilized", mobilized);
			}
		}

		EmitSignal(SignalName.TroopsChanged, countryName);
	}

	// Public hook for the WarManager to force a troop to its home province center.
	public void move_to_garrison(TroopData troop)
	{
		var centers = MapManager.Instance.province_centers;
		var center = centers.TryGetValue(troop.province_id, out var c) ? c : troop.position;
		troop.position = center;
		troop.target_position = center;
		_stop_troop(troop); // Stops any ongoing movement
	}

	// Adds a troop reference to the spatial and country dictionaries.
	public void _add_troop_to_indexes(TroopData troop)
	{
		var pid = troop.province_id;
		var country = troop.country_name;

		// Province Index
		if (!troops_by_province.TryGetValue(pid, out var provinceTroops))
			troops_by_province[pid] = provinceTroops = new List<TroopData>();
		provinceTroops.Add(troop);

		// Country Index
		if (!troops_by_country.TryGetValue(country, out var countryTroops))
			troops_by_country[country] = countryTroops = new List<TroopData>();
		countryTroops.Add(troop);

		EmitSignal(SignalName.TroopsChanged, country);
	}

	// Updates the troop's location in the spatial index (troops_by_province).
	public void _move_troop_to_province_logically(TroopData troop, int newPid)
	{
		var oldPid = troop.province_id;
		if (oldPid == newPid)
			return;

		// Remove from old province list
		if (troops_by_province.TryGetValue(oldPid, out var oldList))
		{
			oldList.Remove(troop);
			if (oldList.Count == 0)
				troops_by_province.Remove(oldPid);
		}

		// Add to new province list and update troop object
		troop.province_id = newPid;
		if (!troops_by_province.TryGetValue(newPid, out var newList))
			troops_by_province[newPid] = newList = new List<TroopData>();
		newList.Add(troop);
	}

	// Careful using this
	public void teleport_troop_to_province(TroopData troop, int targetPid)
	{
		_kill_move_tween(troop);
		moving_troops.Remove(troop);
		// Remove from old province index
		var oldPid = troop.province_id;
		if (troops_by_province.TryGetValue(oldPid, out var oldList))
		{
			oldList.Remove(troop);
			if (oldList.Count == 0)
				troops_by_province.Remove(oldPid);
		}

		// Update troop province
		troop.province_id = targetPid;

		// Update troop position immediately to center of target province
		var centers = MapManager.Instance.province_centers;
		troop.position = centers.TryGetValue(targetPid, out var center) ? center : Vector2.Zero;
		troop.target_position = troop.position;
		troop.path.Clear();
		troop.is_moving = false;

		// Add to new province index
		if (!troops_by_province.TryGetValue(targetPid, out var targetList))
			troops_by_province[targetPid] = targetList = new List<TroopData>();
		targetList.Add(troop);
	}

	public Godot.Collections.Array get_serialized_troops_for_province(int pid)
	{
		var troopData = new Godot.Collections.Array();
		foreach (var troopValue in ProvinceList(pid))
		{
			var troop = AsTroop(troopValue);
			if (troop != null)
				troopData.Add(troop.ToDict());
		}
		return troopData;
	}

	public void load_troops_for_province(int pid, Godot.Collections.Array troopData)
	{
		// Clear existing troops in this province if any (might not be needed if loading fresh)
		// But generally we should probably clear first
		if (troops_by_province.TryGetValue(pid, out var existingTroops))
		{
			foreach (var troop in existingTroops.ToArray())
				RemoveTroop(troop);
		}

		var centers = MapManager.Instance.province_centers;
		foreach (var dataValue in troopData)
		{
			var data = dataValue.AsGodotDictionary();
			var troop = TroopData.FromDict(data);
			troop.province_id = pid;
			troop.position = centers.TryGetValue(pid, out var center) ? center : Vector2.Zero;
			if (troop.is_moving)
			{
				var target = data["target_position"].AsGodotArray();
				troop.target_position = new Vector2(target[0].AsSingle(), target[1].AsSingle());
			}
			else
				troop.target_position = troop.position;

			// Ensure country_obj is assigned
			var countryRef = CountryObject(troop.country_name);
			if (countryRef == null)
			{
				GD.PushError($"TroopManager: Failed to load troop in province {pid} - country '{troop.country_name}' not found!");
				continue;
			}
			troop.country_obj = countryRef;

			// Add to managers
			troops.Add(troop);
			_add_troop_to_indexes(troop);

			if (troop.is_moving && !moving_troops.Contains(troop))
				moving_troops.Add(troop);
		}
	}

	public int get_province_division_count(int pid)
	{
		var total = 0;
		foreach (var troopValue in ProvinceList(pid))
		{
			var troop = AsTroop(troopValue);
			if (troop != null)
				total += troop.divisions_count;
		}
		return total;
	}

	public bool have_troops_in_both_provinces(int provinceIdA, int provinceIdB)
		=> troops_by_province.ContainsKey(provinceIdA) && troops_by_province.ContainsKey(provinceIdB);

	public void clear_path_cache()
	{
		path_cache.Clear();
	}

	// Remove leading waypoints that are equal to the troop's current province.
	public Godot.Collections.Array _sanitize_path_for_troop(Godot.Collections.Array path, int startPid)
	{
		if (path == null || path.Count == 0)
			return new Godot.Collections.Array();
		// Duplicate to avoid mutating caller arrays
		var p = CopyArray(path);
		// Pop front while first entry equals start_pid
		while (p.Count > 0 && p[0].AsInt32() == startPid)
			p.RemoveAt(0);
		return p;
	}

	// extra helper functions. Not made by AI
	public Godot.Collections.Array get_troops_for_country(string country)
		=> CountryList(country);

	public Godot.Collections.Array get_troops_in_province(int provinceId)
		=> ProvinceList(provinceId);

	public int get_province_strength(int pid, string country)
	{
		var total = 0;
		foreach (var troopValue in ProvinceList(pid))
		{
			var troop = AsTroop(troopValue);
			if (troop != null && troop.country_name == country)
				total += troop.divisions_count;
		}
		return total;
	}

	public TroopData DeployReady(string country, ReadyTroop readyTroops, int provId)
	{
		var countryData = CountryObject(country);
		var centers = MapManager.Instance.province_centers;
		var pos = centers.TryGetValue(provId, out var center) ? center : Vector2.Zero;
		var ideology = countryData != null ? countryData.Get("ideology_name").AsString() : "";

		// 1. Create the container (TroopData) with 0 divisions initially
		var troop = TroopData.Create(country, provId, 0, pos, get_flag(country, ideology));

		// 2. Inject the specific divisions we trained
		for (var i = 0; i < readyTroops.count; i++)
			troop.stored_divisions.Add(readyTroops.division);

		// 3. Setup Runtime Metadata
		troop.is_moving = false;
		troop.path = new Godot.Collections.Array();
		troop.province_id = provId;

		// 4. Register
		troops.Add(troop);
		_add_troop_to_indexes(troop);

		if (AUTO_MERGE)
			_auto_merge_in_province(provId, country);

		return troop;
	}

	public TroopData deploy_specific_divisions(string country, Godot.Collections.Array divisionsToDeploy, int provId)
	{
		if (divisionsToDeploy.Count == 0)
			return null;

		var countryData = CountryObject(country);
		var centers = MapManager.Instance.province_centers;
		var pos = centers.TryGetValue(provId, out var center) ? center : Vector2.Zero;
		var ideology = countryData != null ? countryData.Get("ideology_name").AsString() : "";

		// 1. Create the container (TroopData) with 0 divisions initially
		var troop = TroopData.Create(country, provId, 0, pos, get_flag(country, ideology));

		// 2. Inject the specific divisions we trained
		foreach (var divisionValue in divisionsToDeploy)
		{
			var division = divisionValue.AsGodotObject() as DivisionData;
			if (division != null)
				troop.stored_divisions.Add(division);
		}

		// 3. Setup Runtime Metadata
		troop.is_moving = false;
		troop.path = new Godot.Collections.Array();
		troop.province_id = provId;

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
			flag_redirects = data.AsGodotDictionary();
	}

	public void set_custom_flag_path(string path)
	{
		if (path != "" && !path.EndsWith("/", StringComparison.Ordinal))
			path += "/";
		custom_flag_path = path;
		flag_cache.Clear(); // Clear cache to force reload from new path
	}

	// The GDScript default argument is split into an overload: Godot C# does not
	// register optional parameter defaults, so 1-argument call sites need this.
	public Texture2D get_flag(string country) => get_flag(country, "");

	public Texture2D get_flag(string country, string ideology)
	{
		// Normalize the keys
		country = country.ToLowerInvariant();
		ideology = ideology.ToLowerInvariant();

		// Cache key needs to include ideology if provided
		var cacheKey = ideology != "" ? $"{country}_{ideology}" : country;

		// If already cached -> return it
		if (flag_cache.TryGetValue(cacheKey, out var cachedFlag))
			return cachedFlag;

		// 0. Check Redirects
		if (flag_redirects.ContainsKey(country))
		{
			var redirect = flag_redirects[country].AsGodotDictionary();
			var targetCountry = redirect["target"].AsString();
			var targetIdeology = redirect["ideology"].AsString();

			return ideology != ""
				? get_flag(targetCountry, ideology)
				: get_flag(targetCountry, targetIdeology);
		}

		// Helper to check both custom and default locations
		string FindResource(string subPath)
		{
			if (custom_flag_path != "")
			{
				var fullCustom = custom_flag_path + subPath;
				if (ResourceLoader.Exists(fullCustom))
					return fullCustom;
			}
			var fullDefault = $"res://assets/flags/{subPath}";
			return ResourceLoader.Exists(fullDefault) ? fullDefault : "";
		}

		Texture2D LoadFlag(string resourcePath)
		{
			var tex = ResourceLoader.Load<Texture2D>(resourcePath);
			flag_cache[cacheKey] = tex;
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

			var tex = get_flag(baseCountry, checkIdeology);
			if (tex != null)
			{
				flag_cache[cacheKey] = tex;
				return tex;
			}

			if (ideology != "")
			{
				tex = get_flag(baseCountry, "");
				if (tex != null)
				{
					flag_cache[cacheKey] = tex;
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

	public TroopData find_troop_owning_division(DivisionData divToFind)
	{
		foreach (var troopValue in troops)
		{
			var troop = AsTroop(troopValue);
			if (troop == null)
				continue;
			foreach (var division in troop.stored_divisions)
			{
				if (ReferenceEquals(division, divToFind))
					return troop;
			}
		}
		return null;
	}

	// --- Small helpers for the dynamic GDScript objects used by this manager ---

	private static TroopData AsTroop(Variant value) => value.AsGodotObject() as TroopData;

	private Godot.Collections.Array ProvinceList(int pid)
	{
		var result = new Godot.Collections.Array();
		if (troops_by_province.TryGetValue(pid, out var troops))
			foreach (var troop in troops)
				result.Add(troop);
		return result;
	}

	private Godot.Collections.Array CountryList(string country)
	{
		var result = new Godot.Collections.Array();
		if (troops_by_country.TryGetValue(country, out var troops))
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
		var clock = GameState.Instance.current_world?.clock;
		return clock != null && GodotObject.IsInstanceValid(clock) ? clock : null;
	}

	private static bool IsAtWar(string a, string b)
		=> WarManager.Instance.is_at_war_names(a, b);

	private static int DivisionCount(Godot.Collections.Dictionary entry)
		=> entry.ContainsKey("divisions") ? entry["divisions"].AsInt32() : 1;

	private static Godot.Collections.Array CopyArray(Godot.Collections.Array source)
	{
		var copy = new Godot.Collections.Array();
		foreach (var value in source)
			copy.Add(value);
		return copy;
	}

	private static Godot.Collections.Array ToUntyped(Godot.Collections.Array<DivisionData> source)
	{
		var result = new Godot.Collections.Array();
		foreach (var division in source)
			result.Add(division);
		return result;
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
