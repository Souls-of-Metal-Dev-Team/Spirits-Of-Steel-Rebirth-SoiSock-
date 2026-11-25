using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sosr;

namespace Sosr;

// Port of Scripts/CountryAI.gd. Keep [GlobalClass] so GDScript's `CountryAI` global
// identifier keeps resolving now that the .gd file is gone.
//
// NOTE(soi): Godot does not expose parameterized C# constructors to GDScript's
// `.new()`, so call sites must be `var ai = CountryAI.new(); ai.init(country)`.
//
// Dynamic view over the still-GDScript CountryData objects held by CountryManager.
public sealed class CountryRef
{
    private readonly GodotObject _object;

    public CountryRef(GodotObject obj) { _object = obj; }

    public GodotObject Object => _object;

    private Variant V(StringName name) => _object == null ? new Variant() : _object.Get(name);

    public string country_name => V("country_name").AsString();
    public double money => V("money").AsDouble();
    public int factories_amount => V("factories_amount").AsInt32();
    public int manpower => V("manpower").AsInt32();
    public int mobilized => V("mobilized").AsInt32();
    public bool is_puppet => V("is_puppet").AsBool();
    public string owner => V("owner").AsString();
    public Vector2 ideology => V("ideology").AsVector2();

    public Godot.Collections.Array<string> factions => ToStringArray(V("factions"));
    public Godot.Collections.Array<string> puppets => ToStringArray(V("puppets"));
    public Godot.Collections.Array ready_troops => V("ready_troops").AsGodotArray();

    public int get_relation_with(string other)
        => _object.Call("get_relation_with", other).AsInt32();

    public bool train_troops(int count, string type = "infantry")
        => _object.Call("train_troops", count, type).AsBool();

    private static Godot.Collections.Array<string> ToStringArray(Variant value)
    {
        var result = new Godot.Collections.Array<string>();
        if (value.VariantType == Variant.Type.Nil)
            return result;
        if (value.VariantType == Variant.Type.PackedStringArray)
        {
            foreach (var s in value.AsStringArray())
                result.Add(s);
            return result;
        }
        foreach (var item in value.AsGodotArray())
            result.Add(item.AsString());
        return result;
    }
}

[GlobalClass]
public partial class CountryAI : RefCounted
{
    // --- TUNING ---
    private const int TICK_RATE_PEACE = 12; // Slower thinking in peace time
    private const int TICK_RATE_WAR = 2; // Think fast during war
    private const double SATURATION_IDEAL = 1.0; // Target: at least 1 division equivalent per province
    private const double SATURATION_MAX = 4.0; // Avoid overstacking; redistribute if exceeding
    private const double DISTANCE_PENALTY = 0.1; // Reduce score per unit distance to discourage far moves
    // NOTE(soi): why tf was this 1?!?!??
    private const int MIN_DIVISIONS_PER_SPLIT = 10; // Smallest split size
    private const int MIN_TOTAL_POWER_FOR_WAR = 5000; // Minimum power/divisions to even think about war
    private const int MAX_SPLITS_PER_TROOP = 5; // Limit splits to prevent micro-management overhead

    // --- AI DIPLOMACY/WAR LOGIC ---
    private const int DECLARE_WAR_COOLDOWN_FRAMES = 600;
    private const int MAX_PARALLEL_WARS = 2;
    private const double WAR_SCORE_THRESHOLD = 0.6;
    private const int MAX_WAR_DECLARATIONS_PER_TICK = 1;
    private const double AI_CHAOS = 0.9;
    private const int MAX_COMBINED_WAR_MEMBERS = 8; // Prevent world-war scale escalations
    private const int EMERGENCY_DEPLOYMENT_THRESHOLD = 5; // If at war and fewer than this, panic deploy

    // GameState.IndustryType.FACTORY (GDScript enum is not visible from C#).
    private const int IndustryFactory = 1;

    private Godot.Collections.Dictionary<string, Variant> _personality = new();
    private int _lastDeclareFrame = -999999;
    private List<string> _neighborCache = new();
    private bool _neighborsDirty = true;
    private CountryRef _country;

    public CountryAI() { }

