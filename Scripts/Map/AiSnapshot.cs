using System;
using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

// Plain-data view of the world. Built on the main thread, read-only on workers.
public sealed class AiCountrySnapshot
{
    public string name = "";
    public double money;
    public int manpower;
    public int mobilized;
    public int factories;
    public bool isPuppet;
    public string owner = "";
    public string[] factions = Array.Empty<string>();
    public string[] puppets = Array.Empty<string>();
    public Dictionary<string, int> relations = new();
    public HashSet<string> enemies = new();
    public double extremism;
}

public sealed class AiSnapshot
{
    public AiCountrySnapshot[] countries = Array.Empty<AiCountrySnapshot>();
    public Dictionary<string, int> index = new();
    public Dictionary<string, string[]> neighborCountries = new();
    public Dictionary<string, int[]> citiesByCountry = new();
    public Dictionary<string, int[]> provincesByCountry = new();
    public Dictionary<int, Province> provinces = new();
    public Dictionary<string, HashSet<string>> factionMembers = new();
    public HashSet<int> constructionQueue = new();
    public HashSet<(string, string)> wars = new();
    public float worldTension;
    public int frame;
    public ulong seedBase;

    public static AiSnapshot Capture()
    {
        var snap = new AiSnapshot
        {
            provinces = MapManager.Instance.provinceObjects,
            worldTension = MapManager.Instance.worldTension,
            frame = Engine.GetFramesDrawn(),
            seedBase = SimRandom.Shared.State,
        };

        foreach (var key in BuildingManager.Instance.constructionQueue.Keys)
            snap.constructionQueue.Add(key.AsInt32());

        foreach (var pair in WarManager.Instance.wars)
            foreach (var enemy in pair.Value)
            {
                snap.wars.Add((pair.Key, enemy));
                snap.wars.Add((enemy, pair.Key));
            }

        foreach (var pair in FactionManager.Instance.factions)
        {
            var members = new HashSet<string>();
            foreach (var member in pair.Value.members)
                members.Add(member.polity);
            snap.factionMembers[pair.Key] = members;
        }

        var names = CountryManager.Instance.countryNames;
        snap.countries = new AiCountrySnapshot[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var country = CountryManager.Instance.GetCountry(name);
            var entry = new AiCountrySnapshot { name = name };
            if (country != null)
            {
                entry.money = country.money;
                entry.manpower = country.manpower;
                entry.mobilized = country.mobilized;
                entry.factories = country.factoriesAmount;
                entry.isPuppet = country.isPuppet;
                entry.owner = country.owner;
                var factions = new List<string>();
                foreach (var faction in country.factions)
                    factions.Add(faction);
                var puppets = new List<string>();
                foreach (var puppet in country.puppets)
                    puppets.Add(puppet);
                entry.factions = factions.ToArray();
                entry.puppets = puppets.ToArray();
                foreach (var key in country.relations.Keys)
                    entry.relations[key] = country.relations[key];
                entry.extremism = Math.Clamp((double)country.ideology.Length() * 0.00707113562, 0.0, 1.0);
            }
            entry.enemies = new HashSet<string>();
            foreach (var enemy in WarManager.Instance.GetEnemiesOf(name))
                entry.enemies.Add(enemy.AsString());
            snap.index[name] = i;
            snap.countries[i] = entry;
        }

        foreach (var pair in MapManager.Instance.countryToCities)
            snap.citiesByCountry[pair.Key] = pair.Value.ToArray();

        foreach (var pair in MapManager.Instance.countryToProvinces)
        {
            snap.provincesByCountry[pair.Key] = pair.Value.ToArray();
            var neighbors = new HashSet<string>();
            foreach (var pid in pair.Value)
                foreach (var nid in MapManager.Instance.provinceGraph.GetPointConnections(pid))
                {
                    if (!snap.provinces.TryGetValue((int)nid, out var province) || province == null)
                        continue;
                    var owner = province.GetFunctionalOwner();
                    if (owner != "" && owner != "Sea" && owner != pair.Key)
                        neighbors.Add(owner);
                }
            snap.neighborCountries[pair.Key] = new List<string>(neighbors).ToArray();
        }

        return snap;
    }
}
