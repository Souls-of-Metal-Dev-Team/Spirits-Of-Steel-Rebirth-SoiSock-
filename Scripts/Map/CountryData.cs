using System;
using System.Collections.Generic;
using Godot;

namespace Sosr;

// Small surface used by CountryAI.cs. Keep the GDScript member names; the AI
// port was written against them and uses the type name ProcessDay.
public abstract partial class ProcessDay : RefCounted
{
    [Export] public string country_name = "Sea";
    [Export] public double money = 0.0;
    [Export] public int factories_amount = 0;
    [Export] public int manpower = 0;
    [Export] public int mobilized = 0;
    [Export] public bool is_puppet = false;
    [Export] public string owner = "";
    [Export] public Godot.Collections.Array<string> puppets = [];
    [Export] public Godot.Collections.Array<string> factions = [];
    [Export] public Godot.Collections.Array<ReadyTroop> ready_troops = [];

    public abstract Vector2 ideology { get; set; }
    public abstract int get_relation_with(string other_country_name);
    public abstract bool train_troops(int count, string type = "infantry");
}

[GlobalClass]
public partial class CountryData : ProcessDay
{
    static CountryData SEA_COUNTRY_DATA = new();

    // Country name of any country object, or "" for null.
    public static string NameOf(GodotObject a_country)
        => a_country == null ? "" : a_country.Get("country_name").AsString();

    [Export] public double economy_law_penalty = 0.0; // 0.10 means 10% income loss

    Godot.Collections.Dictionary army_composition_cache = new() {
        { "infantry", 0 },
        { "tank", 0 },
        { "artillery", 0 },
    };

    //region --- Configuration & Constants ---
    const double MANPOWER_RECOVERY_PER_YEAR = 0.10;
    const double MANPOWER_RECOVERY_PER_DAY = MANPOWER_RECOVERY_PER_YEAR / 365.0;
    [Export] public double military_size_ratio = 0.005;
    const int BASE_ARMY_COST = 20;
    //endregion

    //region --- Properties ---
    [Export] public string display_name = "Sea";
    [Export] public string capital = "";
    [Export] public Godot.Color country_color = new Godot.Color(1, 1, 1);
    [Export] public bool is_player = false;
    [Export] public bool is_exiled = false;
    CountryAI ai_controller = null;

    [Export] public Godot.Collections.Dictionary<string, int> relations = new();

    double factory_port_daily_cost = 0.2; // The less the better. It's percentage based

    // Economy
    [Export] public int gdp = 0;
    [Export] public double income = 0.0;
    [Export] public int factory_income = 100;
    [Export] public double hourly_money_income = 0.0;
    [Export] public double divCostMod = 1.0;

    [Signal] public delegate void TrainingFinishedEventHandler();
    [Signal] public delegate void IdeologyChangedEventHandler();

    // Politics
    [Export] public Godot.Collections.Dictionary<string, IdeologyDriftTarget> driftTargets = new();
    [Export] public double political_power = 5000.0;
    [Export] public double base_daily_pp_gain = 0.04;
    [Export] public double daily_pp_gain = 0.04;
    [Export] public double base_stability = 0.5;
    [Export] public double stability = 0.5;
    [Export] public double base_war_support = 0.5;
    [Export] public double war_support = 0.5;
    [Export] public string ideology_name = "neutral";
    private Vector2 _ideology = new Vector2(
        GD.RandRange(-100, 100),
        GD.RandRange(-100, 100)
    );

    [Export]
    public override Vector2 ideology
    {
        get => _ideology;
        set
        {
            _ideology = value;
            RefreshIdeologyName();
            EmitSignal(SignalName.IdeologyChanged);
        }
    }

    // Population & Manpower
    [Export] public int total_population = 0;
    [Export] public int total_power = 1000;
    [Export] public int total_water = 1000;
    [Export] public int total_sewage = 1000;

    // Military State
    [Export] public int army_level = 1;
    [Export] public double army_cost = 0.0;
    [Export] public double troop_speed_modifier = 1.0;
    [Export] public double troopSpeedExtra = 1.0;
    [Export] public double troopAttackAdd = 0.0;
    [Export] public double troopAttackMod = 1.0;
    [Export] public double troopDefendAdd = 0.0;
    [Export] public double troopDefendMod = 1.0;
    [Export] public double attacker_attack_mitigation = 1.0;
    [Export] public double defender_attack_mitigation = 1.0;