    // GDScript: CountryAI.new(self) -> CountryAI.new(); ai.init(self)
    public void init(GodotObject country)
    {
        _country = country == null ? null : new CountryRef(country);
        if (_country == null)
            return;

        var extremism = GetExtremism();

        var economy = P(
            ("trade", GD.Randf()),
            ("industry_amount_factor", 0.2 * GD.Randf()),
            ("industry", GD.Randf()),
            ("surplus", GD.Randf()));

        var military = P(
            ("training_factor", GD.Randf()),
            ("max_economic_ratio", 0.7));

        var probability = P(
            ("base", GD.Randf()),
            ("tension_factor", 10.0 * GD.Randf()));

        var score = P(
            ("strength", 2.0 * GD.Randf()),
            ("money_factor", 0.0001 * GD.Randf()),
            ("max_cities", 3.0 * GD.Randf()),
            ("cities", GD.Randf()));

        var combat = P(
            ("attack_weight", 5.0 * GD.Randf()),
            ("defense_weight", 5.0 * GD.Randf()),
            ("city_bonus", 100.0 * GD.Randf()));

        var war = P(
            ("probability", probability),
            ("score", score),
            ("combat", combat),
            ("min_strength_ratio", 1.1 + GD.Randf()),
            ("min_economy", 10000.0 * GD.Randf()));

        var aggression = extremism * 2.0 + AI_CHAOS * GD.Randf();
        _personality = P(
            ("economy", economy),
            ("military", military),
            ("war", war),
            ("aggression", aggression));

        // Adjust war probability based on extremism.
        probability["base"] = D(probability, "base") * (1.0 + extremism);
        probability["tension_factor"] = (2.0 + extremism * 5.0) * GD.Randf();

        var mapManager = MapManager.Instance;
        if (mapManager is Node mapNode && mapNode.IsInsideTree())
        {
            mapManager.Connect("ProvinceOwnershipChanged",
                Callable.From<Variant, Variant, Variant>((_, _, _) => { _neighborsDirty = true; }));
        }
    }

    public void ThinkHour()
    {
        if (_country == null)
            return;
        var tick = Enemies(_country.country_name).Count > 0 ? TICK_RATE_WAR : TICK_RATE_PEACE;
        if (Engine.GetFramesDrawn() % tick != 0)
            return;

        ExecuteBest(new List<AiAction>
        {
            new(ScoreFrontline(), ExecuteFrontline),
        });
    }

    public void ThinkDay()
    {
        ExecuteBest(new List<AiAction>
        {
            new(ScoreFactory(), ExecuteFactory),
            new(ScoreTrain(), ExecuteTrain),
            new(ScoreWar(), ExecuteWar),
            new(ScoreCallToArms(), ExecuteCallToArms),
        });
    }

    // Sorts by score descending, then runs actions until one refuses (returns true)
    // or a score is negative. Exactly the short-circuit behaviour of _execute_best().
    private static void ExecuteBest(List<AiAction> actions)
    {
        if (actions.Count == 0)
            return;

        actions.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach (var action in actions)
        {
            if (action.Score < 0 || action.Action())
                break;
        }
    }

    private double ScoreFactory()
    {
        if (_country.money < 1000)
            return 0;

        return Math.Max(0.1, 1.0 - _country.factories_amount * D(Section("economy"), "industry_amount_factor"))
            * D(Section("economy"), "industry");
    }

    private double ScoreTrain()
    {
        if (_country.money < 500 || _country.manpower < 10000)
            return 0;

        return D(Section("military"), "training_factor");
    }

    private double ScoreWar()
    {
        return _country.is_puppet ? 0 : WorldTension * D(_personality, "aggression");
    }

    private double ScoreFrontline()
    {
        if (Enemies(_country.country_name).Count > 0)
        {
            var deployed = 0;
            foreach (var troopVar in Troops(_country.country_name))
            {
                var troop = troopVar.AsGodotObject() as TroopData;
                if (troop != null)
                    deployed += troop.divisions_count;
            }
            if (deployed < EMERGENCY_DEPLOYMENT_THRESHOLD)
                return 10.0;
        }
        return 1.0; // Always high priority to manage the frontline.
    }

    private bool ExecuteFactory()
    {
        var provinces = MapManager.Instance.country_to_owned_provinces;
        if (!provinces.ContainsKey(_country.country_name))
            return false;

        var queue = EconomyManager.Instance.construction_queue;
        var candidates = new List<int>();
        foreach (var pidVar in provinces[_country.country_name].AsGodotArray())
        {
            var pid = pidVar.AsInt32();
            if (queue.ContainsKey(pidVar))
                continue;
            if (ProvinceObjects[pid].AsGodotObject() is not Province province || province.buildings.Count >= 4)
                continue;
            candidates.Add(pid);
        }
        if (candidates.Count == 0)
            return false;

        var chosen = candidates[GD.RandRange(0, candidates.Count - 1)];
        MapManager.Instance._ProvinceBuildIndustry(chosen, _country.country_name, IndustryFactory);
        return true;
    }

