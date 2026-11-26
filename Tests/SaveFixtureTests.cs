using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static Sosr.Tests.StateDiffTests;

namespace Sosr.Tests;

// Real start and save files from the repo. Ported from scratch/test_save_diff_flow.gd.
public class SaveFixtureTests
{
	private static readonly string Root = FindRoot();

	// Same keys as MapData.STATE_KEYS.
	private static readonly string[] StateKeys =
	{
		"clock", "resources", "biomes", "buildings", "provinces",
		"polities", "ideologies", "factions", "wars",
		"original_territories", "scheduled_events", "significant_figures",
	};

	private static string FindRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent;
		return dir?.FullName ?? throw new DirectoryNotFoundException("project.godot not found");
	}

	private static Dictionary<string, object?> LoadDict(string relativePath) =>
		(Dictionary<string, object?>)StateJson.Parse(File.ReadAllText(Path.Combine(Root, relativePath)))!;

	// Mirrors MapData.LoadStartState.
	private static Dictionary<string, object?> LoadStartState(string start)
	{
		var combined = new Dictionary<string, object?>(LoadDict($"starts/{start}/map_data.json"));
		combined["provinces"] = LoadDict($"starts/{start}/provinces.json");
		foreach (var (key, value) in LoadDict($"starts/{start}/scenario_data.json"))
			combined[key] = value;
		return StateKeys.Where(combined.ContainsKey).ToDictionary(k => k, k => combined[k]);
	}

	[Fact]
	public void SyntheticChangesRoundTrip()
	{
		var start = LoadStartState("ModernDay");
		Assert.Contains("provinces", start.Keys);
		Assert.Contains("polities", start.Keys);

		// Deep copy through JSON, then change a scalar, add a key, remove a province, append a polity.
		var current = (Dictionary<string, object?>)StateJson.Parse(StateJson.Format(start))!;
		((Dictionary<string, object?>)current["clock"]!)["hour"] = 13.0;
		current["new_state_key"] = 42.0;
		var provinces = (Dictionary<string, object?>)current["provinces"]!;
		provinces.Remove(provinces.Keys.First());
		((List<object?>)current["polities"]!).Add(D(("tag", "TEST"), ("name", "Testland")));
		current["scheduled_events"] = L(D(("trigger_time", 1.0), ("event_data", D(("id", "x")))));

		var diff = StateDiff.Diff(start, current);
		Assert.Contains("clock", diff.Keys);
		Assert.Contains("new_state_key", diff.Keys);
		Assert.Contains("scheduled_events", diff.Keys);
		AssertState(current, StateDiff.Merge(start, diff));
	}

	[Fact]
	public void ExistingSaveMergesAndRediffs()
	{
		var save = LoadDict("saves/fish.json");
		Assert.Equal("res://starts/ModernDay/map_data.json", save["scenario_path"]);

		var start = LoadStartState("ModernDay");
		var loaded = StateDiff.Merge(start, (Dictionary<string, object?>)save["diff"]!);
		var again = StateDiff.Merge(start, StateDiff.Diff(start, loaded));

		// Array diffs are set-based (add/remove), so a rediff keeps neither order nor duplicates.
		// fish.json adds 5 polities that the current ModernDay start already has, so compare distinct items.
		Assert.Equal(loaded.Keys.OrderBy(k => k), again.Keys.OrderBy(k => k));
		foreach (var key in loaded.Keys)
		{
			if (loaded[key] is List<object?> list)
				Assert.Equal(Distinct(list), Distinct((List<object?>)again[key]!));
			else
				AssertState(loaded[key], again[key]);
		}
	}

	private static List<string> Distinct(List<object?> list) =>
		list.Select(StateJson.Format).Distinct().OrderBy(s => s).ToList();

	[Fact]
	public void JsonFormatRoundTripsSave()
	{
		var save = StateJson.Parse(File.ReadAllText(Path.Combine(Root, "saves/fish.json")));
		AssertState(save, StateJson.Parse(StateJson.Format(save)));
	}
}
