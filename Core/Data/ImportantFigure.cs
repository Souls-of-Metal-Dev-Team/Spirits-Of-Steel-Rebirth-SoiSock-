using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sosr;

public sealed class ImportantFigure
{
	public enum Status
	{
		ALIVE,
		WOUNDED,
		DEAD,
	}

	public string name = "";
	public Dictionary<string, object?> skills = new();
	public List<object?> traits = new();
	public Vec2I ideology = Vec2I.Zero;
	public string allegiance = "";
	public string occupation = "";
	public int legitimacy;
	public string portraitPath = "";
	public Status status = Status.ALIVE;

	public static Dictionary<string, Dictionary<string, object?>> NOBLE_SKILLS { get; } = new()
	{
		{ "stability",                Skill("add", -1.0,  0.0,  1.0,  0.01 , "more", 1.0, 2) },
		{ "warSupport",               Skill("add", -1.0,  0.0,  1.0,  0.01 , "more", 1.0, 2) },
		{ "dailyPPGain",              Skill("add", -1.0,  0.0,  1.0,  0.01 , "more", 4.0, 2) },
		{ "divCostMod",               Skill("mul",  0.0,  1.0, 10.0,  0.01 , "less", 3.5, 0) },
		{ "troopSpeed",               Skill("add", -1.0,  0.0,  1.0,  0.005, "more", 7.0, 3) },
		{ "troopAttackAdd",           Skill("add", -5.0,  0.0,  5.0,  0.025, "more", 2.5, 3) },
		{ "troopAttackMod",           Skill("mul",  0.0,  1.0, 10.0,  0.01 , "more", 6.0, 0) },
		{ "troopDefendAdd",           Skill("add", -5.0,  0.0,  5.0,  0.025, "more", 2.5, 3) },
		{ "troopDefendMod",           Skill("mul",  0.0,  1.0, 10.0,  0.01 , "more", 6.0, 0) },
		{ "attackerAttackMitigation", Skill("mul",  0.0,  1.0,  5.0,  0.01 , "less", 8.0, 0) },
		{ "defenderAttackMitigation", Skill("mul",  0.0,  1.0,  5.0,  0.01 , "less", 8.0, 0) },
	};

	public static ImportantFigure FromValues(
		string aName,
		Dictionary<string, object?> aSkills,
		List<object?> aTraits,
		string aAllegiance,
		string aOccupation,
		int aLegitimacy,
		string aPortraitPath,
		Status aStatus) => new()
	{
		name = aName,
		skills = aSkills,
		traits = aTraits,
		allegiance = aAllegiance,
		occupation = aOccupation,
		legitimacy = aLegitimacy,
		portraitPath = aPortraitPath,
		status = aStatus,
	};

	public static ImportantFigure FromDict(Dictionary<string, object?> aData)
	{
		var ideology = aData.Get("ideology").List();
		return new ImportantFigure
		{
			name = aData.Get("name").Str(),
			skills = aData.Get("skills").Dict(),
			traits = aData.Get("traits").List(),
			ideology = new Vec2I(ideology[0].Int(), ideology[1].Int()),
			allegiance = aData.Get("allegiance").Str(),
			occupation = aData.Get("occupation").Str(),
			legitimacy = aData.ContainsKey("legitimacy") ? aData["legitimacy"].Int() : 100,
			portraitPath = aData.ContainsKey("portrait_path") ? aData["portrait_path"].Str() : "",
			status = aData.ContainsKey("status") ? (Status)aData["status"].Int() : Status.ALIVE,
		};
	}

	public Dictionary<string, object?> ToDict()
	{
		var traits = new List<object?>();
		foreach (var trait in this.traits)
			traits.Add(trait);
		return new Dictionary<string, object?>
		{
			{ "name", name },
			{ "skills", skills },
			{ "traits", traits },
			{ "ideology", new List<object?> { (double)ideology.X, (double)ideology.Y } },
			{ "allegiance", allegiance },
			{ "occupation", occupation },
			{ "legitimacy", (double)legitimacy },
			{ "portrait_path", portraitPath },
			{ "status", (double)(int)status },
		};
	}

	public string GetDisplayString()
	{
		var toReturn = TextUtil.Capitalize(name) + "\nSkills:";
		foreach (var skill in skills.Keys)
		{
			toReturn += "\n";
			toReturn += FormatSkill(skill, NOBLE_SKILLS[skill], skills[skill].Double());
		}
		return toReturn;
	}

	public string GetDisplaySkills()
	{
		var toReturn = "";
		foreach (var skill in skills.Keys)
		{
			toReturn += "\n";
			toReturn += FormatSkill(skill, NOBLE_SKILLS[skill], skills[skill].Double());
		}
		return toReturn;
	}

	private static Dictionary<string, object?> Skill(string type, double min, double baseValue, double max, double step, string dir, double val, int precision) => new()
	{
		{ "type", type },
		{ "min", min },
		{ "base", baseValue },
		{ "max", max },
		{ "step", step },
		{ "dir", dir },
		{ "val", val },
		{ "precision", (double)precision },
	};

	private static string FormatSkill(string skill, Dictionary<string, object?> skillData, double value)
	{
		var precision = skillData.Get("precision").Int();
		var formatted = "";
		switch (skillData.Get("type").Str())
		{
			case "add":
				formatted = Signed(value, Math.Min(precision, StepDecimals(value))) + " ";
				break;
			case "mul":
				var percent = (value - skillData.Get("base").Double()) * 100.0;
				formatted = Signed(percent, Math.Min(precision, StepDecimals(percent))) + "% ";
				break;
		}
		return formatted + TextUtil.Capitalize(skill);
	}

	private static int StepDecimals(double value)
	{
		for (var i = 0; i < 6; i++)
		{
			var scaled = value * Math.Pow(10, i);
			if (Math.Abs(scaled - Math.Round(scaled)) < 1e-6)
				return i;
		}
		return 6;
	}

	// GDScript "%+.*f".
	private static string Signed(double value, int precision)
	{
		var fraction = precision > 0 ? "." + new string('0', precision) : "";
		return value.ToString("+0" + fraction + ";-0" + fraction + ";+0" + fraction, CultureInfo.InvariantCulture);
	}
}
