using System.Collections.Generic;

namespace Sosr;

// Save layout and start-state assembly. Engine-side MapData/World own only file I/O;
// every transformation of the state tree lives here and is testable headlessly.
public static class SaveService
{
	// Keys shared by a scenario's start state and a unified game-state save.
	public static readonly string[] StateKeys =
	{
		"clock", "resources", "biomes", "buildings", "provinces",
		"polities", "ideologies", "factions", "wars",
		"original_territories", "scheduled_events", "significant_figures",
	};

	public static Dictionary<string, object?> BuildStartState(
		Dictionary<string, object?> mapData,
		Dictionary<string, object?> provinces,
		Dictionary<string, object?> scenarioData)
	{
		var combined = new Dictionary<string, object?>(mapData) { ["provinces"] = provinces };
		foreach (var (key, value) in scenarioData)
			combined[key] = value;

		var state = new Dictionary<string, object?>();
		foreach (var key in StateKeys)
			if (combined.TryGetValue(key, out var value))
				state[key] = value;
		return state;
	}

	// Legacy full saves have no diff. Diff saves are merged with their start.
	public static Dictionary<string, object?> ApplySave(
		Dictionary<string, object?> start,
		Dictionary<string, object?> save)
	{
		if (!save.TryGetValue("diff", out var diff))
			return new Dictionary<string, object?>(save);

		var result = StateDiff.Merge(start, diff.Dict());
		result["scenario_path"] = save.Get("scenario_path").Str();
		return result;
	}

	public static Dictionary<string, object?> CreateSave(
		Dictionary<string, object?> start,
		Dictionary<string, object?> current,
		string scenarioPath)
	{
		// A JSON round-trip normalizes ints from live state to the floats parsed from starts.
		var baseline = (Dictionary<string, object?>)StateJson.Parse(StateJson.Format(start))!;
		var live = (Dictionary<string, object?>)StateJson.Parse(StateJson.Format(current))!;
		return new Dictionary<string, object?>
		{
			{ "scenario_path", scenarioPath },
			{ "diff", StateDiff.Diff(baseline, live) },
		};
	}
}
