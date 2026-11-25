using System;
using Godot;
using Sosr;

namespace Sosr;

public partial class IdeologyManager : Node
{
	public static IdeologyManager Instance => Autoload.Get<IdeologyManager>("IdeologyManager");

	public Godot.Collections.Dictionary ideologies = new Godot.Collections.Dictionary();
	public Godot.Collections.Array ideology_names = new Godot.Collections.Array();

	public void Initialize(Godot.Collections.Dictionary a_ideologyData)
	{
		ideologies = a_ideologyData;
		ideology_names = new Godot.Collections.Array();
		foreach (var key in ideologies.Keys)
			ideology_names.Add(key);

		// GDScript sort_custom uses a stable insertion sort with a strict '<' comparison.
		for (var i = 1; i < ideology_names.Count; i++)
		{
			var key = ideology_names[i];
			var keyArea = _area(ideologies[key].AsGodotDictionary()["region"]);
			var j = i - 1;
			while (j >= 0 && _area(ideologies[ideology_names[j]].AsGodotDictionary()["region"]) > keyArea)
			{
				ideology_names[j + 1] = ideology_names[j];
				j--;
			}
			ideology_names[j + 1] = key;
		}
	}

	public string get_ideology_name(Vector2 coords)
	{
		foreach (var ideology in ideology_names)
		{
			var region = ideologies[ideology].AsGodotDictionary()["region"].AsGodotArray();
			var lowerBound = region[0].AsGodotArray();
			var upperBound = region[1].AsGodotArray();
			var inX = lowerBound[0].AsDouble() <= coords.X && coords.X <= upperBound[0].AsDouble();
			var inY = lowerBound[1].AsDouble() <= coords.Y && coords.Y <= upperBound[1].AsDouble();
			if (inX && inY)
				return ideology.AsString();
		}
		return "neutral";
	}

	public double _area(Variant a)
	{
		var region = a.AsGodotArray();
		var p0 = region[0].AsGodotArray();
		var p1 = region[1].AsGodotArray();
		return Math.Abs((p0[0].AsDouble() - p1[0].AsDouble()) * (p0[1].AsDouble() - p1[1].AsDouble()));
	}
}
