using Godot;
using Sosr;

namespace Sosr;

public partial class PlansManager : Node
{
	public static PlansManager Instance => Autoload.Get<PlansManager>("PlansManager");

	public Godot.Collections.Dictionary plans = new Godot.Collections.Dictionary();
	public string currentStartFolder = "";

	public override void _Ready()
	{
		LoadPlansFromPath("res://plans.json");
	}

	public void LoadPlansFromPath(string path)
	{
		currentStartFolder = path.GetBaseDir() + "/";
		plans.Clear();
		var data = MapData.Instance.LoadJSON(path);
		if (data.VariantType == Variant.Type.Dictionary)
			plans = data.AsGodotDictionary();
	}

	public bool HasAvailablePlans(GodotObject country)
	{
		var countryName = country.Get("countryName").AsString();
		if (!plans.ContainsKey(countryName))
			return false;

		var choices = new Godot.Collections.Array();
		foreach (var element in plans[countryName].AsGodotArray())
		{
			if (element.VariantType == Variant.Type.Nil)
				continue;

			var elementDict = element.AsGodotDictionary();
			if (GetOrDefault(elementDict, "type", "").AsString() == "button")
			{
				var condition = GetOrDefault(elementDict, "condition", new Godot.Collections.Dictionary());
				if (InterpreterManager.Instance.GetFunction(condition, country).AsBool())
				{
					if (country.Get("isPlayer").AsBool())
						return true;

					choices.Add(GetOrDefault(elementDict, "finished", new Godot.Collections.Dictionary()));
				}
			}
		}

		if (choices.Count > 0)
		{
			var choice = choices[(int)(SimRandom.Shared.NextUInt() % (uint)choices.Count)];
			if (choice.VariantType != Variant.Type.Nil && choice.AsBool())
				InterpreterManager.Instance.GetFunction(choice, country);
		}
		return false;
	}

	private static Variant GetOrDefault(Godot.Collections.Dictionary dict, Variant key, Variant fallback)
	{
		return dict.TryGetValue(key, out var value) ? value : fallback;
	}
}
