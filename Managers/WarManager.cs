using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

public partial class WarManager : Node
{
	public static WarManager Instance => Autoload.Get<WarManager>("WarManager");

	// --- Constants ---
	public const float BATTLE_TICK = 1.0f;
	public const float MORALE_DECAY_RATE = 0.02f; // Adjusted for better flow
	public const float MORALE_BOOST_DEFENDER = 10.0f;

	// GDScript cannot read C# static constants through an instance.
	// Exposed to the engine as WarDeclared / PeaceMade / PeaceConferenceRequested.
	[Signal]
	public delegate void WarDeclaredEventHandler(GodotObject attacker, GodotObject defender);

	[Signal]
	public delegate void PeaceMadeEventHandler(GodotObject attacker, GodotObject defender);

	[Signal]
	public delegate void PeaceConferenceRequestedEventHandler(Godot.Collections.Array winners, GodotObject loser);

	// MusicManager.SFX / MUSIC enum values (GDScript enum constants are not visible from C#).
	// SFX: TROOP_MOVE=0, TROOP_SELECTED=1, BATTLE_START=2, OPEN_MENU=3, DECLARE_WAR=4,
	// HOVERED=5, CLOSE_MENU=6, GAME_OVER=7, POPUP=8, BUILD=9, CLAPPING=10.
	private const int SfxDeclareWar = 4;
	private const int SfxGameOver = 7;
	private const int SfxPopup = 8;
	// MUSIC: MAIN_THEME=0, BATTLE_THEME=1.
	private const int MusicMainTheme = 0;
	private const int MusicBattleTheme = 1;

	// --- State ---
	public Dictionary<string, HashSet<string>> wars = new();
	public List<Battle> activeBattles = new();
	public Dictionary<string, List<int>> originalTerritories = new();

	public void ResetState()
	{
		wars.Clear();
		foreach (var battle in activeBattles)
			battle.cleanup();
		activeBattles.Clear();
		originalTerritories.Clear();
	}

	public Godot.Collections.Array SaveWars()
	{
		var savedWars = new Godot.Collections.Array();
		var processedPairs = new HashSet<string>();

		foreach (var countryA in wars)
		{
			foreach (var countryB in countryA.Value)
			{
				// Create a sorted pair to avoid saving [A, B] AND [B, A].
				var first = countryA.Key;
				var second = countryB;
				if (string.CompareOrdinal(first, second) > 0)
					(first, second) = (second, first);

				if (!processedPairs.Add($"{first}|{second}"))
					continue;

				savedWars.Add(new Godot.Collections.Array { first, second });
			}
		}

		return savedWars;
	}

	public void LoadWars(Godot.Collections.Array data)
	{
		// wars.Clear() is already handled by reset_state() in load_game().
		var countries = CountryManager.Instance.countries;
		foreach (var pairVariant in data)
		{
			var pair = pairVariant.AsGodotArray();
			if (pair.Count != 2)
				continue;

			var countryA = TryCountry(countries, pair[0].AsString());
			var countryB = TryCountry(countries, pair[1].AsString());
			if (countryA != null && countryB != null)
				AddWarSilent(countryA, countryB);
		}
	}

	public Godot.Collections.Dictionary SaveOriginalTerritories()
	{
		var result = new Godot.Collections.Dictionary();
		foreach (var pair in originalTerritories)
		{
			var pids = new Godot.Collections.Array();
			foreach (var pid in pair.Value)
				pids.Add(pid);
			result[pair.Key] = pids;
		}
		return result;
	}

	public void LoadOriginalTerritories(Godot.Collections.Dictionary data)
	{
		originalTerritories.Clear();
		foreach (var key in data.Keys)
		{
			var pids = new List<int>();
			foreach (var pid in data[key].AsGodotArray())
				pids.Add(pid.AsInt32());
			originalTerritories[key.AsString()] = pids;
		}
	}