    // Deployment & Training State
    [Export] public Godot.Collections.Array<string> allowedCountries = [];
    [Export] public Godot.Collections.Array<Variant> accepted_cultures = [];
    [Export] public Godot.Collections.Array<Variant> hostedGovernments = [];
    [Export] public string host = "";
    [Export] public Godot.Collections.Array<string> finished_decisions = [];
    public Godot.Collections.Array<TroopTraining> ongoing_training = [];
    [Export] public int deploy_pid = -1;
    [Export] public Godot.Collections.Array<Variant> pre_war_provinces = [];

    // for optimization
    [Export] public bool is_at_war = false;
    [Export] public bool war_dirty = true;
    [Export] public bool _is_loading = false;
    [Export] public bool dirty = true;
    [Export] public bool dirty_manpower = true;
    [Export] public Godot.Collections.Array enemies = [];
    [Export] public Godot.Collections.Array<string> figures = [];
    [Export]
    public Godot.Collections.Dictionary<string, Godot.Collections.Array<string>> government_positions = new() {
        { "Leader", new Godot.Collections.Array<string>() }
    };
    // GDScript UI code still uses the old GDScript spelling.
    [Export]
    public Godot.Collections.Dictionary<string, Godot.Collections.Array<string>> governmentPositions
    {
        get => government_positions;
        set => government_positions = value;
    }
    [Export] public Godot.Collections.Array<Variant> cached_garrison_hubs = [];
    //endregion

    public CountryData()
    {
        ai_controller = new CountryAI();
        ai_controller.init(this);
    }

    //region --- Save / Load ---
    public Godot.Collections.Dictionary ToDict()
    {
        return new Godot.Collections.Dictionary
        {
            { "name", country_name },
            { "display_name", display_name },
            { "capital", capital },
            { "color", "#" + country_color.ToHtml(false).ToUpperInvariant() },
            { "money", money },
            { "ideology", new Godot.Collections.Array { ideology.X, ideology.Y } },
            { "political_power", political_power },
            { "stability", base_stability },
            { "war_support", base_war_support },
            { "puppets", puppets },
            { "accepted_cultures", accepted_cultures },
            { "hostedGovernments", hostedGovernments },
            { "figures", figures },
            { "government_positions", government_positions },
            { "is_player", is_player },
            { "finished_decisions", finished_decisions },
        };
    }

    public static CountryData FromDict(Godot.Collections.Dictionary a_data)
    {
        var country = new CountryData();

        country.country_name = a_data.ContainsKey("name") ? a_data["name"].AsString() : "Sea";
        country.display_name = a_data.ContainsKey("display_name")
            ? a_data["display_name"].AsString()
            : country.country_name;
        country.capital = a_data.ContainsKey("capital") ? a_data["capital"].AsString() : "";
        country.country_color = Color.FromHtml(a_data.ContainsKey("color") ? a_data["color"].AsString() : "#FFFFFF");
        country.money = a_data.ContainsKey("money") ? a_data["money"].AsDouble() : 0.0;

        var ideologyArray = a_data.ContainsKey("ideology") ? a_data["ideology"].AsGodotArray() : new Godot.Collections.Array { 0, 0 };
        country.ideology = new Vector2(ideologyArray[0].AsSingle(), ideologyArray[1].AsSingle());

        country.political_power = a_data.ContainsKey("political_power") ? a_data["political_power"].AsDouble() : 5000.0;
        country.base_stability = a_data.ContainsKey("stability") ? a_data["stability"].AsDouble() : 0.5;
        country.base_war_support = a_data.ContainsKey("war_support") ? a_data["war_support"].AsDouble() : 0.5;
        country.puppets = StringArray(a_data, "puppets");
        country.accepted_cultures = VariantArray(a_data, "accepted_cultures");
        country.hostedGovernments = VariantArray(a_data,
            a_data.ContainsKey("hostedGovernments") ? "hostedGovernments" : "hosted_governments");
        country.allowedCountries.Add(country.country_name);
        country.allowedCountries.Add("Sea");
        country.figures = StringArray(a_data, "figures");

        if (a_data.ContainsKey("government_positions"))
        {
            var positions = a_data["government_positions"].AsGodotDictionary();
            foreach (var key in positions.Keys)
            {
                var position = key.AsString();
                var names = new Godot.Collections.Array<string>();
                var value = positions[key];
                if (value.VariantType == Variant.Type.String)
                    names.Add(value.AsString());
                else
                    foreach (var name in value.AsGodotArray())
                        names.Add(name.AsString());
                country.government_positions[position] = names;
            }
        }

        country.is_player = a_data.ContainsKey("is_player") && a_data["is_player"].AsBool();
        if (a_data.ContainsKey("finished_decisions"))
        {
            foreach (var idVariant in a_data["finished_decisions"].AsGodotArray())
            {
                var id = idVariant.AsString();
                country.finished_decisions.Add(id);
                country.SetMeta("finished_" + id, true);
            }
        }

        country.stability = country.base_stability;
        country.war_support = country.base_war_support;
        return country;
    }