    // Improved: recruit based on current needs (more if at war).
    private bool ExecuteTrain()
    {
        var template = DivisionData.TEMPLATES["infantry"].AsGodotDictionary();
        var maxAffordable = Math.Min(
            (int)(_country.money / template["cost"].AsDouble()),
            (int)(_country.manpower / template["manpower"].AsDouble()));
        if (maxAffordable < 1)
            return false;

        var limit = Enemies(_country.country_name).Count > 0 ? 20 : 10;
        _country.train_troops(Math.Clamp(maxAffordable, 1, limit), "infantry");
        return true;
    }

    private bool ExecuteWar()
    {
        var extremism = GetExtremism();
        var war = Section("war");
        var probability = war["probability"].AsGodotDictionary();
        var aggression = D(_personality, "aggression");
        var tension = WorldTension;

        // 1. THE STOCHASTIC GATE (randomness + world tension).
        if (tension < 0.15 - extremism * 0.1 && aggression < 2.5)
            return false;
        if (GD.Randf() > D(probability, "base") + tension * D(probability, "tension_factor"))
            return false;

        // 2. COOLDOWNS & OVEREXTENSION.
        var frameNow = Engine.GetFramesDrawn();
        if (frameNow - _lastDeclareFrame < DECLARE_WAR_COOLDOWN_FRAMES
            || Enemies(_country.country_name).Count >= MAX_PARALLEL_WARS
            || _country.money < D(war, "min_economy"))
            return false;

        // Pre-calculate our faction set for O(1) intersection checks in the filter.
        var ourFactions = new HashSet<string>(_country.factions);
        var candidates = new List<string>();
        foreach (var enemy in GetNeighborCountries())
        {
            var enemyData = Country(enemy);
            if (enemyData == null)
                continue;

            // Cheapest check first: relation/aggression.
            if (!(_country.get_relation_with(enemy) < 20 || aggression > 2.0))
                continue;

            // Faction check: skip if we share any faction.
            if (enemyData.factions.Any(ourFactions.Contains))
                continue;

            candidates.Add(enemy);
        }

        // Safety: don't declare war with very few troops.
        if (EstimateCountryStrength(_country.country_name, true) < MIN_TOTAL_POWER_FOR_WAR)
            return false;
        if (candidates.Count == 0)
            return false;

        var bestScore = double.NegativeInfinity;
        var bestTarget = "";

        var puppeteers = new HashSet<string>(_country.puppets);
        if (_country.is_puppet)
        {
            puppeteers.Add(_country.owner);
            var ownerData = Country(_country.owner);
            if (ownerData != null)
                foreach (var puppet in ownerData.puppets)
                    puppeteers.Add(puppet);
        }

        var factionManager = FactionManagerSingleton;
        var scoreSettings = war["score"].AsGodotDictionary();
        foreach (var targetName in candidates)
        {
            var targetData = Country(targetName);
            if (targetData == null)
                continue;
            if (factionManager != null && factionManager.in_faction(_country.Object, targetData.Object))
                continue;
            if (WarManager.Instance.is_at_war_names(_country.country_name, targetName))
                continue;
            if (puppeteers.Contains(targetName))
                continue;

            // 3. SCALE ANALYSIS (prevent massive world wars).
            var ownMembers = GetSideMembers(_country.country_name);
            var targetMembers = GetSideMembers(targetName);
            if (ownMembers.Count + targetMembers.Count > MAX_COMBINED_WAR_MEMBERS && aggression < 2.5)
                continue;

            // 4. STRENGTH & DISTANCE ANALYSIS (collective security).
            var ownSideStrength = EstimateSideStrength(_country.country_name);
            var targetSideStrength = EstimateSideStrength(targetName);
            var ratio = ownSideStrength / Math.Max(1.0, targetSideStrength);
            if (ratio < D(war, "min_strength_ratio"))
                continue;

            // 5. DYNAMIC SCORING, capped city count for balance, plus a bit of randomness.
            var cityCount = MapManager.Instance.GetCitiesProvinceCountry(targetName).Count;
            var targetScore = (ratio - 1.0) * D(scoreSettings, "strength")
                + targetData.money * D(scoreSettings, "money_factor")
                + Math.Min(cityCount, (int)D(scoreSettings, "max_cities")) * D(scoreSettings, "cities")
                + GD.RandRange(-0.5, 0.5);

            // 6. FINAL THRESHOLD.
            if (targetScore > bestScore && targetScore > WAR_SCORE_THRESHOLD)
            {
                bestScore = targetScore;
                bestTarget = targetName;
            }
        }

        // 7. EXECUTION.
        if (bestTarget != "")
        {
            var targetRef = Country(bestTarget);
            if (targetRef != null)
                WarManager.Instance.declare_war(_country.Object, targetRef.Object);
            // Aggressive/extreme countries cause more tension.
            MapManager.Instance.IncreaseWorldTension((float)(0.02 + extremism * 0.03 + aggression * 0.01));
            _lastDeclareFrame = frameNow;
        }
        return true;
    }