	public void CheckForNewBattles()
	{
		if (!GodotObject.IsInstanceValid(MapManager.Instance) || !GodotObject.IsInstanceValid(TroopManager.Instance))
			return;

		var troopsByProvince = TroopManager.Instance.troopsByProvince;
		foreach (var pair in troopsByProvince)
		{
			var provinceTroops = pair.Value;
			if (provinceTroops.Count < 2)
				continue;

			// We need at least two different countries at war in this province.
			for (var i = 0; i < provinceTroops.Count; i++)
			{
				var troopA = provinceTroops[i];
				for (var j = i + 1; j < provinceTroops.Count; j++)
				{
					var troopB = provinceTroops[j];

					if (troopA.countryName != troopB.countryName)
					{
						if (IsAtWarNames(troopA.countryName, troopB.countryName))
						{
							// start_battle checks for existing duplicates internally.
							StartBattle(pair.Key, pair.Key); // In same province, use pid for both.
						}
					}
				}
			}
		}
	}

	// One battle round per game hour. No Timer and no frame delta: peers replay the
	// same rounds from the same seed.
	public void OnHourPassed()
	{
		var clock = GameState.Instance.currentWorld?.clock;
		if (clock != null && clock.paused)
			return;

		foreach (var battle in activeBattles.ToArray())
			battle._resolve_round();
	}

	public void StartBattle(int attackerPid, int defenderPid)
	{
		var clock = GameState.Instance.currentWorld?.clock;
		if (clock != null && clock.Get("paused").AsBool())
			return;

		// Prevent duplicate battles.
		foreach (var battle in activeBattles)
		{
			if (battle.attackerPid == attackerPid && battle.defenderPid == defenderPid)
				return;
		}

		var attTroops = GetTroops(attackerPid);
		var defTroops = GetTroops(defenderPid);
		if (attTroops.Count == 0 || defTroops.Count == 0)
			return;

		var attFirst = attTroops[0];
		var defFirst = defTroops[0];
		if (attFirst == null || defFirst == null)
			return;

		activeBattles.Add(new Battle(
			attackerPid,
			defenderPid,
			attFirst.countryName,
			defFirst.countryName,
			GetProvinceMidpoint(attackerPid, defenderPid),
			this
		));
	}

	public void EndBattle(Battle battle)
	{
		if (activeBattles.Contains(battle))
		{
			battle.cleanup();
			activeBattles.Remove(battle);
		}
	}

	public void ApplyCasualties(int pid, string country, float damageAmount)
	{
		var troopsList = new List<TroopData>();
		foreach (var troopVariant in GetTroops(pid))
		{
			var troop = troopVariant;
			if (troop != null && troop.countryName == country)
				troopsList.Add(troop);
		}

		if (troopsList.Count == 0 || damageAmount <= 0)
			return;

		var countryTime = CountryManager.Instance.GetCountry(country);

		// Distribute total damage among all army stacks in the province.
		var damagePerTroop = damageAmount / troopsList.Count;
		foreach (var troopVariant in troopsList)
		{
			if (troopVariant is not TroopData t)
				continue;

			// Distribute troop damage among all divisions in that stack.
			var damagePerDiv = damagePerTroop / t.storedDivisions.Count;
			var lostDivision = false;

			for (var i = t.storedDivisions.Count - 1; i >= 0; i--)
			{
				var div = t.storedDivisions[i];

				// Deduct HP.
				countryTime.Set("mobilized", countryTime.Get("mobilized").AsInt32() -
					(int)(Mathf.Min(div.hp, damagePerDiv) * div.manpowerPerHP));
				div.hp -= damagePerDiv;

				// Gain Experience based on how much damage they took/dealt.
				div.experience += 0.005f;

				// Remove division if it hits 0 HP.
				if (div.hp <= 0)
				{
					t.storedDivisions.RemoveAt(i);
					lostDivision = true;
				}
			}

			// If the entire stack is gone, remove the troop icon from the map.
			if (t.storedDivisions.Count == 0)
				TroopManager.Instance.RemoveTroop(t);
			else if (lostDivision)
				TroopManager.Instance.EmitSignal("TroopsChanged", country);
		}
	}

	public void ResolveProvinceArrival(int pid, TroopData troop)
	{
		if (troop == null)
			return;

		var provinces = MapManager.Instance.provinceObjects;
		if (!provinces.TryGetValue(pid, out var provinceValue))
			return;
		var targetCountry = FunctionalOwner(provinceValue);

		if (targetCountry != troop.countryName && IsAtWarNames(troop.countryName, targetCountry))
		{
			if (TroopManager.Instance.GetProvinceStrength(pid, targetCountry) <= 0)
			{
				if (!IsAtWarNames(troop.countryName, targetCountry))
					MapManager.Instance.DeoccupyProvince(pid);
				else
					MapManager.Instance.OccupyProvince(pid, troop.countryName);
				CheckCountryCollapse(targetCountry, troop.countryName);
			}
		}
	}