    private static Godot.Collections.Array<string> StringArray(Godot.Collections.Dictionary data, string key)
    {
        var result = new Godot.Collections.Array<string>();
        if (data.ContainsKey(key))
            foreach (var value in data[key].AsGodotArray())
                result.Add(value.AsString());
        return result;
    }

    private static Godot.Collections.Array<Variant> VariantArray(Godot.Collections.Dictionary data, string key)
    {
        var result = new Godot.Collections.Array<Variant>();
        if (data.ContainsKey(key))
            foreach (var value in data[key].AsGodotArray())
                result.Add(value);
        return result;
    }
    //endregion

    //region --- Cabinet ---
    public void InitializeCabinet()
    {
        foreach (var position in government_positions.Keys)
        {
            if (!government_positions.TryGetValue(position, out var names))
                continue;

            foreach (var figureName in names)
            {
                var figure = GetFigure(figureName);
                if (figure != null)
                    ApplySkills(figure.skills, true);
            }
        }
    }

    public void RemoveGovernmentPositionFigure(string a_position, string a_figure)
    {
        var figure = GetFigure(a_figure);
        if (figure == null)
            return;

        ApplySkills(figure.skills, false);
        figure.occupation = "";
        if (government_positions.ContainsKey(a_position))
            government_positions[a_position].Remove(figure.name);
    }

    public void RemoveGovernmentPositions(string a_position)
    {
        if (!government_positions.ContainsKey(a_position))
            return;

        var names = new List<string>(government_positions[a_position]);
        foreach (var figureName in names)
            RemoveGovernmentPositionFigure(a_position, figureName);
    }

    public void SetGovernmentPosition(string a_position, ImportantFigure a_figure)
    {
        RemoveGovernmentPositionFigure(a_figure.occupation, a_figure.name);
        if (!government_positions.ContainsKey(a_position))
            government_positions[a_position] = new Godot.Collections.Array<string>();
        government_positions[a_position].Add(a_figure.name);
        ApplySkills(a_figure.skills, true);
        a_figure.occupation = a_position;
    }

    private static ImportantFigure GetFigure(string figureName)
    {
        var figuresDict = MapManager.Instance.significantFigures;
        if (!figuresDict.ContainsKey(figureName))
            return null;
        return figuresDict[figureName] as ImportantFigure;
    }

