using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Sosr.Tests;

public class SaveServiceTests
{
	private static readonly string Root = FindRoot();

	private static string FindRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent;
		return dir?.FullName ?? throw new DirectoryNotFoundException("project.godot not found");
	}

	private static Dictionary<string, object?> Load(string relativePath) =>
		(Dictionary<string, object?>)StateJson.Parse(File.ReadAllText(Path.Combine(Root, relativePath)))!;

	private static Dictionary<string, object?> Start(string name) => SaveService.BuildStartState(
		Load($"starts/{name}/map_data.json"),
		Load($"starts/{name}/provinces.json"),
		Load($"starts/{name}/scenario_data.json"));

	[Fact]
	public void RealSaveMergesAndRediffs()
	{
		var start = Start("ModernDay");
		var save = Load("saves/fish.json");
		var loaded = SaveService.ApplySave(start, save);
		var again = SaveService.ApplySave(start, SaveService.CreateSave(start, loaded, (string)save["scenario_path"]!));

		Assert.Equal(loaded.Keys.OrderBy(k => k), again.Keys.OrderBy(k => k));
		foreach (var key in loaded.Keys)
		{
			if (loaded[key] is List<object?> list)
			{
				var other = (List<object?>)again[key]!;
				Assert.Equal(
					list.Select(StateJson.Format).Distinct().OrderBy(s => s),
					other.Select(StateJson.Format).Distinct().OrderBy(s => s));
			}
			else
				Assert.True(StateValue.Equal(loaded[key], again[key]), key);
		}
	}

	[Fact]
	public void LegacyFullSavePassesThrough()
	{
		var legacy = new Dictionary<string, object?> { { "clock", new Dictionary<string, object?>() } };
		var result = SaveService.ApplySave(Start("ModernDay"), legacy);
		Assert.True(StateValue.Equal(legacy, result));
	}
}