	public void CallToArms(GodotObject caller, GodotObject target)
	{
		if (IsSame(caller, target))
			return;

		// DEAD CHECK: Target must have land to fight.
		var provinces = MapManager.Instance.countryToProvinces;
		if (!HasProvinces(provinces, target.Get("countryName").AsString()) && !IsTrue(target.Get("isExiled")))
			return;

		foreach (var enemyNameVariant in GetEnemiesOf(caller.Get("countryName").AsString()))
		{
			var enemyData = FindCountry(enemyNameVariant.AsString());
			if (enemyData != null)
				DeclareWar(target, enemyData);
		}
	}

	// GDScript exposes the optional a_silent parameter; Godot does not pass C# default
	// values to GDScript callers, so keep both arities as separate registered methods.
	public void DeclareWar(GodotObject warer, GodotObject waree)
	{
		DeclareWar(warer, waree, false);
	}

	public void DeclareWar(GodotObject warer, GodotObject waree, bool aSilent)
	{
		if (IsSame(warer, waree) || IsAtWar(warer, waree) ||
			FactionManager.Instance.InFaction(warer, waree))
			return;

		var provinces = MapManager.Instance.countryToProvinces;
		if (!HasProvinces(provinces, warer.Get("countryName").AsString()) && !IsTrue(warer.Get("isExiled")))
			return;
		if (!HasProvinces(provinces, waree.Get("countryName").AsString()) && !IsTrue(waree.Get("isExiled")))
			return;

		if (!AddWarSilent(warer, waree) || aSilent)
			return;

		var player = CountryManager.Instance.playerCountry;
		if (IsTrue(warer.Get("isPlayer")) || IsTrue(waree.Get("isPlayer")) ||
			IsAtWar(player, warer) || IsAtWar(player, waree))
		{
			EmitSignal(SignalName.WarDeclared, warer, waree);
			// MusicManager.play_music(MUSIC.BATTLE_THEME) -> 1
			// MusicManager.play_sfx(SFX.DECLARE_WAR) -> 4
			// (both commented out in the original GDScript)
		}
	}

	public void _snapshot_country_territory(string cName)
	{
		if (originalTerritories.ContainsKey(cName))
			return;

		var provinces = MapManager.Instance.countryToProvinces;
		var pids = new Godot.Collections.Array();
		var snapshot = new List<int>();
		if (provinces.TryGetValue(cName, out var value))
		{
			foreach (var pid in value)
			{
				pids.Add(pid);
				snapshot.Add(pid);
			}
		}
		originalTerritories[cName] = snapshot;

		// Also store in CountryData if it exists.
		var countryData = FindCountry(cName);
		if (countryData != null && countryData.Get("preWarProvinces").AsGodotArray().Count == 0)
			countryData.Set("preWarProvinces", pids.Duplicate());
	}

	public bool AddWarSilent(GodotObject a, GodotObject b)
	{
		if (IsSame(a, b) || IsAtWar(a, b))
			return false;

		var nameA = CountryData.NameOf(a);
		var nameB = CountryData.NameOf(b);
		if (!wars.TryGetValue(nameA, out var aEnemies))
			wars[nameA] = aEnemies = new HashSet<string>();
		if (!wars.TryGetValue(nameB, out var bEnemies))
			wars[nameB] = bEnemies = new HashSet<string>();
		aEnemies.Add(nameB);
		bEnemies.Add(nameA);
		var aAllowed = a.Get("allowedCountries").AsGodotArray();
		if (!aAllowed.Contains(nameB))
			aAllowed.Add(nameB);
		var bAllowed = b.Get("allowedCountries").AsGodotArray();
		if (!bAllowed.Contains(nameA))
			bAllowed.Add(nameA);

		return true;
	}

	public bool IsAtWar(GodotObject a, GodotObject b)
	{
		if (a == null || b == null)
			return false;
		return wars.TryGetValue(CountryData.NameOf(a), out var enemies) && enemies.Contains(CountryData.NameOf(b));
	}