    private void ApplySkills(Godot.Collections.Dictionary skills, bool add)
    {
        foreach (var key in skills.Keys)
        {
            var skill = key.AsString();
            var value = skills[key].AsDouble();
            if (value == 0.0)
                continue;

            switch (skill)
            {
                case "stability":
                    stability += add ? value : -value;
                    break;
                case "warSupport":
                    war_support += add ? value : -value;
                    break;
                case "dailyPPGain":
                    daily_pp_gain += add ? value : -value;
                    break;
                case "divCostMod":
                    divCostMod = add ? divCostMod * value : divCostMod / value;
                    break;
                case "troopSpeed":
                    troopSpeedExtra += add ? value : -value;
                    break;
                case "troopAttackAdd":
                    troopAttackAdd += add ? value : -value;
                    break;
                case "troopAttackMod":
                    troopAttackMod = add ? troopAttackMod * value : troopAttackMod / value;
                    break;
                case "troopDefendAdd":
                    troopDefendAdd += add ? value : -value;
                    break;
                case "troopDefendMod":
                    troopDefendMod = add ? troopDefendMod * value : troopDefendMod / value;
                    break;
                case "attackerAttackMitigation":
                    attacker_attack_mitigation = add ? attacker_attack_mitigation * value : attacker_attack_mitigation / value;
                    break;
                case "defenderAttackMitigation":
                    defender_attack_mitigation = add ? defender_attack_mitigation * value : defender_attack_mitigation / value;
                    break;
            }
        }
    }
    //endregion

    //region --- Lifecycle ---
    public Godot.Collections.Array<string> GetAllAllowedCountries()
    {
        var combined = new Godot.Collections.Array<string>();
        foreach (var country in allowedCountries)
            combined.Add(country);

        var factionManager = (FactionManager)FactionManager.Instance;
        foreach (var factionName in factions)
        {
            if (!factionManager.factions.ContainsKey(factionName))
                continue;

            foreach (var member in factionManager.factions[factionName].members)
                if (!combined.Contains(member.polity))
                    combined.Add(member.polity);
        }
        return combined;
    }

    public Godot.Collections.Array<string> get_all_allowed_countries() => GetAllAllowedCountries();

    public void ProcessHour()
    {
        if (_is_loading)
            return;

        political_power += daily_pp_gain;

        hourly_money_income = ((gdp * 0.0000228310502) + (factories_amount * factory_income)) * (1.0 - economy_law_penalty);
        army_cost = CalculateArmyUpkeep();
        income = hourly_money_income - army_cost;
        money += income;
        troop_speed_modifier = 1.0 + (army_level * 0.1) + troopSpeedExtra;

        UpdateManpowerPool();
        _ProcessReinforcements();

        if (dirty_manpower && !dirty)
            UpdateManpowerPool();

        if (war_dirty)
            UpdateIsAtWar();

        if (!is_player)
            ai_controller.ThinkHour();
    }

    public void process_hour() => ProcessHour();

    public void ProcessDay()
    {
        if (_is_loading)
            return;

        var moveAmount = Vector2.Zero;
        foreach (var ideaTarget in driftTargets.Values)
            moveAmount += ideology.DirectionTo(ideaTarget.finalPosition) * ideaTarget.driftAmount;
        ideology += moveAmount;

        _RefreshEconomicStats();
        _ProcessTraining();
        ((DecisionManager)DecisionManager.Instance).process_country_day(this);

        if (!is_player)
            ai_controller.ThinkDay();
    }

    public void process_day() => ProcessDay();

    private void _RefreshEconomicStats()
    {
        if (!dirty)
            return;

        total_population = CountryManager.Instance.get_country_population(country_name);
        factories_amount = CountryManager.Instance.get_factories_amount(country_name);
        gdp = (int)(CountryManager.Instance.get_country_gdp(country_name) * total_population * 0.000001);

        UpdateManpowerPool();
        dirty = false;
    }

    public void RefreshIdeologyName()
    {
        // During autoload construction IdeologyManager may not exist yet; the
        // GDScript version fell back to "neutral" in that case.
        var manager = IdeologyManager.Instance as IdeologyManager;
        if (manager == null)
        {
            ideology_name = "neutral";
            return;
        }
        ideology_name = manager.get_ideology_name(ideology);
    }

    public void refresh_ideology_name() => RefreshIdeologyName();
    //endregion

    //region --- Military Management ---
    public override bool train_troops(int count, string type = "infantry")
    {
        if (!DivisionData.TEMPLATES.ContainsKey(type))
        {
            GD.PushError($"Unknown division type: {type}");
            return false;
        }

        var template = DivisionData.TEMPLATES[type];
        var totalManpowerNeeded = count * template["manpower"].AsInt32();

        if (manpower < totalManpowerNeeded || money < count * template["cost"].AsDouble() * divCostMod)
            return false;

        manpower -= totalManpowerNeeded;
        mobilized += totalManpowerNeeded;

        ongoing_training.Add(TroopTraining.FromValues(
            count,
            type,
            template["days"].AsInt32(),
            template["cost"].AsDouble()));
        return true;
    }