    // Returns false because the GDScript version is void (null is falsy in
    // _execute_best), so it never short-circuits the other daily actions.
    private bool ExecuteFrontline()
    {
        var name = _country.country_name;

        // Deploy ready troops to random cities so they spread out.
        if (_country.ready_troops.Count > 0)
        {
            var ownedCities = MapManager.Instance.country_to_cities;
            var cities = ownedCities.ContainsKey(name) ? ownedCities[name].AsGodotArray() : new Godot.Collections.Array();
            if (cities.Count > 0)
            {
                // Duplicate to safely erase during iteration.
                foreach (var troopData in _country.ready_troops.Duplicate())
                {
                    var city = cities[GD.RandRange(0, cities.Count - 1)];
                    TroopManager.Instance.DeployReady(name, troopData.As<ReadyTroop>(), city.AsInt32());
                    _country.ready_troops.Remove(troopData);
                }
            }
        }

        var idleTroops = new List<TroopData>();
        foreach (var troopVar in Troops(name))
        {
            if (troopVar.AsGodotObject() is TroopData troop && !troop.is_moving)
                idleTroops.Add(troop);
        }
        if (idleTroops.Count == 0)
            return false;

        var enemies = Enemies(name);
        var movePayload = new Godot.Collections.Array();
        var graph = Graph;

        if (enemies.Count == 0)
        {
            var hubsVar = MapManager.Instance.country_to_cities;
            var hubs = hubsVar.ContainsKey(name) ? hubsVar[name].AsGodotArray() : new Godot.Collections.Array();
            foreach (var troop in idleTroops)
            {
                if (hubs.Contains(troop.province_id))
                    continue;

                // Choose the closest hub to avoid unnecessary long moves.
                var troopPos = graph.GetPointPosition(troop.province_id);
                var sortedHubs = hubs
                    .OrderBy(h => troopPos.DistanceSquaredTo(graph.GetPointPosition(h.AsInt64())))
                    .ToList();
                if (sortedHubs.Count > 0)
                {
                    movePayload.Add(new Godot.Collections.Dictionary
                    {
                        ["troop"] = troop,
                        ["province_id"] = sortedHubs[0].AsInt64(),
                    });
                }
            }
            if (movePayload.Count > 0)
                TroopManager.Instance.command_move_assigned(movePayload);
            return false;
        }

        // Weighted targets: cities, troops, and empty gaps.
        var targets = new List<Target>();
        var seen = new HashSet<long>();
        var enemySet = new HashSet<string>();
        foreach (var enemy in enemies)
            enemySet.Add(enemy.AsString());

        var citySet = new HashSet<long>();
        foreach (var cityVar in MapManager.Instance.all_cities)
            citySet.Add(cityVar["id"].AsInt64());

        var combat = Section("war")["combat"].AsGodotDictionary();
        var provinces = ProvinceObjects;
        foreach (var myPid in MapManager.Instance.GetProvincesBorderingEnemies(name, new Godot.Collections.Array<string>(enemies)))
        {
            foreach (var nId in graph.GetPointConnections(myPid))
            {
                if (!provinces.ContainsKey(nId))
                    continue;
                if (provinces[nId].AsGodotObject() is not Province province)
                    continue;

                var enemyName = province.GetFunctionalOwner();
                if (!enemySet.Contains(enemyName) || seen.Contains(nId))
                    continue;
                seen.Add(nId);

                var enemyStrength = TroopManager.Instance.get_province_strength((int)nId, enemyName);
                var score = 10.0;
                if (citySet.Contains(nId))
                    score += D(combat, "city_bonus");
                score += enemyStrength > 0
                    ? enemyStrength * D(combat, "attack_weight")
                    : 15.0; // High priority to flip "free" land fast.

                targets.Add(new Target((int)nId, 0.0, enemyStrength, score));

                // --- BLITZKRIEG LOGIC --- an enemy city just behind the front line.
                foreach (var dnId in graph.GetPointConnections(nId))
                {
                    if (!provinces.ContainsKey(dnId))
                        continue;
                    if (provinces[dnId].AsGodotObject() is not Province deepProvince)
                        continue;
                    if (deepProvince.GetFunctionalOwner() == enemyName
                        && !seen.Contains(dnId)
                        && citySet.Contains(dnId))
                    {
                        var deepStrength = TroopManager.Instance.get_province_strength((int)dnId, enemyName);
                        targets.Add(new Target((int)dnId, 0.0, deepStrength, D(combat, "city_bonus") * 0.8));
                    }
                }
            }
        }
        if (targets.Count == 0)
            return false;

        var centers = MapManager.Instance.province_centers;
        foreach (var troop in idleTroops)
        {
            var troopPos = centers[troop.province_id].AsVector2();

            // Sort targets by score / (distance * 0.01 + 1), descending.
            double WeightedScore(Target t) =>
                t.Score / (troopPos.DistanceTo(centers[t.Id].AsVector2()) * 0.01 + 1.0);
            targets.Sort((a, b) => WeightedScore(b).CompareTo(WeightedScore(a)));

            var divisionsLeft = troop.divisions_count;
            foreach (var target in targets)
            {
                if (divisionsLeft <= 0)
                    break;
                if (target.VirtualStrength >= SATURATION_MAX)
                    continue;

                // Deterministic splitting: send 1-2 to capture empty land, enough to beat defenders.
                var amount = (int)Math.Clamp(
                    target.EnemyStrength > 0 ? target.EnemyStrength * 1.2 : SATURATION_IDEAL,
                    1, divisionsLeft);

                // Ensure the split-off part is substantial.
                if (amount < divisionsLeft)
                    amount = (int)Math.Clamp(Math.Max(amount, MIN_DIVISIONS_PER_SPLIT), 1, divisionsLeft);

                // Only split if it's worth the micro-overhead.
                if (amount < divisionsLeft && divisionsLeft - amount < MIN_DIVISIONS_PER_SPLIT)
                    amount = divisionsLeft;

                movePayload.Add(new Godot.Collections.Dictionary
                {
                    ["troop"] = troop,
                    ["province_id"] = target.Id,
                    ["divisions"] = amount,
                });

                target.VirtualStrength += amount;
                divisionsLeft -= amount;
            }
        }

        if (movePayload.Count > 0)
            TroopManager.Instance.command_move_assigned(movePayload);
        return false;
    }