	public bool IsCountryAtWar(string countryName)
	{
		var countryData = FindCountry(countryName);
		return countryName != "" && wars.TryGetValue(countryName, out var enemies) && enemies.Count > 0;
	}

	public bool IsAtWarNames(string aName, string bName)
	{
		return wars.TryGetValue(aName, out var enemies) && enemies.Contains(bName);
	}

	// API for AI Manager.
	public Godot.Collections.Array GetCountriesAtWar()
	{
		var result = new Godot.Collections.Array();
		foreach (var name in wars.Keys)
			result.Add(name);
		return result;
	}

	// Returns an array of country names that are currently at war with the given country name.
	public Godot.Collections.Array GetEnemiesOf(string countryName)
	{
		var enemies = new Godot.Collections.Array();
		if (wars.TryGetValue(countryName, out var enemyNames))
			foreach (var enemyName in enemyNames)
				enemies.Add(enemyName);
		return enemies;
	}

	public Vector2 GetProvinceMidpoint(int pid1, int pid2)
	{
		var centers = MapManager.Instance.provinceCenters;
		var p1 = centers.TryGetValue(pid1, out var v1) ? v1 : Vector2.Zero;
		var p2 = centers.TryGetValue(pid2, out var v2) ? v2 : Vector2.Zero;
		return (p1 + p2) * 0.5f;
	}

	public void CheckCountryCollapse(string countryName, string victorName)
	{
		if (MapManager.Instance.GetCitiesProvinceCountry(countryName).Count == 0)
			_handle_total_collapse(countryName, victorName);
	}

	public void _handle_total_collapse(string fallenName, string victorName)
	{
		var loser = FindCountry(fallenName);
		var winner = FindCountry(victorName);
		if (loser == null)
			return;

		// NOTE Z21: Fixes some bug that makes this function run multiple times.
		if (!wars.ContainsKey(fallenName))
			return;
		var player = CountryManager.Instance.playerCountry;
		var playerWon = player != null &&
			((winner != null && IsTrue(winner.Get("isPlayer"))) || IsAtWar(player, loser));
		var winners = GetEnemiesOf(fallenName);
		// Ensure the primary winner is included (might already be if they were at war).
		if (!winners.Contains(victorName))
			winners.Add(victorName);

		// --- 0. Remove all remaining troops ---
		foreach (var troop in TroopManager.Instance.GetTroopsForCountry(fallenName).ToArray())
			TroopManager.Instance.RemoveTroop(troop);

		// --- 1. Clean up wars and permissions ---
		var formerEnemies = new Godot.Collections.Array();
		if (wars.TryGetValue(fallenName, out var loserEnemies))
		{
			formerEnemies = new Godot.Collections.Array();
			foreach (var enemyName in loserEnemies)
				formerEnemies.Add(enemyName);
			wars.Remove(fallenName);
			var loserAllowed = loser.Get("allowedCountries").AsGodotArray();
			if (loserAllowed.Contains(victorName))
				loserAllowed.Remove(victorName);
		}

		FactionManager.Instance.ClearFaction(loser);

		foreach (var pair in wars)
		{
			pair.Value.Remove(fallenName);
			var country = FindCountry(pair.Key);
			if (country == null)
				continue;
			var allowed = country.Get("allowedCountries").AsGodotArray();
			if (allowed.Contains(fallenName))
				allowed.Remove(fallenName);
		}

		foreach (var enemy in formerEnemies)
			EmitSignal(SignalName.PeaceMade, FindCountry(enemy.AsString()), loser);

		if (IsTrue(loser.Get("isPlayer")) || playerWon)
		{
			MusicManager.Instance.PlayMusic(MusicMainTheme);
			MusicManager.Instance.PlaySfx(SfxPopup);

			EventManager.Instance.ShowAlert(new Godot.Collections.Dictionary
			{
				{ "event", "capitulated" },
				{ "c1", loser },
				{ "c2", loser },
			});

			if (IsTrue(loser.Get("isPlayer")))
			{
				MusicManager.Instance.PlaySfx(SfxGameOver);
				MusicManager.Instance.PlayMusic(MusicMainTheme);
			}
			else if (player == null || !IsCountryAtWar(player.Get("countryName").AsString()))
			{
				MusicManager.Instance.PlayMusic(MusicMainTheme);
			}
		}

		// --- 2. Return territory occupied BY the loser to original owners ---
		var allProvinces = MapManager.Instance.countryToProvinces;
		var ownedProvinces = MapManager.Instance.countryToOwnedProvinces;
		var fallenProvinces = allProvinces.TryGetValue(fallenName, out var fp)
			? new List<int>(fp)
			: new List<int>();
		var fallenOwned = ownedProvinces.TryGetValue(fallenName, out var fo)
			? fo
			: new List<int>();
		foreach (var pid in fallenProvinces)
		{
			if (!fallenOwned.Contains(pid))
				MapManager.Instance.DeoccupyProvince(pid);
		}

		// --- 3. Territory preview (for peace UI only) ---
		if (playerWon)
		{
			foreach (var pid in new List<int>(fallenOwned))
				MapManager.Instance.DeoccupyProvince(pid);

			var winnersData = new Godot.Collections.Array();
			foreach (var winnerName in winners)
			{
				var wData = FindCountry(winnerName.AsString());
				if (wData != null)
					winnersData.Add(wData);
			}

			EmitSignal(SignalName.PeaceConferenceRequested, winnersData, loser);
			originalTerritories.Remove(fallenName);
		}
		else // --- 5. AI takes everything ---
		{
			foreach (var pid in new List<int>(fallenOwned))
			{
				var province = ProvinceObject(pid);
				var occupier = province == null ? "" : province.occupier;
				if (occupier == "")
					MapManager.Instance.TransferOwnership(pid, victorName);
				else
					MapManager.Instance.TransferOwnership(pid, occupier);
			}

			originalTerritories.Remove(fallenName);
			CountryManager.Instance.CleanupEmptyCountries();
		}
	}