    private void _ProcessTraining()
    {
        for (var i = ongoing_training.Count - 1; i >= 0; i--)
        {
            var training = ongoing_training[i];
            var batchDailyCost = training.divisions_count * training.daily_cost * divCostMod;

            if (money >= batchDailyCost)
            {
                money -= batchDailyCost;
                training.days_left -= 1;
            }

            if (training.days_left <= 0)
            {
                _GraduateTroops(training);
                ongoing_training.RemoveAt(i);
            }
        }
    }

    private void _GraduateTroops(TroopTraining training)
    {
        ready_troops.Add(ReadyTroop.FromValues(
            DivisionData.create_division(training.division_type),
            training.divisions_count));
        EmitSignal(SignalName.TrainingFinished);
    }

    public void UpdateManpowerPool()
    {
        var maxAllowedManpower = (int)(total_population * military_size_ratio);
        var totalMobilized = manpower + mobilized;

        if (totalMobilized < maxAllowedManpower)
        {
            var dailyGrowth = Math.Max(1, (int)(total_population * 0.0001));
            manpower += dailyGrowth;

            if (totalMobilized > maxAllowedManpower)
                manpower = Math.Max(0, maxAllowedManpower - mobilized);
        }

        manpower = Math.Max(0, manpower);
        dirty_manpower = false;
    }

    public double CalculateArmyUpkeep()
    {
        var activeDivisions = 0;
        foreach (var troop in GetTroops(country_name))
            activeDivisions += troop.divisions_count;

        var reserveDivisions = 0;
        foreach (var readyTroop in ready_troops)
            reserveDivisions += readyTroop.count;

        return (activeDivisions + (reserveDivisions * 0.25)) * army_level * BASE_ARMY_COST * divCostMod;
    }

    public double calculate_army_upkeep() => CalculateArmyUpkeep();

    private IReadOnlyList<TroopData> GetTroops(string country)
        => TroopManager.Instance.troops_by_country.TryGetValue(country, out var troops)
            ? troops
            : Array.Empty<TroopData>();

    private void _SetupStartingArmy()
    {
        var projectedIncome = ((gdp * 0.0000228310502) + factories_amount * factory_income) * 0.25;
        var upkeep = Math.Max(1.0, army_level * BASE_ARMY_COST);
        var finalCount = Math.Clamp((int)(projectedIncome / upkeep), 1, 6);

        var template = DivisionData.TEMPLATES["infantry"];
        var manpowerPerDivision = template["manpower"].AsInt32();

        if (manpower < finalCount * manpowerPerDivision)
            finalCount = (int)(manpower / Math.Max(1, manpowerPerDivision));

        if (finalCount <= 0)
            return;

        var startingDivisions = new Godot.Collections.Array<DivisionData>();
        for (var i = 0; i < finalCount; i++)
            startingDivisions.Add(DivisionData.create_division("infantry"));

        manpower -= finalCount * manpowerPerDivision;
        CallDeferred("_deploy_initial_force", startingDivisions);
    }

    private void _DeployInitialForce(Godot.Collections.Array<DivisionData> divisions)
    {
        var provincesDict = MapManager.Instance.country_to_provinces;
        if (!provincesDict.TryGetValue(country_name, out var provinces))
            return;

        if (provinces.Count == 0)
            return;

        var cities = MapManager.Instance.GetCitiesProvinceCountry(country_name);

        var currentBatch = new Godot.Collections.Array<DivisionData>();
        for (var i = 0; i < divisions.Count; i++)
        {
            currentBatch.Add(divisions[i]);
            if (currentBatch.Count < 5 && i != divisions.Count - 1)
                continue;

            var targetPid = cities.Count > 0
                ? cities[GD.RandRange(0, cities.Count - 1)].AsInt32()
                : provinces[GD.RandRange(0, provinces.Count - 1)];
            var payload = new Godot.Collections.Array();
            foreach (var division in currentBatch)
                payload.Add(division);
            TroopManager.Instance.deploy_specific_divisions(country_name, payload, targetPid);
            currentBatch = new Godot.Collections.Array<DivisionData>();
        }
    }