    private double ScoreCallToArms()
    {
        if (Enemies(_country.country_name).Count == 0)
            return 0.0;
        if (_country.factions.Count == 0)
            return 0.0;
        return 1.5;
    }

    private bool ExecuteCallToArms()
    {
        var calledAny = false;
        var enemies = Enemies(_country.country_name);
        var factions = FactionManagerSingleton?.factions;
        if (factions == null)
            return false;

        foreach (var factionName in _country.factions)
        {
            if (!factions.ContainsKey(factionName))
                continue;
            var factionData = factions[factionName];

            // CIVIL WAR CHECK: don't call allies if the enemy is in the same faction.
            var isInternalConflict = false;
            foreach (var member in factionData.members)
            {
                if (enemies.Contains(member.polity))
                {
                    isInternalConflict = true;
                    break;
                }
            }
            if (isInternalConflict)
                continue;

            foreach (var member in factionData.members)
            {
                if (member.polity == _country.country_name)
                    continue;

                var memberCountry = Country(member.polity);
                if (memberCountry == null)
                    continue;

                WarManager.Instance.call_to_arms(_country.Object, memberCountry.Object);
                calledAny = true;
            }
        }
        return calledAny;
    }

    // --- UTILITIES ---

    private Godot.Collections.Array GetPeaceHubs()
    {
        var cities = MapManager.Instance.GetCitiesProvinceCountry(_country.country_name);
        if (cities.Count > 0)
            return cities;

        var provinces = MapManager.Instance.country_to_provinces;
        return provinces.ContainsKey(_country.country_name)
            ? provinces[_country.country_name].AsGodotArray()
            : new Godot.Collections.Array();
    }