	// --- Helpers ---

	private static List<TroopData> GetTroops(int pid)
	{
		return TroopManager.Instance.GetTroopsInProvince(pid);
	}

	private static GodotObject FindCountry(string name)
	{
		var countries = CountryManager.Instance.countries;
		return TryCountry(countries, name);
	}

	private static GodotObject TryCountry(Godot.Collections.Dictionary countries, string name)
	{
		return countries.TryGetValue(name, out var value) ? value.AsGodotObject() : null;
	}

	private static Province ProvinceObject(int pid)
	{
		var provinces = MapManager.Instance.provinceObjects;
		return provinces.TryGetValue(pid, out var value) ? value : null;
	}

	private static string FunctionalOwner(Province province)
	{
		if (province == null)
			return "";
		return province.GetFunctionalOwner();
	}

	private static bool HasProvinces(Dictionary<string, List<int>> provinces, string name)
	{
		return provinces.TryGetValue(name, out var value) && value.Count > 0;
	}

	private static bool IsTrue(Variant value)
	{
		return value.VariantType != Variant.Type.Nil && value.AsBool();
	}

	private static bool IsSame(GodotObject a, GodotObject b)
	{
		if (ReferenceEquals(a, b))
			return true;
		if (a == null || b == null)
			return false;
		return a.Equals(b); // GodotObject.Equals compares the underlying Godot instance.
	}
}

// Inner Battle class from the GDScript ported as a top-level class so GDScript's
// MapContainer can keep reading battle fields dynamically. No [GlobalClass].
public partial class Battle : RefCounted
{
	public int attackerPid;
	public int defenderPid;
	public string attackerCountry;
	public string defenderCountry;

	public GodotObject attackerStats;
	public GodotObject defenderStats;

	public float attackProgress = 0.0f;
	public float attMorale;
	public float defMorale;
	public float initialDefMorale;

	// These represent the total combined HP of all divisions in the battle.
	public float currentDefHp = 0.0f;
	public float totalStartingHp = 0.0f;

	public Vector2 position;
	public WarManager manager;

	public Battle() { }

	public Battle(int atkPid, int defPid, string atkC, string defC, Vector2 pos, WarManager m)
	{
		attackerPid = atkPid;
		defenderPid = defPid;
		attackerCountry = atkC;
		defenderCountry = defC;
		position = pos;
		manager = m;

		var countries = CountryManager.Instance.countries;
		attackerStats = countries.TryGetValue(attackerCountry, out var atkValue) ? atkValue.AsGodotObject() : null;
		defenderStats = countries.TryGetValue(defenderCountry, out var defValue) ? defValue.AsGodotObject() : null;

		// Sync morale.
		attMorale = attackerStats != null ? attackerStats.Call("GetMaxMorale").AsSingle() : 80.0f;
		initialDefMorale = (defenderStats != null ? defenderStats.Call("GetMaxMorale").AsSingle() : 80.0f)
			+ WarManager.MORALE_BOOST_DEFENDER;
		defMorale = initialDefMorale;

		// Set initial HP snapshot.
		_update_hp_totals();
		totalStartingHp = currentDefHp;

	}

