using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class CountryAI : RefCounted, IGameController
{
    public string CountryName => _country?.countryName ?? "";

    public bool Issue(GameAction action) => ActionDispatcher.Instance.Submit(action);

    // --- TUNING ---
    private const int TICK_RATE_PEACE = 12; // Slower thinking in peace time
    private const int TICK_RATE_WAR = 2; // Think fast during war
    private const double SATURATION_IDEAL = 1.0; // Target: at least 1 division equivalent per province
    private const double SATURATION_MAX = 4.0; // Avoid overstacking; redistribute if exceeding
    // NOTE(soi): why tf was this 1?!?!??
    private const int MIN_DIVISIONS_PER_SPLIT = 10; // Smallest split size
    private const int MIN_TOTAL_POWER_FOR_WAR = 5000; // Minimum power/divisions to even think about war

    // --- AI DIPLOMACY/WAR LOGIC ---
    private const int DECLARE_WAR_COOLDOWN_FRAMES = 600;
    private const int MAX_PARALLEL_WARS = 2;
    private const double WAR_SCORE_THRESHOLD = 0.6;
    private const double AI_CHAOS = 0.9;
    private const int MAX_COMBINED_WAR_MEMBERS = 8; // Prevent world-war scale escalations
    private const int EMERGENCY_DEPLOYMENT_THRESHOLD = 5; // If at war and fewer than this, panic deploy

    // GameState.IndustryType.FACTORY (GDScript enum is not visible from C#).
    private const int IndustryFactory = 1;

    private Godot.Collections.Dictionary _personality = new();
    private int _lastDeclareFrame = -999999;
    private List<string> _neighborCache = new();
    private bool _neighborsDirty = true;
    private CountryData _country;

    // Day planning runs on workers. This state is set per call, never shared.
    private Dictionary<string, double> _traits = new();
    private AiSnapshot _snap;
    private AiCountrySnapshot _self;
    private RandomStream _rng;
    private AiPlan _plan;

    public CountryAI() { }

    // GDScript: CountryAI.new(self) -> CountryAI.new(); ai.init(self)
    public void init(CountryData country)
    {
        _country = country;
        if (_country == null)
            return;

        var extremism = GetExtremism();

        var economy = P(
            ("trade", SimRandom.Shared.NextDouble()),
            ("industry_amount_factor", 0.2 * SimRandom.Shared.NextDouble()),
            ("industry", SimRandom.Shared.NextDouble()),
            ("surplus", SimRandom.Shared.NextDouble()));

        var military = P(
            ("training_factor", SimRandom.Shared.NextDouble()),
            ("max_economic_ratio", 0.7));

        var probability = P(
            ("base", SimRandom.Shared.NextDouble()),
            ("tension_factor", 10.0 * SimRandom.Shared.NextDouble()));

        var score = P(
            ("strength", 2.0 * SimRandom.Shared.NextDouble()),
            ("money_factor", 0.0001 * SimRandom.Shared.NextDouble()),
            ("max_cities", 3.0 * SimRandom.Shared.NextDouble()),
            ("cities", SimRandom.Shared.NextDouble()));

        var combat = P(
            ("attack_weight", 5.0 * SimRandom.Shared.NextDouble()),
            ("defense_weight", 5.0 * SimRandom.Shared.NextDouble()),
            ("city_bonus", 100.0 * SimRandom.Shared.NextDouble()));

        var war = P(
            ("probability", probability),
            ("score", score),
            ("combat", combat),
            ("min_strength_ratio", 1.1 + SimRandom.Shared.NextDouble()),
            ("min_economy", 10000.0 * SimRandom.Shared.NextDouble()));

        var aggression = extremism * 2.0 + AI_CHAOS * SimRandom.Shared.NextDouble();
        _personality = P(
            ("economy", economy),
            ("military", military),
            ("war", war),
            ("aggression", aggression));

        // Adjust war probability based on extremism.
        probability["base"] = D(probability, "base") * (1.0 + extremism);
        probability["tension_factor"] = (2.0 + extremism * 5.0) * SimRandom.Shared.NextDouble();
        Flatten("", _personality, _traits);

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
        var atWar = WarManager.Instance.IsCountryAtWar(_country.countryName);
        var tick = atWar ? TICK_RATE_WAR : TICK_RATE_PEACE;
        if (Engine.GetFramesDrawn() % tick != 0)
            return;

        var enemies = Enemies(_country.countryName);
        ExecuteBest(new List<AiAction>
        {
            new(ScoreFrontline(enemies), () => ExecuteFrontline(enemies)),
        });
    }

    // Called on a worker. Produces commands only; never touches Godot objects.
    public AiPlan PlanDay(AiSnapshot snapshot, int index)
    {
        _snap = snapshot;
        _self = snapshot.countries[index];
        _rng = new RandomStream();
        _rng.Reset(snapshot.seedBase + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL);
        _plan = new AiPlan();

        ExecuteBest(new List<AiAction>
        {
            new(ScoreFactory(), ExecuteFactory),
            new(ScoreTrain(), ExecuteTrain),
            new(ScoreWar(), ExecuteWar),
            new(ScoreCallToArms(), ExecuteCallToArms),
        });

        var result = _plan;
        _snap = null;
        _self = null;
        _plan = null;
        return result;
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
        if (_self.money < 1000)
            return 0;

        return Math.Max(0.1, 1.0 - _self.factories * T("economy.industry_amount_factor"))
            * T("economy.industry");
    }

    private double ScoreTrain()
    {
        if (_self.money < 500 || _self.manpower < 10000)
            return 0;

        return T("military.training_factor");
    }

    private double ScoreWar()
    {
        return _self.isPuppet ? 0 : _snap.worldTension * T("aggression");
    }

    private double ScoreFrontline(Godot.Collections.Array<string> enemies)
    {
        if (enemies.Count > 0)
        {
            var deployed = 0;
            foreach (var troop in Troops(_country.countryName))
            {
                if (troop != null)
                    deployed += troop.divisionsCount;
            }
            if (deployed < EMERGENCY_DEPLOYMENT_THRESHOLD)
                return 10.0;
        }
        return 1.0; // Always high priority to manage the frontline.
    }

    private bool ExecuteFactory()
    {
        // Dispatcher validates against the functional-controller list; owned provinces
        // occupied by an enemy are rejected there, so never offer them as candidates.
        var name = _self.name;
        if (!_snap.provincesByCountry.TryGetValue(name, out var pids))
            return false;

        var queue = _snap.constructionQueue;
        var candidates = new List<int>();
        foreach (var pid in pids)
        {
            if (queue.Contains(pid))
                continue;
            if (!_snap.provinces.TryGetValue(pid, out var province) || province == null || province.buildings.Count >= 4)
                continue;
            candidates.Add(pid);
        }
        if (candidates.Count == 0)
            return false;

        var chosen = candidates[_rng.RandRange(0, candidates.Count - 1)];
        _plan.Actions.Add(new BuildIndustryAction
        {
            Issuer = name,
            ProvinceId = chosen,
            Country = name,
            Type = IndustryFactory,
        });
        return true;
    }

    // Improved: recruit based on current needs (more if at war).
    private bool ExecuteTrain()
    {
        var template = DivisionData.TEMPLATES["infantry"];
        var maxAffordable = Math.Min(
            (int)(_self.money / template.Get("cost").Double()),
            (int)(_self.manpower / template.Get("manpower").Double()));
        if (maxAffordable < 1)
            return false;

        var limit = _self.enemies.Count > 0 ? 20 : 10;
        _plan.Actions.Add(new QueueTrainingAction
        {
            Issuer = _self.name,
            Country = _self.name,
            Count = Math.Clamp(maxAffordable, 1, limit),
            Type = "infantry",
        });
        return true;
    }

    private bool ExecuteWar()
    {
        var c = _self;
        var extremism = c.extremism;
        var aggression = T("aggression");
        var tension = _snap.worldTension;

        // 1. THE STOCHASTIC GATE (randomness + world tension).
        if (tension < 0.15 - extremism * 0.1 && aggression < 2.5)
            return false;
        if (_rng.NextDouble() > T("war.probability.base") + tension * T("war.probability.tension_factor"))
            return false;

        // 2. COOLDOWNS & OVEREXTENSION.
        var frameNow = _snap.frame;
        if (frameNow - _lastDeclareFrame < DECLARE_WAR_COOLDOWN_FRAMES
            || c.enemies.Count >= MAX_PARALLEL_WARS
            || c.money < T("war.min_economy"))
            return false;

        // Pre-calculate our faction set for O(1) intersection checks in the filter.
        var ourFactions = new HashSet<string>(c.factions);
        var candidates = new List<string>();
        foreach (var enemy in PlanNeighborCountries())
        {
            var enemyData = PlanCountry(enemy);
            if (enemyData == null)
                continue;

            // Cheapest check first: relation/aggression.
            if (!(PlanRelation(c.name, enemy) < 20 || aggression > 2.0))
                continue;

            // Faction check: skip if we share any faction.
            if (enemyData.factions.Any(ourFactions.Contains))
                continue;

            candidates.Add(enemy);
        }

        // Safety: don't declare war with very few troops.
        if (PlanStrength(c.name, true) < MIN_TOTAL_POWER_FOR_WAR)
            return false;
        if (candidates.Count == 0)
            return false;

        var bestScore = double.NegativeInfinity;
        var bestTarget = "";

        var puppeteers = new HashSet<string>(c.puppets);
        if (c.isPuppet)
        {
            puppeteers.Add(c.owner);
            var ownerData = PlanCountry(c.owner);
            if (ownerData != null)
                foreach (var puppet in ownerData.puppets)
                    puppeteers.Add(puppet);
        }

        foreach (var targetName in candidates)
        {
            var targetData = PlanCountry(targetName);
            if (targetData == null)
                continue;
            if (PlanSharesFaction(c, targetData))
                continue;
            if (_snap.wars.Contains((c.name, targetName)))
                continue;
            if (puppeteers.Contains(targetName))
                continue;

            // 3. SCALE ANALYSIS (prevent massive world wars).
            var ownMembers = PlanSideMembers(c.name);
            var targetMembers = PlanSideMembers(targetName);
            if (ownMembers.Count + targetMembers.Count > MAX_COMBINED_WAR_MEMBERS && aggression < 2.5)
                continue;

            // 4. STRENGTH & DISTANCE ANALYSIS (collective security).
            var ownSideStrength = PlanSideStrength(c.name);
            var targetSideStrength = PlanSideStrength(targetName);
            var ratio = ownSideStrength / Math.Max(1.0, targetSideStrength);
            if (ratio < T("war.min_strength_ratio"))
                continue;

            // 5. DYNAMIC SCORING, capped city count for balance, plus a bit of randomness.
            var cityCount = _snap.citiesByCountry.TryGetValue(targetName, out var cities) ? cities.Length : 0;
            var targetScore = (ratio - 1.0) * T("war.score.strength")
                + targetData.money * T("war.score.money_factor")
                + Math.Min(cityCount, (int)T("war.score.max_cities")) * T("war.score.cities")
                + _rng.RandRange(-0.5, 0.5);

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
            var targetRef = PlanCountry(bestTarget);
            if (targetRef != null)
                _plan.Actions.Add(new DeclareWarAction
                {
                    Issuer = c.name,
                    Warer = c.name,
                    Waree = bestTarget,
                });
            // Aggressive/extreme countries cause more tension.
            _plan.TensionDelta += (float)(0.02 + extremism * 0.03 + aggression * 0.01);
            _lastDeclareFrame = frameNow;
        }
        return true;
    }

    // Returns false because the GDScript version is void (null is falsy in
    // _execute_best), so it never short-circuits the other daily actions.
    private bool ExecuteFrontline(Godot.Collections.Array<string> enemies)
    {
        var name = _country.countryName;

        // Deploy ready troops to random cities so they spread out.
        if (_country.readyTroops.Count > 0)
        {
            var ownedCities = MapManager.Instance.countryToCities;
            var cities = ownedCities.ContainsKey(name) ? ownedCities[name] : new List<int>();
            if (cities.Count > 0)
            {
                // Duplicate to safely erase during iteration.
                foreach (var troopData in new List<ReadyTroop>(_country.readyTroops))
                {
                    var city = cities[SimRandom.Shared.RandRange(0, cities.Count - 1)];
                    TroopManager.Instance.DeployReady(name, troopData, city);
                    _country.readyTroops.Remove(troopData);
                }
            }
        }

        var idleTroops = new List<TroopData>();
        foreach (var troop in Troops(name))
        {
            if (troop != null && !troop.isMoving)
                idleTroops.Add(troop);
        }
        if (idleTroops.Count == 0)
            return false;

        var movePayload = new List<Dictionary<string, object?>>();
        var graph = Graph;

        if (enemies.Count == 0)
        {
            var hubsVar = MapManager.Instance.countryToCities;
            var hubs = hubsVar.ContainsKey(name) ? hubsVar[name] : new List<int>();
            foreach (var troop in idleTroops)
            {
                if (hubs.Contains(troop.provinceId))
                    continue;

                // Choose the closest hub to avoid unnecessary long moves.
                var troopPos = graph.GetPointPosition(troop.provinceId);
                var bestHub = -1L;
                var bestDistance = float.MaxValue;
                foreach (var hub in hubs)
                {
                    var distance = troopPos.DistanceSquaredTo(graph.GetPointPosition(hub));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestHub = hub;
                    }
                }
                if (bestHub >= 0)
                {
                    movePayload.Add(new Dictionary<string, object?>{
                        ["troop"] = troop,
                        ["province_id"] = bestHub,
				});
                }
            }
            if (movePayload.Count > 0)
                Issue(new MoveTroopsAction
                {
                    Issuer = _country.countryName,
                    Assignments = movePayload,
                });
            return false;
        }

        // Weighted targets: cities, troops, and empty gaps.
        var targets = new List<Target>();
        var seen = new HashSet<long>();
        var enemySet = new HashSet<string>();
        foreach (var enemy in enemies)
            enemySet.Add(enemy);

        var citySet = new HashSet<long>();
        foreach (var cityVar in MapManager.Instance.allCities)
            citySet.Add(cityVar["id"].AsInt64());

        var combat = Section("war")["combat"].AsGodotDictionary();
        var provinces = ProvinceObjects;
        foreach (var myPid in MapManager.Instance.GetProvincesBorderingEnemies(name, enemies))
        {
            foreach (var nId in graph.GetPointConnections(myPid))
            {
                if (!provinces.TryGetValue((int)nId, out var province) || province == null)
                    continue;

                var enemyName = province.GetFunctionalOwner();
                if (!enemySet.Contains(enemyName) || seen.Contains(nId))
                    continue;
                seen.Add(nId);

                var enemyStrength = TroopManager.Instance.GetProvinceStrength((int)nId, enemyName);
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
                    if (!provinces.TryGetValue((int)dnId, out var deepProvince) || deepProvince == null)
                        continue;
                    if (deepProvince.GetFunctionalOwner() == enemyName
                        && !seen.Contains(dnId)
                        && citySet.Contains(dnId))
                    {
                        var deepStrength = TroopManager.Instance.GetProvinceStrength((int)dnId, enemyName);
                        targets.Add(new Target((int)dnId, 0.0, deepStrength, D(combat, "city_bonus") * 0.8));
                    }
                }
            }
        }
        if (targets.Count == 0)
            return false;

        var centers = MapManager.Instance.provinceCenters;
        foreach (var troop in idleTroops)
        {
            var troopPos = centers[troop.provinceId];

            // Sort targets by score / (distance * 0.01 + 1), descending.
            double WeightedScore(Target t) =>
                t.Score / (troopPos.DistanceTo(centers[t.Id]) * 0.01 + 1.0);
            targets.Sort((a, b) => WeightedScore(b).CompareTo(WeightedScore(a)));

            var divisionsLeft = troop.divisionsCount;
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
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

                movePayload.Add(new Dictionary<string, object?>{
                    ["troop"] = troop,
                    ["province_id"] = target.Id,
                    ["divisions"] = amount,
				});

                target.VirtualStrength += amount;
                targets[i] = target;
                divisionsLeft -= amount;
            }
        }

        if (movePayload.Count > 0)
            Issue(new MoveTroopsAction
            {
                Issuer = _country.countryName,
                Assignments = movePayload,
            });
        return false;
    }

    private double ScoreCallToArms()
    {
        if (_self.enemies.Count == 0)
            return 0.0;
        if (_self.factions.Length == 0)
            return 0.0;
        return 1.5;
    }

    private bool ExecuteCallToArms()
    {
        var calledAny = false;
        var name = _self.name;

        foreach (var factionName in _self.factions)
        {
            if (!_snap.factionMembers.TryGetValue(factionName, out var members))
                continue;

            // CIVIL WAR CHECK: don't call allies if the enemy is in the same faction.
            var isInternalConflict = false;
            foreach (var member in members)
            {
                if (_self.enemies.Contains(member))
                {
                    isInternalConflict = true;
                    break;
                }
            }
            if (isInternalConflict)
                continue;

            foreach (var member in members)
            {
                if (member == name || PlanCountry(member) == null)
                    continue;

                _plan.Actions.Add(new CallToArmsAction
                {
                    Issuer = name,
                    Caller = name,
                    Target = member,
                });
                calledAny = true;
            }
        }
        return calledAny;
    }

    // --- UTILITIES ---

    private List<string> GetNeighborCountries()
    {
        if (!_neighborsDirty)
            return _neighborCache;

        var neighbors = new HashSet<string>();
        var provinces = MapManager.Instance.countryToProvinces;
        if (provinces.TryGetValue(_country.countryName, out var pids))
        {
            foreach (var pid in pids)
            {
                foreach (var nid in Graph.GetPointConnections(pid))
                {
                    if (!ProvinceObjects.TryGetValue((int)nid, out var province) || province == null)
                        continue;

                    var owner = !string.IsNullOrEmpty(province.occupier) ? province.occupier : province.country;
                    if (owner != "" && owner != "Sea" && owner != _country.countryName)
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

    private static Godot.Collections.Dictionary P(params (string Key, Variant Value)[] entries)
    {
        var dict = new Godot.Collections.Dictionary();
        foreach (var (key, value) in entries)
            dict[key] = value;
        return dict;
    }

    private double T(string key) => _traits.TryGetValue(key, out var value) ? value : 0;

    private static void Flatten(string prefix, Godot.Collections.Dictionary dict, Dictionary<string, double> output)
    {
        foreach (var keyVariant in dict.Keys)
        {
            var key = prefix == "" ? keyVariant.AsString() : prefix + "." + keyVariant.AsString();
            var value = dict[keyVariant];
            if (value.VariantType == Variant.Type.Dictionary)
                Flatten(key, value.AsGodotDictionary(), output);
            else if (value.VariantType == Variant.Type.Int || value.VariantType == Variant.Type.Float)
                output[key] = value.AsDouble();
        }
    }

    private static double D(Godot.Collections.Dictionary<string, Variant> dict, string key) => dict[key].AsDouble();
    private static double D(Godot.Collections.Dictionary dict, string key) => dict[key].AsDouble();

    private Godot.Collections.Dictionary Section(string name) => _personality[name].AsGodotDictionary();

    private static double WorldTension => MapManager.Instance.worldTension;
    private static SoiStar Graph => MapManager.Instance.provinceGraph as SoiStar;
    private static Dictionary<int, Province> ProvinceObjects => MapManager.Instance.provinceObjects;
    private static FactionManager FactionManagerSingleton => FactionManager.Instance as FactionManager;
    private static CountryData Country(string name) => CountryManager.Instance.GetCountry(name);
    private static Godot.Collections.Array<string> Enemies(string name)
    {
        var result = new Godot.Collections.Array<string>();
        foreach (var enemy in WarManager.Instance.GetEnemiesOf(name))
            result.Add(enemy.AsString());
        return result;
    }

    private static List<TroopData> Troops(string name)
    {
        var result = new List<TroopData>();
        foreach (var troop in TroopManager.Instance.GetTroopsForCountry(name))
            if (troop is TroopData data)
                result.Add(data);
        return result;
    }

    // --- READ-ONLY SNAPSHOT HELPERS (day planning only) ---

    private AiCountrySnapshot PlanCountry(string name) =>
        _snap.index.TryGetValue(name, out var i) ? _snap.countries[i] : null;

    private int PlanRelation(string from, string to)
    {
        if (!_snap.index.ContainsKey(to))
            return 0;
        var country = PlanCountry(from);
        return country != null && country.relations.TryGetValue(to, out var value) ? value : 50;
    }

    private bool PlanSharesFaction(AiCountrySnapshot a, AiCountrySnapshot b)
    {
        var own = new HashSet<string>(a.factions);
        return b.factions.Any(own.Contains);
    }

    private string[] PlanNeighborCountries() =>
        _snap.neighborCountries.TryGetValue(_self.name, out var list) ? list : Array.Empty<string>();

    private double PlanStrength(string name, bool onlyDeployed)
    {
        var country = PlanCountry(name);
        if (country == null)
            return 0.1;
        double total = 0;
        if (!onlyDeployed)
        {
            total += country.manpower * 0.05;
            total += country.money * 0.01;
        }
        total += country.mobilized;
        return Math.Max(0.1, total);
    }

    private double PlanSideStrength(string name)
    {
        var total = 0.0;
        foreach (var member in PlanSideMembers(name))
            total += PlanStrength(member, false);
        return total;
    }

    private List<string> PlanSideMembers(string name)
    {
        var side = new List<string> { name };
        var country = PlanCountry(name);
        if (country == null)
            return side;

        foreach (var faction in country.factions)
            if (_snap.factionMembers.TryGetValue(faction, out var members))
                foreach (var member in members)
                    if (!side.Contains(member))
                        side.Add(member);

        var all = new List<string>(side);
        foreach (var member in side)
        {
            var data = PlanCountry(member);
            if (data == null)
                continue;
            foreach (var puppet in data.puppets)
                if (!all.Contains(puppet))
                    all.Add(puppet);
        }
        return all;
    }

    private readonly record struct AiAction(double Score, Func<bool> Action);

    private record struct Target
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

// Output of one AI plan. Applied on the main thread after all plans finish.
public sealed class AiPlan
{
    public List<GameAction> Actions = new();
    public float TensionDelta;
}