    private void _ProcessReinforcements()
    {
        foreach (var troop in GetTroops(country_name))
        {
            if (troop.is_moving)
                continue;

            foreach (var division in troop.stored_divisions)
            {
                if (division.hp >= division.max_hp || division.max_hp <= 0)
                    continue;

                var percentage = Math.Min(0.05, (division.max_hp - division.hp) / division.max_hp);
                var template = DivisionData.TEMPLATES[division.type];
                var menNeeded = (int)(template["manpower"].AsInt32() * percentage);
                var moneyNeeded = (int)(template["cost"].AsDouble() * percentage * divCostMod);

                if (manpower < menNeeded || money < moneyNeeded)
                    continue;

                money -= moneyNeeded;
                manpower -= menNeeded;
                mobilized += menNeeded;
                division.hp += (float)(percentage * division.max_hp);
            }
        }
    }

    public bool deploy_ready_troop(ReadyTroop troop, int specific_pid = -1)
    {
        var index = ready_troops.IndexOf(troop);
        if (index == -1)
            return false;

        var targetPid = specific_pid;
        if (targetPid == -1)
        {
            var provincesDict = MapManager.Instance.country_to_provinces;
            if (!provincesDict.TryGetValue(country_name, out var provinces))
                return false;

            if (provinces.Count == 0)
                return false;
            targetPid = provinces[GD.RandRange(0, provinces.Count - 1)];
        }

        TroopManager.Instance.DeployReady(country_name, troop, targetPid);
        ready_troops.RemoveAt(index);
        return true;
    }
    //endregion

    //region --- Stats & Manpower ---
    public double GetArmyPressure()
    {
        var armySize = 0;
        foreach (var troop in GetTroops(country_name))
            armySize += troop.divisions_count;

        var capacity = Math.Max(1.0, (gdp * 0.03) + factories_amount * 5);
        return armySize / capacity;
    }

    public double get_army_pressure() => GetArmyPressure();

    public double GetMaxMorale()
    {
        return (60.0 + (stability * 40.0) + (army_level * 5.0)) * (money < 0 ? 0.5 : 1.0);
    }

    public double get_max_morale() => GetMaxMorale();

    public double GetAttackEfficiency()
    {
        return (troopAttackAdd + 0.9 + (war_support * 0.3) + (army_level * 0.05)) * (money < 0 ? 0.7 : 1.0) * troopAttackMod;
    }

    public double get_attack_efficiency() => GetAttackEfficiency();

    public double GetDefenseEfficiency()
    {
        return (troopDefendAdd + 1.0 + (stability * 0.15) + (army_level * 0.05)) * (money < 0 ? 0.8 : 1.0) * troopDefendMod;
    }

    public double get_defense_efficiency() => GetDefenseEfficiency();
    //endregion

    //region --- Relations & War ---
    public void set_relation_with(string other_country_name, int value)
    {
        relations[other_country_name] = Math.Clamp(value, 0, 100);
    }

    public void update_relations()
    {
        var countries = CountryManager.Instance.countries;
        foreach (var key in new List<string>(relations.Keys))
        {
            if (!countries.ContainsKey(key))
            {
                relations[key] = 50;
                continue;
            }

            var other = countries[key].AsGodotObject();
            var otherIdeology = other.Get("ideology").AsVector2();
            relations[key] = (int)(141 - ideology.DistanceTo(otherIdeology));
        }
    }

    public override int get_relation_with(string other_country_name)
    {
        var countries = CountryManager.Instance.countries;
        if (!countries.ContainsKey(other_country_name))
        {
            relations.Remove(other_country_name);
            return 0;
        }

        return relations.ContainsKey(other_country_name) ? relations[other_country_name] : 50;
    }

    public void UpdateIsAtWar()
    {
        enemies = WarManager.Instance.get_enemies_of(country_name);
        is_at_war = enemies.Count > 0;
        war_dirty = false;
    }
    //endregion
}
