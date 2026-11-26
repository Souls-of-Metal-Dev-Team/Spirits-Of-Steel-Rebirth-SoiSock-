using System;
using System.Collections.Generic;
using Godot;

namespace Sosr;

[GlobalClass]
public partial class CountryData : RefCounted
{
    static CountryData SEA_COUNTRY_DATA = new();

    [Export] public string countryName = "Sea";
    [Export] public double money = 0.0;
    [Export] public int factoriesAmount = 0;
    [Export] public int manpower = 0;
    [Export] public int mobilized = 0;
    [Export] public bool isPuppet = false;
    [Export] public string owner = "";
    [Export] public Godot.Collections.Array<string> puppets = [];
    [Export] public Godot.Collections.Array<string> factions = [];
    public List<ReadyTroop> readyTroops = new();

    // Country name of any country object, or "" for null.
    public static string NameOf(GodotObject aCountry)
        => aCountry == null ? "" : aCountry.Get("countryName").AsString();

    [Export] public double economyLawPenalty = 0.0; // 0.10 means 10% income loss

    Godot.Collections.Dictionary armyCompositionCache = new() {
        { "infantry", 0 },
        { "tank", 0 },
        { "artillery", 0 },
    };

    //region --- Configuration & Constants ---
    const double MANPOWER_RECOVERY_PER_YEAR = 0.10;
    const double MANPOWER_RECOVERY_PER_DAY = MANPOWER_RECOVERY_PER_YEAR / 365.0;
    [Export] public double militarySizeRatio = 0.005;
    const int BASE_ARMY_COST = 20;
    //endregion

    //region --- Properties ---
    [Export] public string displayName = "Sea";
    [Export] public string capital = "";
    [Export] public Godot.Color countryColor = new Godot.Color(1, 1, 1);
    [Export] public bool isPlayer = false;
    [Export] public bool isExiled = false;
    CountryAI aiController = null;

    [Export] public Godot.Collections.Dictionary<string, int> relations = new();

    double factoryPortDailyCost = 0.2; // The less the better. It's percentage based

    // Economy
    [Export] public int gdp = 0;
    [Export] public double income = 0.0;
    [Export] public int factoryIncome = 100;
    [Export] public double hourlyMoneyIncome = 0.0;
    [Export] public double divCostMod = 1.0;

    [Signal] public delegate void TrainingFinishedEventHandler();
    [Signal] public delegate void IdeologyChangedEventHandler();

    // Politics
    public Dictionary<string, IdeologyDriftTarget> driftTargets = new();
    [Export] public double politicalPower = 5000.0;
    [Export] public double baseDailyPpGain = 0.04;
    [Export] public double dailyPpGain = 0.04;
    [Export] public double baseStability = 0.5;
    [Export] public double stability = 0.5;
    [Export] public double baseWarSupport = 0.5;
    [Export] public double warSupport = 0.5;
    [Export] public string ideologyName = "neutral";
    // Randomized in FromDict; autoloads are not reachable during field initialization.
    private Vector2 _ideology = Vector2.Zero;

    [Export]
    public Vector2 ideology
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
    [Export] public int totalPopulation = 0;
    [Export] public int totalPower = 1000;
    [Export] public int totalWater = 1000;
    [Export] public int totalSewage = 1000;

    // Military State
    [Export] public int armyLevel = 1;
    [Export] public double armyCost = 0.0;
    [Export] public double troopSpeedModifier = 1.0;
    [Export] public double troopSpeedExtra = 1.0;
    [Export] public double troopAttackAdd = 0.0;
    [Export] public double troopAttackMod = 1.0;
    [Export] public double troopDefendAdd = 0.0;
    [Export] public double troopDefendMod = 1.0;
    [Export] public double attackerAttackMitigation = 1.0;
    [Export] public double defenderAttackMitigation = 1.0;

    // Deployment & Training State
    [Export] public Godot.Collections.Array<string> allowedCountries = [];
    [Export] public Godot.Collections.Array<Variant> acceptedCultures = [];
    [Export] public Godot.Collections.Array<Variant> hostedGovernments = [];
    [Export] public string host = "";
    [Export] public Godot.Collections.Array<string> finishedDecisions = [];
    public List<TroopTraining> ongoingTraining = new();
    [Export] public int deployPid = -1;
    [Export] public Godot.Collections.Array<Variant> preWarProvinces = [];