    private List<string> GetNeighborCountries()
    {
        if (!_neighborsDirty)
            return _neighborCache;

        var neighbors = new HashSet<string>();
        var provinces = MapManager.Instance.country_to_provinces;
        if (provinces.ContainsKey(_country.country_name))
        {
            foreach (var pid in provinces[_country.country_name].AsGodotArray())
            {
                foreach (var nid in Graph.GetPointConnections(pid.AsInt64()))
                {
                    if (!ProvinceObjects.ContainsKey(nid))
                        continue;
                    if (ProvinceObjects[nid].AsGodotObject() is not Province province)
                        continue;

                    var owner = !string.IsNullOrEmpty(province.occupier) ? province.occupier : province.country;
                    if (owner != "" && owner != "Sea" && owner != _country.country_name)
                        neighbors.Add(owner);
                }
            }
        }

        _neighborCache = neighbors.ToList();
        _neighborsDirty = false;
        return _neighborCache;
    }

    private double EstimateCountryStrength(string countryName, bool onlyDeployed = false)
    {
        var c = Country(countryName);
        if (c == null)
            return 0.1;

        double total = 0.0;
        if (!onlyDeployed)
        {
            total += c.manpower * 0.05; // Manpower pool is potential, not active.
            total += c.money * 0.01; // Money is even less direct.
        }
        total += c.mobilized;
        return Math.Max(0.1, total);
    }

    private double EstimateSideStrength(string countryName)
    {
        var total = 0.0;
        foreach (var name in GetSideMembers(countryName))
            total += EstimateCountryStrength(name);
        return total;
    }

    private List<string> GetSideMembers(string countryName)
    {
        var sideMembers = new List<string> { countryName };
        var cData = Country(countryName);
        if (cData == null)
            return sideMembers;

        // 1. Add faction members.
        var factions = FactionManagerSingleton?.factions;
        if (factions != null)
        {
            foreach (var factionName in cData.factions)
            {
                if (!factions.ContainsKey(factionName))
                    continue;
                foreach (var member in factions[factionName].members)
                {
                    if (!sideMembers.Contains(member.polity))
                        sideMembers.Add(member.polity);
                }
            }
        }

        // 2. Add puppets for all side members.
        var allPotential = new List<string>(sideMembers);
        foreach (var member in sideMembers)
        {
            var mData = Country(member);
            if (mData == null)
                continue;
            foreach (var puppet in mData.puppets)
            {
                if (!allPotential.Contains(puppet))
                    allPotential.Add(puppet);
            }
        }
        return allPotential;
    }

    private double GetExtremism()
    {
        // Ideology map is roughly -100 to 100 on both axes; 141.42 is the corner distance.
        if (_country == null)
            return 0.0;
        return Math.Clamp(_country.ideology.Length() * 0.00707113562, 0.0, 1.0);
    }

    // --- BRIDGE / CONTAINER HELPERS ---

    private static Godot.Collections.Dictionary<string, Variant> P(params (string Key, Variant Value)[] entries)
    {
        var dict = new Godot.Collections.Dictionary<string, Variant>();
        foreach (var (key, value) in entries)
            dict[key] = value;
        return dict;
    }

    private static double D(Godot.Collections.Dictionary<string, Variant> dict, string key) => dict[key].AsDouble();
    private static double D(Godot.Collections.Dictionary dict, string key) => dict[key].AsDouble();

    private Godot.Collections.Dictionary Section(string name) => _personality[name].AsGodotDictionary();

    private static double WorldTension => MapManager.Instance.world_tension;
    private static SoiStar Graph => MapManager.Instance.province_graph as SoiStar;
    private static Godot.Collections.Dictionary ProvinceObjects => MapManager.Instance.province_objects;
    private static FactionManager FactionManagerSingleton => FactionManager.Instance as FactionManager;
    private static CountryRef Country(string name)
    {
        var obj = CountryManager.Instance.GetCountry(name);
        return obj == null ? null : new CountryRef(obj);
    }
    private static Godot.Collections.Array Enemies(string name) =>
        WarManager.Instance.get_enemies_of(name);
    private static Godot.Collections.Array Troops(string name) =>
        TroopManager.Instance.get_troops_for_country(name);

    private sealed class AiAction
    {
        public double Score { get; }
        public Func<bool> Action { get; }

        public AiAction(double score, Func<bool> action)
        {
            Score = score;
            Action = action;
        }
    }

    private sealed class Target
    {
        public int Id { get; }
        public double VirtualStrength { get; set; }
        public int EnemyStrength { get; }
        public double Score { get; }

        public Target(int id, double virtualStrength, int enemyStrength, double score)
        {
            Id = id;
            VirtualStrength = virtualStrength;
            EnemyStrength = enemyStrength;
            Score = score;
        }
    }
}