	public void _update_hp_totals()
	{
		// This sums up the current HP of every division the defender has in the province.
		var total = 0.0f;
		foreach (var troopVariant in GetTroops(defenderPid))
		{
			if (troopVariant is not TroopData t || t.countryName != defenderCountry)
				continue;
			foreach (var div in t.storedDivisions)
				total += div.hp;
		}
		currentDefHp = total;
	}

	public void cleanup() { }

	public void _resolve_round()
	{
		var attTroops = GetTroopsForCountry(attackerPid, attackerCountry);
		var defTroops = GetTroopsForCountry(defenderPid, defenderCountry);
		if (attTroops.Count == 0)
		{
			manager.EndBattle(this);
			return;
		}

		// --- NEW: SUPPLY & MONEY CHECK ---
		// 1% of recruitment cost as a "per-round" supply cost.
		var attSupplyCost = 0.0f;
		foreach (var t in attTroops)
		{
			foreach (var div in t.storedDivisions)
			{
				var template = DivisionData.TEMPLATES.ContainsKey(div.type)
					? DivisionData.TEMPLATES[div.type]
					: DivisionData.TEMPLATES["infantry"];
				attSupplyCost += template.Get("cost").Single() * 0.5f;
			}
		}
		if (attackerStats != null)
			attackerStats.Set("money", attackerStats.Get("money").AsDouble() - attSupplyCost);

		var defSupplyCost = 0.0f;
		foreach (var t in defTroops)
		{
			foreach (var div in t.storedDivisions)
			{
				var template = DivisionData.TEMPLATES.ContainsKey(div.type)
					? DivisionData.TEMPLATES[div.type]
					: DivisionData.TEMPLATES["infantry"];
				defSupplyCost += template.Get("cost").Single() * 0.1f;
			}
		}
		if (defenderStats != null)
			defenderStats.Set("money", defenderStats.Get("money").AsDouble() - defSupplyCost);

		// Apply costs and determine penalties.
		var attSupplyMult = 1.0f;
		var defSupplyMult = 1.0f;

		if (attackerStats != null)
		{
			if (attackerStats.Get("money").AsDouble() >= attSupplyCost)
				attackerStats.Set("money", attackerStats.Get("money").AsDouble() - attSupplyCost);
			else
			{
				attSupplyMult = 0.4f;
				attMorale -= 2.0f; // Extra morale penalty for hungry troops.
			}
		}

		if (defenderStats != null)
		{
			if (defenderStats.Get("money").AsDouble() >= defSupplyCost)
				defenderStats.Set("money", defenderStats.Get("money").AsDouble() - defSupplyCost);
			else
			{
				defSupplyMult = 0.4f;
				defMorale -= 2.0f;
			}
		}

		// --- 1. Calculate Power (now including Supply Penalty) ---
		var totalAtkPower = 0.0f;
		foreach (var t in attTroops)
		{
			foreach (var div in t.storedDivisions)
				totalAtkPower += div.GetAttackPower() * (div.hp / div.maxHp);
		}
		totalAtkPower *= attSupplyMult;

		var totalDefPower = 0.0f;
		foreach (var t in defTroops)
		{
			foreach (var div in t.storedDivisions)
				totalDefPower += div.GetDefensePower() * (div.hp / div.maxHp);
		}
		totalDefPower *= defSupplyMult;

		// --- 2. Modifiers (Morale and Efficiency) ---
		var finalAttack = totalAtkPower * (attMorale * 0.01f) *
			(attackerStats != null ? attackerStats.Call("GetAttackEfficiency").AsSingle() : 1.0f);
		var finalDefense = totalDefPower * (defMorale * 0.01f) *
			(defenderStats != null ? defenderStats.Call("GetDefenseEfficiency").AsSingle() : 1.0f);

		// --- 3. Apply Damage ---
		manager.ApplyCasualties(defenderPid, defenderCountry,
			finalAttack * defenderStats.Get("attackerAttackMitigation").AsSingle());
		manager.ApplyCasualties(attackerPid, attackerCountry,
			finalDefense * attackerStats.Get("defenderAttackMitigation").AsSingle() * 0.5f);

		// --- 4. Update Morale Decay ---
		attMorale -= finalDefense * WarManager.MORALE_DECAY_RATE;
		defMorale -= finalAttack * WarManager.MORALE_DECAY_RATE;

		// --- 5. Wrap up Round ---
		_update_hp_totals();

		if (currentDefHp <= 0 || defMorale <= 5.0f)
		{
			_defender_loses();
			return;
		}

		if (attMorale <= 5.0f)
		{
			manager.EndBattle(this);
			return;
		}

		// Progress Calculation.
		var hpRatio = totalStartingHp > 0 ? 1.0f - currentDefHp / totalStartingHp : 1.0f;
		attackProgress = Mathf.Clamp(Mathf.Max(hpRatio, 1.0f - defMorale / initialDefMorale), 0.0f, 1.0f);
	}