    // for optimization
    [Export] public bool IsAtWar = false;
    [Export] public bool warDirty = true;
    [Export] public bool _is_loading = false;
    [Export] public bool dirty = true;
    [Export] public bool dirtyManpower = true;
    [Export] public Godot.Collections.Array enemies = [];
    [Export] public Godot.Collections.Array<string> figures = [];
    [Export]
    public Godot.Collections.Dictionary<string, Godot.Collections.Array<string>> governmentPositions = new() {
        { "Leader", new Godot.Collections.Array<string>() }
    };
    [Export] public Godot.Collections.Array<Variant> cachedGarrisonHubs = [];
    //endregion

    public CountryData()
    {
        aiController = new CountryAI();
        aiController.init(this);
    }

    //region --- Save / Load ---
    public Godot.Collections.Dictionary ToDict()
    {
        return new Godot.Collections.Dictionary
        {
            { "name", countryName },
            { "display_name", displayName },
            { "capital", capital },
            { "color", "#" + countryColor.ToHtml(false).ToUpperInvariant() },
            { "money", money },
            { "ideology", new Godot.Collections.Array { ideology.X, ideology.Y } },
            { "political_power", politicalPower },
            { "stability", baseStability },
            { "war_support", baseWarSupport },
            { "puppets", puppets },
            { "accepted_cultures", acceptedCultures },
            { "hostedGovernments", hostedGovernments },
            { "figures", figures },
            { "government_positions", governmentPositions },
            { "is_player", isPlayer },
            { "finished_decisions", finishedDecisions },
        };
    }

    public static CountryData FromDict(Godot.Collections.Dictionary aData)
    {
        var country = new CountryData();

        country.countryName = aData.ContainsKey("name") ? aData["name"].AsString() : "Sea";
        country.displayName = aData.ContainsKey("display_name")
            ? aData["display_name"].AsString()
            : country.countryName;
        country.capital = aData.ContainsKey("capital") ? aData["capital"].AsString() : "";
        country.countryColor = Color.FromHtml(aData.ContainsKey("color") ? aData["color"].AsString() : "#FFFFFF");
        country.money = aData.ContainsKey("money") ? aData["money"].AsDouble() : 0.0;

        if (aData.ContainsKey("ideology"))
        {
            var ideologyArray = aData["ideology"].AsGodotArray();
            country.ideology = new Vector2(ideologyArray[0].AsSingle(), ideologyArray[1].AsSingle());
        }
        else
        {
            country.ideology = new Vector2(
                SimRandom.Shared.RandRange(-100, 100),
                SimRandom.Shared.RandRange(-100, 100));
        }

        country.politicalPower = aData.ContainsKey("political_power") ? aData["political_power"].AsDouble() : 5000.0;
        country.baseStability = aData.ContainsKey("stability") ? aData["stability"].AsDouble() : 0.5;
        country.baseWarSupport = aData.ContainsKey("war_support") ? aData["war_support"].AsDouble() : 0.5;
        country.puppets = StringArray(aData, "puppets");
        country.acceptedCultures = VariantArray(aData, "accepted_cultures");
        country.hostedGovernments = VariantArray(aData,
            aData.ContainsKey("hostedGovernments") ? "hostedGovernments" : "hosted_governments");
        country.allowedCountries.Add(country.countryName);
        country.allowedCountries.Add("Sea");
        country.figures = StringArray(aData, "figures");

        if (aData.ContainsKey("government_positions"))
        {
            var positions = aData["government_positions"].AsGodotDictionary();
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
                country.governmentPositions[position] = names;
            }
        }

        country.isPlayer = aData.ContainsKey("is_player") && aData["is_player"].AsBool();
        if (aData.ContainsKey("finished_decisions"))
        {
            foreach (var idVariant in aData["finished_decisions"].AsGodotArray())
            {
                var id = idVariant.AsString();
                country.finishedDecisions.Add(id);
                country.SetMeta("finished_" + id, true);
            }
        }

