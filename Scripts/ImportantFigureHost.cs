using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sosr;

// Engine-bound halves of ImportantFigure: portrait lookup and seeded generation.
public static class ImportantFigureHost
{
	public static ImportantFigure FromRandom(string aAllegiance, float aBudget = 0.0f)
	{
		double budget = aBudget;
		var name = "";
		for (var i = 0; i < SimRandom.Shared.RandRange(2, 3); i++)
			name += TextUtil.Capitalize(NameGenerator.GenerateName(
				SimRandom.Shared.NextUInt() % 2 == 0, SimRandom.Shared.RandRange(3, 10)));

		var skills = new Dictionary<string, object?>();
		var skillsToPick = SimRandom.Shared.RandRange(1, 5);
		var pickableSkills = ImportantFigure.NOBLE_SKILLS.Keys.ToList();
		for (var i = 0; i < skillsToPick && pickableSkills.Count > 0; i++)
		{
			var skill = PickRandom(pickableSkills);
			ApplySkill(skills, skill, invert: false, ref budget);
			pickableSkills.Remove(skill);
		}

		while (budget < 0 && pickableSkills.Count > 0)
		{
			var skill = PickRandom(pickableSkills);
			ApplySkill(skills, skill, invert: true, ref budget);
			pickableSkills.Remove(skill);
		}

		var figure = ImportantFigure.FromValues(
			name, skills, new List<object?>(), aAllegiance, "", 0, "", ImportantFigure.Status.ALIVE);
		var randomPortraitsDir = "res://starts/" + GameState.Instance.currentStart + "/assets/portraits/random/";
		var dir = DirAccess.Open(randomPortraitsDir);
		if (dir != null)
		{
			dir.ListDirBegin();
			var portraits = new List<string>();
			var fileName = dir.GetNext();
			while (fileName != "")
			{
				if (!dir.CurrentIsDir() && fileName.EndsWith(".png"))
					portraits.Add(fileName);
				fileName = dir.GetNext();
			}
			if (portraits.Count > 0)
				figure.portraitPath = randomPortraitsDir + PickRandom(portraits);
		}
		return figure;
	}

	private static void ApplySkill(Dictionary<string, object?> skills, string skill, bool invert, ref double budget)
	{
		var skillData = ImportantFigure.NOBLE_SKILLS[skill];
		var level = System.Math.Max(SimRandom.Shared.RandRange(1, 5) - SimRandom.Shared.RandRange(0, 2), 1);
		budget -= skillData.Get("val").Double() * level;
		var result = skillData.Get("base").Double();
		var sign = skillData.Get("dir").Str() == "more" ? 1.0 : -1.0;
		if (invert)
			sign = -sign;
		result += sign * skillData.Get("step").Double() * level;
		if (invert)
			budget += 2 * skillData.Get("val").Double() * level;
		skills[skill] = result;
	}

	private static T PickRandom<T>(List<T> items) =>
		items[(int)(SimRandom.Shared.NextUInt() % (uint)items.Count)];

	public static Texture GetPortrait(ImportantFigure? aFigure)
	{
		if (aFigure == null)
			return ResourceLoader.Load<Texture>("res://assets/portraits/Fallback.png");

		if (aFigure.portraitPath != "" && (FileAccess.FileExists(aFigure.portraitPath) || ResourceLoader.Exists(aFigure.portraitPath)))
			return ResourceLoader.Load<Texture>(aFigure.portraitPath);

		var start = GameState.Instance.currentStart;
		var path = "res://starts/" + start + "/assets/portraits/" + aFigure.allegiance + "/" + aFigure.name + ".png";
		if (FileAccess.FileExists(path) || ResourceLoader.Exists(path))
		{
			aFigure.portraitPath = path;
			return ResourceLoader.Load<Texture>(path);
		}

		path = "res://starts/" + start + "/assets/portraits/random/" + aFigure.name + ".png";
		if (FileAccess.FileExists(path) || ResourceLoader.Exists(path))
		{
			aFigure.portraitPath = path;
			return ResourceLoader.Load<Texture>(path);
		}

		aFigure.portraitPath = "res://assets/portraits/Fallback.png";
		return ResourceLoader.Load<Texture>("res://assets/portraits/Fallback.png");
	}
}
