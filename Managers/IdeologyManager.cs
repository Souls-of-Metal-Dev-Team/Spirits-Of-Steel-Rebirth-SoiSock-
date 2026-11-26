using System;
using Godot;
using Sosr;

namespace Sosr;

public partial class IdeologyManager : Node
{
	public static IdeologyManager Instance => Autoload.Get<IdeologyManager>("IdeologyManager");

	public Godot.Collections.Dictionary ideologies = new Godot.Collections.Dictionary();
	public Godot.Collections.Array ideologyNames = new Godot.Collections.Array();

	public void Initialize(Godot.Collections.Dictionary aIdeologyData)
	{
		ideologies = aIdeologyData;
		ideologyNames = new Godot.Collections.Array();
		foreach (var key in ideologies.Keys)
			ideologyNames.Add(key);

		// GDScript sort_custom uses a stable insertion sort with a strict '<' comparison.
		for (var i = 1; i < ideologyNames.Count; i++)
		{
			var key = ideologyNames[i];
			var keyArea = _area(ideologies[key].AsGodotDictionary()["region"]);
			var j = i - 1;
			while (j >= 0 && _area(ideologies[ideologyNames[j]].AsGodotDictionary()["region"]) > keyArea)
			{
				ideologyNames[j + 1] = ideologyNames[j];
				j--;
			}
			ideologyNames[j + 1] = key;
		}
	}

	public string GetIdeologyName(Vector2 coords)
	{
		foreach (var ideology in ideologyNames)
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
