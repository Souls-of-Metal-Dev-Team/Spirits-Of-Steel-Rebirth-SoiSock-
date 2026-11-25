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
	public Godot.Collections.Dictionary wars = new();
	public Godot.Collections.Array active_battles = new();
	public Godot.Collections.Dictionary original_territories = new();

	public void reset_state()
	{
		wars.Clear();
		foreach (var battleVariant in active_battles)
		{
			if (battleVariant.AsGodotObject() is Battle battle)
				battle.cleanup();
		}
		active_battles.Clear();
		original_territories.Clear();
	}

	public Godot.Collections.Array save_wars()
	{
		var saved_wars = new Godot.Collections.Array();
		var processed_pairs = new HashSet<string>();

		foreach (var countryAValue in wars.Keys)
		{
			var countryA = countryAValue.AsGodotObject();
			if (countryA == null)
				continue;
			var nameA = countryA.Get("country_name").AsString();
			foreach (var countryBValue in wars[countryAValue].AsGodotDictionary().Keys)
			{
				var countryB = countryBValue.AsGodotObject();
				if (countryB == null)
					continue;
				var nameB = countryB.Get("country_name").AsString();

				// Create a sorted pair to avoid saving [A, B] AND [B, A].
				var pair = new Godot.Collections.Array { nameA, nameB };
				pair.Sort();
				var pair_key = $"{pair[0].AsString()}|{pair[1].AsString()}";
				if (!processed_pairs.Add(pair_key))
					continue;

				saved_wars.Add(pair);
			}
		}

		return saved_wars;
	}

	public void load_wars(Godot.Collections.Array data)
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
				add_war_silent(countryA, countryB);
		}
	}

	public Godot.Collections.Dictionary save_original_territories()
	{
		return original_territories;
	}

	public void load_original_territories(Godot.Collections.Dictionary data)
	{
		original_territories = data;
	}

	public void check_for_new_battles()
	{
		if (!GodotObject.IsInstanceValid(MapManager.Instance) || !GodotObject.IsInstanceValid(TroopManager.Instance))
			return;

		var troops_by_province = TroopManager.Instance.troops_by_province;
		foreach (var pidValue in troops_by_province.Keys)
		{
			var province_troops = troops_by_province[pidValue].AsGodotArray();
			if (province_troops.Count < 2)
				continue;

			// We need at least two different countries at war in this province.
			for (var i = 0; i < province_troops.Count; i++)
			{
				var troopA = province_troops[i].AsGodotObject();
				if (troopA == null)
					continue;
				for (var j = i + 1; j < province_troops.Count; j++)
				{
					var troopB = province_troops[j].AsGodotObject();
					if (troopB == null)
						continue;

					if (troopA.Get("country_name").AsString() != troopB.Get("country_name").AsString())
					{
						if (is_at_war_names(troopA.Get("country_name").AsString(), troopB.Get("country_name").AsString()))
						{
							// start_battle checks for existing duplicates internally.
							var pid = pidValue.AsInt32();
							start_battle(pid, pid); // In same province, use pid for both.
						}
					}
				}
			}
		}
	}

	public override void _Process(double delta)
	{
		if (active_battles.Count == 0)
			return;

		var clock = GameState.Instance.current_world?.clock;
		var paused = clock == null || clock.Get("paused").AsBool();
		var time_scale = clock != null ? Mathf.Max(clock.Get("time_scale").AsSingle(), 0.001f) : 1.0f;
		foreach (var battleVariant in active_battles)
		{
			if (battleVariant.AsGodotObject() is not Battle battle || !GodotObject.IsInstanceValid(battle.timer_node))
				continue;
			battle.timer_node.Paused = paused;
			var target_wait = BATTLE_TICK / time_scale;
			if (!Mathf.IsEqualApprox(battle.timer_node.WaitTime, target_wait))
				battle.timer_node.WaitTime = target_wait;
		}
	}

	public void start_battle(int attacker_pid, int defender_pid)
	{
		var clock = GameState.Instance.current_world?.clock;
		if (clock != null && clock.Get("paused").AsBool())
			return;

		// Prevent duplicate battles.
		foreach (var battleVariant in active_battles)
		{
			if (battleVariant.AsGodotObject() is Battle b && b.attacker_pid == attacker_pid && b.defender_pid == defender_pid)
				return;
		}

		var att_troops = GetTroops(attacker_pid);
		var def_troops = GetTroops(defender_pid);
		if (att_troops.Count == 0 || def_troops.Count == 0)
			return;

		var attFirst = att_troops[0].AsGodotObject();
		var defFirst = def_troops[0].AsGodotObject();
		if (attFirst == null || defFirst == null)
			return;

		active_battles.Add(new Battle(
			attacker_pid,
			defender_pid,
			attFirst.Get("country_name").AsString(),
			defFirst.Get("country_name").AsString(),
			get_province_midpoint(attacker_pid, defender_pid),
			this
		));
	}

	public void end_battle(Battle battle)
	{
		if (active_battles.Contains(battle))
		{
			battle.cleanup();
			active_battles.Remove(battle);
		}
	}

	public void apply_casualties(int pid, string country, float damage_amount)
	{
		var troops_list = new Godot.Collections.Array();
		foreach (var troopVariant in GetTroops(pid))
		{
			var troop = troopVariant.AsGodotObject();
			if (troop != null && troop.Get("country_name").AsString() == country)
				troops_list.Add(troop);
		}

		if (troops_list.Count == 0 || damage_amount <= 0)
			return;

		var countryTime = CountryManager.Instance.GetCountry(country);

		// Distribute total damage among all army stacks in the province.
		var damage_per_troop = damage_amount / troops_list.Count;
		foreach (var troopVariant in troops_list)
		{
			if (troopVariant.AsGodotObject() is not TroopData t)
				continue;

			// Distribute troop damage among all divisions in that stack.
			var damage_per_div = damage_per_troop / t.stored_divisions.Count;
			var lost_division = false;

			for (var i = t.stored_divisions.Count - 1; i >= 0; i--)
			{
				var div = t.stored_divisions[i];

				// Deduct HP.
				countryTime.Set("mobilized", countryTime.Get("mobilized").AsInt32() -
					(int)(Mathf.Min(div.hp, damage_per_div) * div.manpowerPerHP));
				div.hp -= damage_per_div;

				// Gain Experience based on how much damage they took/dealt.
				div.experience += 0.005f;

				// Remove division if it hits 0 HP.
				if (div.hp <= 0)
				{
					t.stored_divisions.RemoveAt(i);
					lost_division = true;
				}
			}

			// If the entire stack is gone, remove the troop icon from the map.
			if (t.stored_divisions.Count == 0)
				TroopManager.Instance.RemoveTroop(t);
			else if (lost_division)
				TroopManager.Instance.EmitSignal("TroopsChanged", country);
		}
	}

	public void resolve_province_arrival(int pid, TroopData troop)
	{
		if (troop == null)
			return;

		var provinces = MapManager.Instance.province_objects;
		if (!provinces.TryGetValue(pid, out var provinceValue))
			return;
		var target_country = FunctionalOwner(provinceValue.AsGodotObject());

		if (target_country != troop.country_name && is_at_war_names(troop.country_name, target_country))
		{
			if (TroopManager.Instance.get_province_strength(pid, target_country) <= 0)
			{
				if (!is_at_war_names(troop.country_name, target_country))
					MapManager.Instance.DeoccupyProvince(pid);
				else
					MapManager.Instance.OccupyProvince(pid, troop.country_name);
				check_country_collapse(target_country, troop.country_name);
			}
		}
	}

	public void call_to_arms(GodotObject caller, GodotObject target)
	{
		if (IsSame(caller, target))
			return;

		// DEAD CHECK: Target must have land to fight.
		var provinces = MapManager.Instance.country_to_provinces;
		if (!HasProvinces(provinces, target.Get("country_name").AsString()) && !IsTrue(target.Get("is_exiled")))
			return;

		foreach (var enemyNameVariant in get_enemies_of(caller.Get("country_name").AsString()))
		{
			var enemy_data = FindCountry(enemyNameVariant.AsString());
			if (enemy_data != null)
				declare_war(target, enemy_data);
		}
	}

	// GDScript exposes the optional a_silent parameter; Godot does not pass C# default
	// values to GDScript callers, so keep both arities as separate registered methods.
	public void declare_war(GodotObject warer, GodotObject waree)
	{
		declare_war(warer, waree, false);
	}

	public void declare_war(GodotObject warer, GodotObject waree, bool a_silent)
	{
		if (IsSame(warer, waree) || is_at_war(warer, waree) ||
			FactionManager.Instance.in_faction(warer, waree))
			return;

		var provinces = MapManager.Instance.country_to_provinces;
		if (!HasProvinces(provinces, warer.Get("country_name").AsString()) && !IsTrue(warer.Get("is_exiled")))
			return;
		if (!HasProvinces(provinces, waree.Get("country_name").AsString()) && !IsTrue(waree.Get("is_exiled")))
			return;

		if (!add_war_silent(warer, waree) || a_silent)
			return;

		var player = CountryManager.Instance.player_country;
		if (IsTrue(warer.Get("is_player")) || IsTrue(waree.Get("is_player")) ||
			is_at_war(player, warer) || is_at_war(player, waree))
		{
			EmitSignal(SignalName.WarDeclared, warer, waree);
			// MusicManager.play_music(MUSIC.BATTLE_THEME) -> 1
			// MusicManager.play_sfx(SFX.DECLARE_WAR) -> 4
			// (both commented out in the original GDScript)
		}
	}

	public void _snapshot_country_territory(string c_name)
	{
		if (original_territories.ContainsKey(c_name))
			return;

		var provinces = MapManager.Instance.country_to_provinces;
		var pids = provinces.TryGetValue(c_name, out var value)
			? value.AsGodotArray().Duplicate()
			: new Godot.Collections.Array();
		original_territories[c_name] = pids;

		// Also store in CountryData if it exists.
		var country_data = FindCountry(c_name);
		if (country_data != null && country_data.Get("pre_war_provinces").AsGodotArray().Count == 0)
			country_data.Set("pre_war_provinces", pids.Duplicate());
	}

	public bool add_war_silent(GodotObject a, GodotObject b)
	{
		if (IsSame(a, b) || is_at_war(a, b))
			return false;

		if (!wars.ContainsKey(a))
			wars[a] = new Godot.Collections.Dictionary();
		if (!wars.ContainsKey(b))
			wars[b] = new Godot.Collections.Dictionary();
		wars[a].AsGodotDictionary()[b] = true;
		wars[b].AsGodotDictionary()[a] = true;

		var nameB = b.Get("country_name").AsString();
		var nameA = a.Get("country_name").AsString();
		var aAllowed = a.Get("allowedCountries").AsGodotArray();
		if (!aAllowed.Contains(nameB))
			aAllowed.Add(nameB);
		var bAllowed = b.Get("allowedCountries").AsGodotArray();
		if (!bAllowed.Contains(nameA))
			bAllowed.Add(nameA);

		return true;
	}

	public bool is_at_war(GodotObject a, GodotObject b)
	{
		if (a == null || b == null)
			return false;
		return wars.ContainsKey(a) && wars[a].AsGodotDictionary().ContainsKey(b);
	}

	public bool is_country_at_war(string country_name)
	{
		var country_data = FindCountry(country_name);
		return country_data != null && wars.ContainsKey(country_data) && wars[country_data].AsGodotDictionary().Count > 0;
	}

	public bool is_at_war_names(string a_name, string b_name)
	{
		var countries = CountryManager.Instance.countries;
		var a = TryCountry(countries, a_name);
		if (a == null || !wars.ContainsKey(a))
			return false;
		var b = TryCountry(countries, b_name);
		return b != null && wars[a].AsGodotDictionary().ContainsKey(b);
	}

	// API for AI Manager.
	public Godot.Collections.Array get_countries_at_war()
	{
		return new Godot.Collections.Array(wars.Keys);
	}

	// Returns an array of country names that are currently at war with the given country name.
	public Godot.Collections.Array get_enemies_of(string country_name)
	{
		var enemies = new Godot.Collections.Array();
		var country_data = FindCountry(country_name);
		if (country_data == null || !wars.ContainsKey(country_data))
			return enemies;

		// wars[country_data] is a Dictionary keyed by enemy CountryData objects.
		foreach (var enemyValue in wars[country_data].AsGodotDictionary().Keys)
		{
			var enemy = enemyValue.AsGodotObject();
			if (enemy != null)
				enemies.Add(enemy.Get("country_name").AsString());
		}

		return enemies;
	}

	public Vector2 get_province_midpoint(int pid1, int pid2)
	{
		var centers = MapManager.Instance.province_centers;
		var p1 = centers.TryGetValue(pid1, out var v1) ? v1.AsVector2() : Vector2.Zero;
		var p2 = centers.TryGetValue(pid2, out var v2) ? v2.AsVector2() : Vector2.Zero;
		return (p1 + p2) * 0.5f;
	}

	public void check_country_collapse(string country_name, string victor_name)
	{
		if (MapManager.Instance.GetCitiesProvinceCountry(country_name).Count == 0)
			_handle_total_collapse(country_name, victor_name);
	}

	public void _handle_total_collapse(string fallen_name, string victor_name)
	{
		var loser = FindCountry(fallen_name);
		var winner = FindCountry(victor_name);

		// NOTE Z21: Fixes some bug that makes this function run multiple times.
		if (loser == null || !wars.ContainsKey(loser))
			return;
		var player = CountryManager.Instance.player_country;
		var player_won = player != null &&
			((winner != null && IsTrue(winner.Get("is_player"))) || is_at_war(player, loser));
		var winners = get_enemies_of(fallen_name);
		// Ensure the primary winner is included (might already be if they were at war).
		if (!winners.Contains(victor_name))
			winners.Add(victor_name);

		// --- 0. Remove all remaining troops ---
		foreach (var troopVariant in TroopManager.Instance.get_troops_for_country(fallen_name).Duplicate())
			TroopManager.Instance.RemoveTroop(troopVariant.As<TroopData>());

		// --- 1. Clean up wars and permissions ---
		var former_enemies = new Godot.Collections.Array();
		if (wars.ContainsKey(loser))
		{
			former_enemies = new Godot.Collections.Array(wars[loser].AsGodotDictionary().Keys);
			wars.Remove(loser);
			var loserAllowed = loser.Get("allowedCountries").AsGodotArray();
			if (loserAllowed.Contains(victor_name))
				loserAllowed.Remove(victor_name);
		}

		FactionManager.Instance.clear_faction(loser);

		foreach (var countryValue in wars.Keys)
		{
			if (wars[countryValue].AsGodotDictionary().ContainsKey(loser))
				wars[countryValue].AsGodotDictionary().Remove(loser);

			var country = countryValue.AsGodotObject();
			if (country == null)
				continue;
			var allowed = country.Get("allowedCountries").AsGodotArray();
			if (allowed.Contains(fallen_name))
				allowed.Remove(fallen_name);
		}

		foreach (var enemy in former_enemies)
			EmitSignal(SignalName.PeaceMade, enemy.AsGodotObject(), loser);

		if (IsTrue(loser.Get("is_player")) || player_won)
		{
			MusicManager.Instance.play_music(MusicMainTheme);
			MusicManager.Instance.play_sfx(SfxPopup);

			EventManager.Instance.show_alert(new Godot.Collections.Dictionary
			{
				{ "event", "capitulated" },
				{ "c1", loser },
				{ "c2", loser },
			});

			if (IsTrue(loser.Get("is_player")))
			{
				MusicManager.Instance.play_sfx(SfxGameOver);
				MusicManager.Instance.play_music(MusicMainTheme);
			}
			else if (player == null || !is_country_at_war(player.Get("country_name").AsString()))
			{
				MusicManager.Instance.play_music(MusicMainTheme);
			}
		}

		// --- 2. Return territory occupied BY the loser to original owners ---
		var allProvinces = MapManager.Instance.country_to_provinces;
		var ownedProvinces = MapManager.Instance.country_to_owned_provinces;
		var fallenProvinces = allProvinces.TryGetValue(fallen_name, out var fp)
			? fp.AsGodotArray().Duplicate()
			: new Godot.Collections.Array();
		var fallenOwned = ownedProvinces.TryGetValue(fallen_name, out var fo)
			? fo.AsGodotArray()
			: new Godot.Collections.Array();
		foreach (var pid in fallenProvinces)
		{
			if (!fallenOwned.Contains(pid))
				MapManager.Instance.DeoccupyProvince(pid.AsInt32());
		}

		// --- 3. Territory preview (for peace UI only) ---
		if (player_won)
		{
			foreach (var pid in fallenOwned.Duplicate())
				MapManager.Instance.DeoccupyProvince(pid.AsInt32());

			var winners_data = new Godot.Collections.Array();
			foreach (var winnerName in winners)
			{
				var w_data = FindCountry(winnerName.AsString());
				if (w_data != null)
					winners_data.Add(w_data);
			}

			EmitSignal(SignalName.PeaceConferenceRequested, winners_data, loser);
			original_territories.Remove(fallen_name);
		}
		else // --- 5. AI takes everything ---
		{
			foreach (var pid in fallenOwned.Duplicate())
			{
				var province = ProvinceObject(pid.AsInt32());
				var occupier = province == null ? "" : province.Get("occupier").AsString();
				if (occupier == "")
					MapManager.Instance.TransferOwnership(pid.AsInt32(), victor_name);
				else
					MapManager.Instance.TransferOwnership(pid.AsInt32(), occupier);
			}

			original_territories.Remove(fallen_name);
			CountryManager.Instance.cleanup_empty_countries();
		}
	}

	// --- Helpers ---

	private static Godot.Collections.Array GetTroops(int pid)
	{
		return TroopManager.Instance.get_troops_in_province(pid);
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

	private static GodotObject ProvinceObject(int pid)
	{
		var provinces = MapManager.Instance.province_objects;
		return provinces.TryGetValue(pid, out var value) ? value.AsGodotObject() : null;
	}

	private static string FunctionalOwner(GodotObject province)
	{
		if (province == null)
			return "";
		return province.Call("GetFunctionalOwner").AsString();
	}

	private static bool HasProvinces(Godot.Collections.Dictionary provinces, string name)
	{
		return provinces.TryGetValue(name, out var value) && value.AsGodotArray().Count > 0;
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
	public int attacker_pid;
	public int defender_pid;
	public string attacker_country;
	public string defender_country;

	public GodotObject attacker_stats;
	public GodotObject defender_stats;

	public float attack_progress = 0.0f;
	public float att_morale;
	public float def_morale;
	public float initial_def_morale;

	// These represent the total combined HP of all divisions in the battle.
	public float current_def_hp = 0.0f;
	public float total_starting_hp = 0.0f;

	public Timer timer_node;
	public Vector2 position;
	public WarManager manager;

	public Battle() { }

	public Battle(int atk_pid, int def_pid, string atk_c, string def_c, Vector2 pos, WarManager m)
	{
		attacker_pid = atk_pid;
		defender_pid = def_pid;
		attacker_country = atk_c;
		defender_country = def_c;
		position = pos;
		manager = m;

		var countries = CountryManager.Instance.countries;
		attacker_stats = countries.TryGetValue(attacker_country, out var atkValue) ? atkValue.AsGodotObject() : null;
		defender_stats = countries.TryGetValue(defender_country, out var defValue) ? defValue.AsGodotObject() : null;

		// Sync morale.
		att_morale = attacker_stats != null ? attacker_stats.Call("get_max_morale").AsSingle() : 80.0f;
		initial_def_morale = (defender_stats != null ? defender_stats.Call("get_max_morale").AsSingle() : 80.0f)
			+ WarManager.MORALE_BOOST_DEFENDER;
		def_morale = initial_def_morale;

		// Set initial HP snapshot.
		_update_hp_totals();
		total_starting_hp = current_def_hp;

		timer_node = new Timer
		{
			OneShot = false,
			WaitTime = WarManager.BATTLE_TICK,
		};
		timer_node.Timeout += _resolve_round;
		manager.AddChild(timer_node);
		timer_node.Start();
	}

	public void _update_hp_totals()
	{
		// This sums up the current HP of every division the defender has in the province.
		var total = 0.0f;
		foreach (var troopVariant in GetTroops(defender_pid))
		{
			if (troopVariant.AsGodotObject() is not TroopData t || t.country_name != defender_country)
				continue;
			foreach (var div in t.stored_divisions)
				total += div.hp;
		}
		current_def_hp = total;
	}

	public void cleanup()
	{
		if (GodotObject.IsInstanceValid(timer_node))
		{
			timer_node.Stop();
			timer_node.QueueFree();
			timer_node = null;
		}
	}

	public void _resolve_round()
	{
		var att_troops = GetTroopsForCountry(attacker_pid, attacker_country);
		var def_troops = GetTroopsForCountry(defender_pid, defender_country);
		if (att_troops.Count == 0)
		{
			manager.end_battle(this);
			return;
		}

		// --- NEW: SUPPLY & MONEY CHECK ---
		// 1% of recruitment cost as a "per-round" supply cost.
		var att_supply_cost = 0.0f;
		foreach (var t in att_troops)
		{
			foreach (var div in t.stored_divisions)
			{
				var template = DivisionData.TEMPLATES.ContainsKey(div.type)
					? DivisionData.TEMPLATES[div.type].AsGodotDictionary()
					: DivisionData.TEMPLATES["infantry"].AsGodotDictionary();
				att_supply_cost += template["cost"].AsSingle() * 0.5f;
			}
		}
		if (attacker_stats != null)
			attacker_stats.Set("money", attacker_stats.Get("money").AsDouble() - att_supply_cost);

		var def_supply_cost = 0.0f;
		foreach (var t in def_troops)
		{
			foreach (var div in t.stored_divisions)
			{
				var template = DivisionData.TEMPLATES.ContainsKey(div.type)
					? DivisionData.TEMPLATES[div.type].AsGodotDictionary()
					: DivisionData.TEMPLATES["infantry"].AsGodotDictionary();
				def_supply_cost += template["cost"].AsSingle() * 0.1f;
			}
		}
		if (defender_stats != null)
			defender_stats.Set("money", defender_stats.Get("money").AsDouble() - def_supply_cost);

		// Apply costs and determine penalties.
		var att_supply_mult = 1.0f;
		var def_supply_mult = 1.0f;

		if (attacker_stats != null)
		{
			if (attacker_stats.Get("money").AsDouble() >= att_supply_cost)
				attacker_stats.Set("money", attacker_stats.Get("money").AsDouble() - att_supply_cost);
			else
			{
				att_supply_mult = 0.4f;
				att_morale -= 2.0f; // Extra morale penalty for hungry troops.
			}
		}

		if (defender_stats != null)
		{
			if (defender_stats.Get("money").AsDouble() >= def_supply_cost)
				defender_stats.Set("money", defender_stats.Get("money").AsDouble() - def_supply_cost);
			else
			{
				def_supply_mult = 0.4f;
				def_morale -= 2.0f;
			}
		}

		// --- 1. Calculate Power (now including Supply Penalty) ---
		var total_atk_power = 0.0f;
		foreach (var t in att_troops)
		{
			foreach (var div in t.stored_divisions)
				total_atk_power += div.get_attack_power() * (div.hp / div.max_hp);
		}
		total_atk_power *= att_supply_mult;

		var total_def_power = 0.0f;
		foreach (var t in def_troops)
		{
			foreach (var div in t.stored_divisions)
				total_def_power += div.get_defense_power() * (div.hp / div.max_hp);
		}
		total_def_power *= def_supply_mult;

		// --- 2. Modifiers (Morale and Efficiency) ---
		var final_attack = total_atk_power * (att_morale * 0.01f) *
			(attacker_stats != null ? attacker_stats.Call("get_attack_efficiency").AsSingle() : 1.0f);
		var final_defense = total_def_power * (def_morale * 0.01f) *
			(defender_stats != null ? defender_stats.Call("get_defense_efficiency").AsSingle() : 1.0f);

		// --- 3. Apply Damage ---
		manager.apply_casualties(defender_pid, defender_country,
			final_attack * defender_stats.Get("attacker_attack_mitigation").AsSingle());
		manager.apply_casualties(attacker_pid, attacker_country,
			final_defense * attacker_stats.Get("defender_attack_mitigation").AsSingle() * 0.5f);

		// --- 4. Update Morale Decay ---
		att_morale -= final_defense * WarManager.MORALE_DECAY_RATE;
		def_morale -= final_attack * WarManager.MORALE_DECAY_RATE;

		// --- 5. Wrap up Round ---
		_update_hp_totals();

		if (current_def_hp <= 0 || def_morale <= 5.0f)
		{
			_defender_loses();
			return;
		}

		if (att_morale <= 5.0f)
		{
			manager.end_battle(this);
			return;
		}

		// Progress Calculation.
		var hp_ratio = total_starting_hp > 0 ? 1.0f - current_def_hp / total_starting_hp : 1.0f;
		attack_progress = Mathf.Clamp(Mathf.Max(hp_ratio, 1.0f - def_morale / initial_def_morale), 0.0f, 1.0f);
	}

	public void _defender_loses()
	{
		var retreat_pid = _find_retreat_province(defender_pid, defender_country);

		// Duplicate to avoid modification errors during loop.
		foreach (var troopVariant in GetTroops(defender_pid).Duplicate())
		{
			if (troopVariant.AsGodotObject() is not TroopData t || t.country_name != defender_country)
				continue;

			// If nowhere to run or bad luck, unit is destroyed.
			if (retreat_pid == -1 || GD.Randf() < 0.2f)
			{
				TroopManager.Instance.RemoveTroop(t);
			}
			else
			{
				// RETREAT: Lose 20% of strength then move.
				var iterations = Mathf.CeilToInt(t.stored_divisions.Count * 0.2f);
				for (var i = 0; i < iterations; i++)
				{
					if (t.stored_divisions.Count == 0)
						continue;
					var removing = (int)(GD.Randi() % (uint)t.stored_divisions.Count);
					var div = t.stored_divisions[i];
					if (t.country_obj != null)
					{
						t.country_obj.Set("mobilized", t.country_obj.Get("mobilized").AsInt32() -
							(int)(div.hp * div.manpowerPerHP));
					}
					t.stored_divisions.RemoveAt(removing);
				}

				if (t.stored_divisions.Count == 0)
					TroopManager.Instance.RemoveTroop(t);
				else
				{
					TroopManager.Instance.EmitSignal("TroopsChanged", t.country_name);
					TroopManager.Instance.teleport_troop_to_province(t, retreat_pid);
				}
			}
		}

		MapManager.Instance.OccupyProvince(defender_pid, attacker_country);
		manager.check_country_collapse(defender_country, attacker_country);
		manager.end_battle(this);
	}

	public int _find_retreat_province(int from_pid, string country)
	{
		var graph = MapManager.Instance.province_graph;
		if (graph == null || !graph.Call("has_point", (long)from_pid).AsBool())
			return -1;

		foreach (var neighbor in graph.Call("get_point_connections", (long)from_pid).AsGodotArray())
		{
			var n = neighbor.AsInt32();
			// Retreat logic: must be owned by self and not currently under attack.
			var provinces = MapManager.Instance.province_objects;
			if (!provinces.TryGetValue(n, out var provinceValue))
				continue;
			if (FunctionalOwner(provinceValue.AsGodotObject()) == country)
				return n;
		}
		return -1;
	}

	public float _get_divisions(int pid, string country)
	{
		return TroopManager.Instance.get_province_strength(pid, country);
	}

	private static Godot.Collections.Array GetTroops(int pid)
	{
		return TroopManager.Instance.get_troops_in_province(pid);
	}

	private static List<TroopData> GetTroopsForCountry(int pid, string country)
	{
		var result = new List<TroopData>();
		foreach (var troopVariant in GetTroops(pid))
		{
			if (troopVariant.AsGodotObject() is TroopData t && t.country_name == country)
				result.Add(t);
		}
		return result;
	}

	private static string FunctionalOwner(GodotObject province)
	{
		if (province == null)
			return "";
		return province.Call("GetFunctionalOwner").AsString();
	}
}