	public void _defender_loses()
	{
		var retreatPid = _find_retreat_province(defenderPid, defenderCountry);

		// Duplicate to avoid modification errors during loop.
		foreach (var troopVariant in GetTroops(defenderPid).ToArray())
		{
			if (troopVariant is not TroopData t || t.countryName != defenderCountry)
				continue;

			// If nowhere to run or bad luck, unit is destroyed.
			if (retreatPid == -1 || SimRandom.Shared.NextDouble() < 0.2f)
			{
				TroopManager.Instance.RemoveTroop(t);
			}
			else
			{
				// RETREAT: Lose 20% of strength then move.
				var iterations = Mathf.CeilToInt(t.storedDivisions.Count * 0.2f);
				for (var i = 0; i < iterations; i++)
				{
					if (t.storedDivisions.Count == 0)
						continue;
					var removing = (int)(SimRandom.Shared.NextUInt() % (uint)t.storedDivisions.Count);
					var div = t.storedDivisions[i];
					if (t.countryObj != null)
					{
						((GodotObject)t.countryObj).Set("mobilized", ((GodotObject)t.countryObj).Get("mobilized").AsInt32() -
							(int)(div.hp * div.manpowerPerHP));
					}
					t.storedDivisions.RemoveAt(removing);
				}

				if (t.storedDivisions.Count == 0)
					TroopManager.Instance.RemoveTroop(t);
				else
				{
					TroopManager.Instance.EmitSignal("TroopsChanged", t.countryName);
					TroopManager.Instance.TeleportTroopToProvince(t, retreatPid);
				}
			}
		}

		MapManager.Instance.OccupyProvince(defenderPid, attackerCountry);
		manager.CheckCountryCollapse(defenderCountry, attackerCountry);
		manager.EndBattle(this);
	}

	public int _find_retreat_province(int fromPid, string country)
	{
		var graph = MapManager.Instance.provinceGraph;
		if (graph == null || !graph.Call("has_point", (long)fromPid).AsBool())
			return -1;

		foreach (var neighbor in graph.Call("get_point_connections", (long)fromPid).AsGodotArray())
		{
			var n = (int)neighbor.AsInt64();
			// Retreat logic: must be owned by self and not currently under attack.
			var provinces = MapManager.Instance.provinceObjects;
			if (!provinces.TryGetValue(n, out var provinceValue))
				continue;
			if (FunctionalOwner(provinceValue) == country)
				return n;
		}
		return -1;
	}

	public float _get_divisions(int pid, string country)
	{
		return TroopManager.Instance.GetProvinceStrength(pid, country);
	}

	private static List<TroopData> GetTroops(int pid)
	{
		return TroopManager.Instance.GetTroopsInProvince(pid);
	}

	private static List<TroopData> GetTroopsForCountry(int pid, string country)
	{
		var result = new List<TroopData>();
		foreach (var troopVariant in GetTroops(pid))
		{
			if (troopVariant is TroopData t && t.countryName == country)
				result.Add(t);
		}
		return result;
	}

	private static string FunctionalOwner(Province province)
	{
		if (province == null)
			return "";
		return province.GetFunctionalOwner();
	}
}
