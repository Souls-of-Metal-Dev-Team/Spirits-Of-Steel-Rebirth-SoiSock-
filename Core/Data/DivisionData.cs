using System.Collections.Generic;

namespace Sosr;

public sealed class DivisionData
{
	public static Dictionary<string, Dictionary<string, object?>> TEMPLATES { get; } = new()
	{
		{ "infantry", new Dictionary<string, object?>
			{
				{ "hp", 100.0 }, { "manpower", 10000.0 }, { "cost", 500.0 },
				{ "days", 9.0 }, { "attack", 1.0 }, { "defense", 1.0 }, { "speed", 1.0 },
			}
		},
		{ "tank", new Dictionary<string, object?>
			{
				{ "hp", 280.0 }, { "manpower", 20000.0 }, { "cost", 10000.0 },
				{ "days", 30.0 }, { "attack", 5.0 }, { "defense", 7.0 }, { "speed", 2.5 },
			}
		},
		{ "artillery", new Dictionary<string, object?>
			{
				{ "hp", 50.0 }, { "manpower", 1000.0 }, { "cost", 10000.0 },
				{ "days", 15.0 }, { "attack", 5.0 }, { "defense", 0.3 }, { "speed", 0.8 },
			}
		},
	};

	public string name = "Infantry Division";
	public string type = "infantry";
	public float hp = 100.0f;
	public float maxHp = 100.0f;
	public float experience;
	public int maxManpower = 10000;
	public int manpowerPerHP = 100;

	public DivisionData()
	{
		manpowerPerHP = (int)(maxManpower / maxHp);
	}

	public static DivisionData FromDict(Dictionary<string, object?> aDict)
	{
		var division = new DivisionData
		{
			name = aDict.Get("name").Str(),
			type = aDict.ContainsKey("type") ? aDict["type"].Str("infantry") : "infantry",
			hp = aDict.ContainsKey("hp") ? aDict["hp"].Single() : 100f,
			maxHp = aDict.ContainsKey("max_hp") ? aDict["max_hp"].Single() : 100f,
			experience = aDict.Get("experience").Single(),
			maxManpower = aDict.ContainsKey("max_manpower") ? aDict["max_manpower"].Int() : 10000,
		};
		division.manpowerPerHP = (int)(division.maxManpower / division.maxHp);
		return division;
	}

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "name", name },
		{ "type", type },
		{ "hp", (double)hp },
		{ "max_hp", (double)maxHp },
		{ "experience", (double)experience },
		{ "max_manpower", (double)maxManpower },
	};

	private Dictionary<string, object?> GetStats(string pType) =>
		TEMPLATES.TryGetValue(pType, out var stats) ? stats : TEMPLATES["infantry"];

	public float GetAttackPower() => GetStats(type).Get("attack").Single() * (1.0f + experience * 0.5f);

	public float GetDefensePower() => GetStats(type).Get("defense").Single() * (1.0f + experience * 0.5f);

	public static DivisionData CreateDivision(string pType)
	{
		var stats = TEMPLATES.TryGetValue(pType, out var found) ? found : TEMPLATES["infantry"];
		var div = new DivisionData
		{
			type = pType,
			hp = stats.Get("hp").Single(),
			maxHp = stats.Get("hp").Single(),
			maxManpower = stats.Get("manpower").Int(),
			name = TextUtil.Capitalize(pType),
		};
		div.manpowerPerHP = (int)(div.maxManpower / div.maxHp);
		return div;
	}
}
