using System.Collections.Generic;

namespace Sosr;

public sealed class BiomeData
{
	public string name = "";
	// Hex string, e.g. "#FFFFFF" (Godot conversion happens at the UI boundary).
	public string color = "#FFFFFF";

	public static BiomeData FromDict(Dictionary<string, object?> aData) => new()
	{
		name = aData.Get("name").Str(),
		color = aData.Get("color").Hex(),
	};

	public static BiomeData FromValues(string aName, string aColor) => new()
	{
		name = aName,
		color = aColor.Hex(),
	};

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "name", name },
		{ "color", color },
	};
}
