using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

// Sole owner of significant figures. Generation and portrait lookup stay in
// ImportantFigureHost; this manager owns the dictionary and its persistence.
public partial class FigureManager : Node
{
	public static FigureManager Instance => GameState.Instance.figureManager;

	public Dictionary<string, ImportantFigure> figures = new();

	public void Clear() => figures.Clear();

	public ImportantFigure Get(string name) =>
		name != null && figures.TryGetValue(name, out var figure) ? figure : null;

	public ImportantFigure Generate(string country, float budget = 0.0f)
	{
		var figure = ImportantFigureHost.FromRandom(country, budget);
		figures[figure.name] = figure;
		return figure;
	}

	public Godot.Collections.Array Serialize()
	{
		var result = new Godot.Collections.Array();
		foreach (var figure in figures.Values)
			result.Add(VariantState.ToVariant(figure.ToDict()));
		return result;
	}

	public void Load(Godot.Collections.Array data)
	{
		figures.Clear();
		foreach (var variant in data)
		{
			var dict = variant.AsGodotDictionary();
			var figure = ImportantFigure.FromDict(dict.AsState());
			figures[figure.name] = figure;
		}
	}
}
