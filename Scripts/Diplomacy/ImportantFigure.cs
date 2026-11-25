using System.Collections.Generic;
using System.Globalization;
using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class ImportantFigure : RefCounted
{
	public enum Status
	{
		ALIVE,
		WOUNDED,
		DEAD,
	}

	public string name = "";
	public Godot.Collections.Dictionary skills = new Godot.Collections.Dictionary();
	public Godot.Collections.Array traits = new Godot.Collections.Array();
	public Vector2I ideology = new Vector2I(0, 0);
	public string allegiance = "";
	public string occupation = "";
	public int legitimacy = 0;
	public string portrait_path = "";
	public Status status = Status.ALIVE;

	public static Godot.Collections.Dictionary<string, Godot.Collections.Dictionary> NOBLE_SKILLS { get; } = new()
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

	public static ImportantFigure FromRandom(string aAllegiance, float aBudget = 0.0f)
	{
		var figure = new ImportantFigure();
		double budget = aBudget;

		figure.name = "";
		for (var i = 0; i < GD.RandRange(2, 3); i++)
			figure.name += (NameGenerator.GenerateName(GD.Randi() % 2 == 0, GD.RandRange(3, 10))).Capitalize();
		figure.skills = new Godot.Collections.Dictionary(); // TODO
		var skillsToPick = GD.RandRange(1, 5);
		var pickableSkills = new Godot.Collections.Array<string>();
		foreach (var key in NOBLE_SKILLS.Keys)
			pickableSkills.Add(key);
		for (var i = 0; i < skillsToPick; i++)
		{
			var skill = PickRandom(pickableSkills);
			var skillData = NOBLE_SKILLS[skill];
			var level = Mathf.Max(GD.RandRange(1, 5) - GD.RandRange(0, 2), 1);
			var cost = skillData["val"].AsDouble() * level;
			var result = skillData["base"].AsDouble();
			switch (skillData["dir"].AsString())
			{
				case "more":
					result += skillData["step"].AsDouble() * level;
					break;
				case "less":
					result -= skillData["step"].AsDouble() * level;
					break;
			}
			budget -= cost;
			pickableSkills.Remove(skill);
			figure.skills[skill] = result;
		}

		while (budget < 0 && pickableSkills.Count > 0)
		{
			var skill = PickRandom(pickableSkills);
			var skillData = NOBLE_SKILLS[skill];
			var level = Mathf.Max(GD.RandRange(1, 5) - GD.RandRange(0, 2), 1);
			var cost = skillData["val"].AsDouble() * level;
			var result = skillData["base"].AsDouble();
			switch (skillData["dir"].AsString())
			{
				case "more":
					result -= skillData["step"].AsDouble() * level;
					break;
				case "less":
					result += skillData["step"].AsDouble() * level;
					break;
			}
			budget += cost;
			pickableSkills.Remove(skill);
			figure.skills[skill] = result;
		}

		figure.traits = new Godot.Collections.Array(); // TODO
		figure.ideology = new Vector2I(0, 0);
		figure.allegiance = aAllegiance;
		figure.occupation = "";
		figure.portrait_path = "";
		var randomPortraitsDir = "res://starts/" + Sosr.GameState.Instance.current_start + "/assets/portraits/random/";
		var dir = DirAccess.Open(randomPortraitsDir);
		if (dir != null)
		{
			dir.ListDirBegin();
			var fileName = dir.GetNext();
			var portraits = new Godot.Collections.Array<string>();
			while (fileName != "")
			{
				if (!dir.CurrentIsDir() && fileName.EndsWith(".png"))
					portraits.Add(fileName);
				fileName = dir.GetNext();
			}

			if (portraits.Count > 0)
				figure.portrait_path = randomPortraitsDir + PickRandom(portraits);
		}
		figure.status = Status.ALIVE;

		return figure;
	}

	public static ImportantFigure FromValues(
		string aName,
		Godot.Collections.Dictionary aSkills,
		Godot.Collections.Array aTraits,
		string aAllegiance,
		string aOccupation,
		int aLegitimacy,
		string aPortraitPath,
		Status aStatus)
	{
		var figure = new ImportantFigure();

		figure.name = aName;
		figure.skills = aSkills;
		figure.traits = aTraits;
		figure.allegiance = aAllegiance;
		figure.occupation = aOccupation;
		figure.legitimacy = aLegitimacy;
		figure.portrait_path = aPortraitPath;
		figure.status = aStatus;

		return figure;
	}

	public static ImportantFigure FromDict(Godot.Collections.Dictionary aData)
	{
		var figure = new ImportantFigure();

		figure.name = aData["name"].AsString();
		figure.skills = aData["skills"].AsGodotDictionary();
		figure.traits = aData["traits"].AsGodotArray();
		var ideology = aData["ideology"].AsGodotArray();
		figure.ideology = new Vector2I(ideology[0].AsInt32(), ideology[1].AsInt32());
		figure.allegiance = aData["allegiance"].AsString();
		figure.occupation = aData["occupation"].AsString();
		figure.legitimacy = aData.ContainsKey("legitimacy") ? aData["legitimacy"].AsInt32() : 100;
		figure.portrait_path = aData.ContainsKey("portrait_path") ? aData["portrait_path"].AsString() : "";
		figure.status = aData.ContainsKey("status") ? (Status)aData["status"].AsInt32() : Status.ALIVE;

		return figure;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "skills", skills },
			{ "traits", traits },
			{ "ideology", new Godot.Collections.Array { ideology.X, ideology.Y } },
			{ "allegiance", allegiance },
			{ "occupation", occupation },
			{ "legitimacy", legitimacy },
			{ "portrait_path", portrait_path },
			{ "status", (int)status },
		};
	}

	public static Texture GetPortrait(ImportantFigure aFigure)
	{
		if (aFigure == null)
			return ResourceLoader.Load<Texture>("res://assets/portraits/Fallback.png");

		if (aFigure.portrait_path != "" && (FileAccess.FileExists(aFigure.portrait_path) || ResourceLoader.Exists(aFigure.portrait_path)))
			return ResourceLoader.Load<Texture>(aFigure.portrait_path);

		var start = Sosr.GameState.Instance.current_start;
		var path = "res://starts/" + start + "/assets/portraits/" + aFigure.allegiance + "/" + aFigure.name + ".png";
		if (FileAccess.FileExists(path) || ResourceLoader.Exists(path))
		{
			aFigure.portrait_path = path;
			return ResourceLoader.Load<Texture>(path);
		}

		// Fallback to random folder if it's there but not in portrait_path for some reason
		path = "res://starts/" + start + "/assets/portraits/random/" + aFigure.name + ".png";
		if (FileAccess.FileExists(path) || ResourceLoader.Exists(path))
		{
			aFigure.portrait_path = path;
			return ResourceLoader.Load<Texture>(path);
		}

		aFigure.portrait_path = "res://assets/portraits/Fallback.png";
		return ResourceLoader.Load<Texture>("res://assets/portraits/Fallback.png");
	}

	public string GetDisplayString()
	{
		var toReturn = name.Capitalize() + "\nSkills:";
		foreach (var skill in SkillKeys(skills))
		{
			toReturn += "\n";
			toReturn += FormatSkill(skill, NOBLE_SKILLS[skill], skills[skill].AsDouble());
		}
		return toReturn;
	}

	public string GetDisplaySkills()
	{
		var toReturn = "";
		foreach (var skill in SkillKeys(skills))
		{
			toReturn += "\n";
			toReturn += FormatSkill(skill, NOBLE_SKILLS[skill], skills[skill].AsDouble());
		}
		return toReturn;
	}

	private static Godot.Collections.Dictionary Skill(string type, double min, double baseValue, double max, double step, string dir, double val, int precision)
	{
		return new Godot.Collections.Dictionary
		{
			{ "type", type },
			{ "min", min },
			{ "base", baseValue },
			{ "max", max },
			{ "step", step },
			{ "dir", dir },
			{ "val", val },
			{ "precision", precision },
		};
	}

	private static T PickRandom<[MustBeVariant] T>(Godot.Collections.Array<T> aItems)
	{
		return aItems[(int)(GD.Randi() % (uint)aItems.Count)];
	}

	private static IEnumerable<string> SkillKeys(Godot.Collections.Dictionary aSkills)
	{
		foreach (var key in aSkills.Keys)
			yield return key is Variant variant ? variant.AsString() : key.ToString();
	}

	private static string FormatSkill(string skill, Godot.Collections.Dictionary skillData, double value)
	{
		var precision = skillData["precision"].AsInt32();
		var formatted = "";
		switch (skillData["type"].AsString())
		{
			case "add":
				formatted = Signed(value, Mathf.Min(precision, Mathf.StepDecimals(value))) + " ";
				break;
			case "mul":
				var percent = (value - skillData["base"].AsDouble()) * 100f;
				formatted = Signed(percent, Mathf.Min(precision, Mathf.StepDecimals(percent))) + "% ";
				break;
		}
		return formatted + skill.Capitalize();
	}

	// GDScript "%+.*f".
	private static string Signed(double value, int precision)
	{
		var fraction = precision > 0 ? "." + new string('0', precision) : "";
		return value.ToString("+0" + fraction + ";-0" + fraction + ";+0" + fraction, CultureInfo.InvariantCulture);
	}
}