        country.stability = country.baseStability;
        country.warSupport = country.baseWarSupport;
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
        foreach (var position in governmentPositions.Keys)
        {
            if (!governmentPositions.TryGetValue(position, out var names))
                continue;

            foreach (var figureName in names)
            {
                var figure = GetFigure(figureName);
                if (figure != null)
                    ApplySkills(figure.skills, true);
            }
        }
    }

    public void RemoveGovernmentPositionFigure(string aPosition, string aFigure)
    {
        var figure = GetFigure(aFigure);
        if (figure == null)
            return;

        ApplySkills(figure.skills, false);
        figure.occupation = "";
        if (governmentPositions.ContainsKey(aPosition))
            governmentPositions[aPosition].Remove(figure.name);
    }

    public void RemoveGovernmentPositions(string aPosition)
    {
        if (!governmentPositions.ContainsKey(aPosition))
            return;

        var names = new List<string>(governmentPositions[aPosition]);
        foreach (var figureName in names)
            RemoveGovernmentPositionFigure(aPosition, figureName);
    }

    public void SetGovernmentPosition(string aPosition, ImportantFigure aFigure)
    {
        RemoveGovernmentPositionFigure(aFigure.occupation, aFigure.name);
        if (!governmentPositions.ContainsKey(aPosition))
            governmentPositions[aPosition] = new Godot.Collections.Array<string>();
        governmentPositions[aPosition].Add(aFigure.name);
        ApplySkills(aFigure.skills, true);
        aFigure.occupation = aPosition;
    }

    private static ImportantFigure GetFigure(string figureName)
    {
        var figuresDict = MapManager.Instance.significantFigures;
        if (!figuresDict.ContainsKey(figureName))
            return null;
        return figuresDict[figureName] as ImportantFigure;
    }

    private void ApplySkills(Dictionary<string, object?> skills, bool add)
    {
        foreach (var key in skills.Keys)
        {
            var skill = key;
            var value = skills[key].Double();
            if (value == 0.0)
                continue;

            switch (skill)
            {
                case "stability":
                    stability += add ? value : -value;
                    break;
                case "warSupport":
                    warSupport += add ? value : -value;
                    break;
                case "dailyPPGain":
                    dailyPpGain += add ? value : -value;
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
                    attackerAttackMitigation = add ? attackerAttackMitigation * value : attackerAttackMitigation / value;
                    break;
                case "defenderAttackMitigation":
                    defenderAttackMitigation = add ? defenderAttackMitigation * value : defenderAttackMitigation / value;
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
public void ProcessHour()
    {
        if (_is_loading)
            return;

        politicalPower += dailyPpGain;

        hourlyMoneyIncome = ((gdp * 0.0000228310502) + (factoriesAmount * factoryIncome)) * (1.0 - economyLawPenalty);
        armyCost = CalculateArmyUpkeep();
        income = hourlyMoneyIncome - armyCost;
        money += income;
        troopSpeedModifier = 1.0 + (armyLevel * 0.1) + troopSpeedExtra;

        UpdateManpowerPool();
        _ProcessReinforcements();

        if (dirtyManpower && !dirty)
            UpdateManpowerPool();

        if (warDirty)
            UpdateIsAtWar();

        if (!isPlayer)
            aiController.ThinkHour();
    }

    public void ProcessDay()
    {
        if (_is_loading)
            return;

        var moveAmount = Vec2.Zero;
        foreach (var ideaTarget in driftTargets.Values)
            moveAmount += (ideology.DirectionTo(ideaTarget.finalPosition.ToGodot()) * ideaTarget.driftAmount).ToCore();
        ideology += moveAmount.ToGodot();

        _RefreshEconomicStats();
        _ProcessTraining();
        ((DecisionManager)DecisionManager.Instance).ProcessCountryDay(this);

        if (!isPlayer)
            aiController.ThinkDay();
    }

    private void _RefreshEconomicStats()
    {
        if (!dirty)
            return;

        totalPopulation = CountryManager.Instance.GetCountryPopulation(countryName);
        factoriesAmount = CountryManager.Instance.GetFactoriesAmount(countryName);
        gdp = (int)(CountryManager.Instance.GetCountryGdp(countryName) * totalPopulation * 0.000001);

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
            ideologyName = "neutral";
            return;
        }
        ideologyName = manager.GetIdeologyName(ideology);
    }
//endregion

    //region --- Military Management ---
    public bool TrainTroops(int count, string type = "infantry")
    {
        if (!DivisionData.TEMPLATES.ContainsKey(type))
        {
            GD.PushError($"Unknown division type: {type}");
            return false;
        }

        var template = DivisionData.TEMPLATES[type];
        var totalManpowerNeeded = count * template.Get("manpower").Int();

        if (manpower < totalManpowerNeeded || money < count * template.Get("cost").Double() * divCostMod)
            return false;

        manpower -= totalManpowerNeeded;
        mobilized += totalManpowerNeeded;

        ongoingTraining.Add(TroopTraining.FromValues(
            count,
            type,
            template.Get("days").Int(),
            template.Get("cost").Double()));
        return true;
    }

    private void _ProcessTraining()
    {
        for (var i = ongoingTraining.Count - 1; i >= 0; i--)
        {
            var training = ongoingTraining[i];
            var batchDailyCost = training.divisionsCount * training.dailyCost * divCostMod;

            if (money >= batchDailyCost)
            {
                money -= batchDailyCost;
                training.daysLeft -= 1;
            }

            if (training.daysLeft <= 0)
            {
                _GraduateTroops(training);
                ongoingTraining.RemoveAt(i);
            }
        }
    }

    private void _GraduateTroops(TroopTraining training)
    {
        readyTroops.Add(ReadyTroop.FromValues(
            DivisionData.CreateDivision(training.divisionType),
            training.divisionsCount));
        EmitSignal(SignalName.TrainingFinished);
    }

    public void UpdateManpowerPool()
    {
        var maxAllowedManpower = (int)(totalPopulation * militarySizeRatio);
        var totalMobilized = manpower + mobilized;

        if (totalMobilized < maxAllowedManpower)
        {
            var dailyGrowth = Math.Max(1, (int)(totalPopulation * 0.0001));
            manpower += dailyGrowth;

            if (totalMobilized > maxAllowedManpower)
                manpower = Math.Max(0, maxAllowedManpower - mobilized);
        }

        manpower = Math.Max(0, manpower);
        dirtyManpower = false;
    }

    public double CalculateArmyUpkeep()
    {
        var activeDivisions = 0;
        foreach (var troop in GetTroops(countryName))
            activeDivisions += troop.divisionsCount;

        var reserveDivisions = 0;
        foreach (var readyTroop in readyTroops)
            reserveDivisions += readyTroop.count;

        return (activeDivisions + (reserveDivisions * 0.25)) * armyLevel * BASE_ARMY_COST * divCostMod;
    }
private IReadOnlyList<TroopData> GetTroops(string country)
        => TroopManager.Instance.troopsByCountry.TryGetValue(country, out var troops)
            ? troops
            : Array.Empty<TroopData>();

    private void _SetupStartingArmy()
    {
        var projectedIncome = ((gdp * 0.0000228310502) + factoriesAmount * factoryIncome) * 0.25;
        var upkeep = Math.Max(1.0, armyLevel * BASE_ARMY_COST);
        var finalCount = Math.Clamp((int)(projectedIncome / upkeep), 1, 6);

        var template = DivisionData.TEMPLATES["infantry"];
        var manpowerPerDivision = template.Get("manpower").Int();

        if (manpower < finalCount * manpowerPerDivision)
            finalCount = (int)(manpower / Math.Max(1, manpowerPerDivision));

        if (finalCount <= 0)
            return;

        var startingDivisions = new List<DivisionData>();
        for (var i = 0; i < finalCount; i++)
            startingDivisions.Add(DivisionData.CreateDivision("infantry"));

        manpower -= finalCount * manpowerPerDivision;
        _DeployInitialForce(startingDivisions);
    }

    private void _DeployInitialForce(List<DivisionData> divisions)
    {
        var provincesDict = MapManager.Instance.countryToProvinces;
        if (!provincesDict.TryGetValue(countryName, out var provinces))
            return;

        if (provinces.Count == 0)
            return;

        var cities = MapManager.Instance.GetCitiesProvinceCountry(countryName);

        var currentBatch = new List<DivisionData>();
        for (var i = 0; i < divisions.Count; i++)
        {
            currentBatch.Add(divisions[i]);
            if (currentBatch.Count < 5 && i != divisions.Count - 1)
                continue;

            var targetPid = cities.Count > 0
                ? cities[SimRandom.Shared.RandRange(0, cities.Count - 1)].AsInt32()
                : provinces[SimRandom.Shared.RandRange(0, provinces.Count - 1)];
            TroopManager.Instance.DeploySpecificDivisions(countryName, currentBatch, targetPid);
            currentBatch = new List<DivisionData>();
        }
    }

    private void _ProcessReinforcements()
    {
        foreach (var troop in GetTroops(countryName))
        {
            if (troop.isMoving)
                continue;

            foreach (var division in troop.storedDivisions)
            {
                if (division.hp >= division.maxHp || division.maxHp <= 0)
                    continue;

                var percentage = Math.Min(0.05, (division.maxHp - division.hp) / division.maxHp);
                var template = DivisionData.TEMPLATES[division.type];
                var menNeeded = (int)(template.Get("manpower").Int() * percentage);
                var moneyNeeded = (int)(template.Get("cost").Double() * percentage * divCostMod);

                if (manpower < menNeeded || money < moneyNeeded)
                    continue;

                money -= moneyNeeded;
                manpower -= menNeeded;
                mobilized += menNeeded;
                division.hp += (float)(percentage * division.maxHp);
            }
        }
    }

    public bool DeployReadyTroop(ReadyTroop troop, int specificPid = -1)
    {
        var index = readyTroops.IndexOf(troop);
        if (index == -1)
            return false;

        var targetPid = specificPid;
        if (targetPid == -1)
        {
            var provincesDict = MapManager.Instance.countryToProvinces;
            if (!provincesDict.TryGetValue(countryName, out var provinces))
                return false;

            if (provinces.Count == 0)
                return false;
            targetPid = provinces[SimRandom.Shared.RandRange(0, provinces.Count - 1)];
        }

        TroopManager.Instance.DeployReady(countryName, troop, targetPid);
        readyTroops.RemoveAt(index);
        return true;
    }
    //endregion

    //region --- Stats & Manpower ---
    public double GetArmyPressure()
    {
        var armySize = 0;
        foreach (var troop in GetTroops(countryName))
            armySize += troop.divisionsCount;

        var capacity = Math.Max(1.0, (gdp * 0.03) + factoriesAmount * 5);
        return armySize / capacity;
    }
public double GetMaxMorale()
    {
        return (60.0 + (stability * 40.0) + (armyLevel * 5.0)) * (money < 0 ? 0.5 : 1.0);
    }
public double GetAttackEfficiency()
    {
        return (troopAttackAdd + 0.9 + (warSupport * 0.3) + (armyLevel * 0.05)) * (money < 0 ? 0.7 : 1.0) * troopAttackMod;
    }
public double GetDefenseEfficiency()
    {
        return (troopDefendAdd + 1.0 + (stability * 0.15) + (armyLevel * 0.05)) * (money < 0 ? 0.8 : 1.0) * troopDefendMod;
    }
//endregion

    //region --- Relations & War ---
    public void SetRelationWith(string otherCountryName, int value)
    {
        relations[otherCountryName] = Math.Clamp(value, 0, 100);
    }

    public void UpdateRelations()
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

    public int GetRelationWith(string otherCountryName)
    {
        var countries = CountryManager.Instance.countries;
        if (!countries.ContainsKey(otherCountryName))
        {
            relations.Remove(otherCountryName);
            return 0;
        }

        return relations.ContainsKey(otherCountryName) ? relations[otherCountryName] : 50;
    }

    public void UpdateIsAtWar()
    {
        enemies = WarManager.Instance.GetEnemiesOf(countryName);
        IsAtWar = enemies.Count > 0;
        warDirty = false;
    }
    //endregion
}
