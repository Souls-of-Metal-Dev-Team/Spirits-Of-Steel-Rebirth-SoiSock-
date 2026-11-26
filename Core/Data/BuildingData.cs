using System.Collections.Generic;

namespace Sosr;

public sealed class BuildingData
{
	public enum BuildingState
	{
		CONSTRUCTION = 0,
		FUNCTIONAL = 1,
		RUIN = 2,
	}

	public const int CONSTRUCTION = 0;
	public const int FUNCTIONAL = 1;
	public const int RUIN = 2;

	public string type = "";
	public BuildingState state;
	public float durability;

	public static BuildingData FromValues(string aType, BuildingState aState, float aDurability = 1.0f) => new()
	{
		type = aType,
		state = aState,
		durability = aDurability,
	};

	public static BuildingData FromDict(Dictionary<string, object?> aData) => new()
	{
		type = aData.Get("type").Str(),
		state = aData.ContainsKey("state") ? (BuildingState)aData["state"].Int() : BuildingState.FUNCTIONAL,
		durability = aData.ContainsKey("durability") ? aData["durability"].Single() : 1.0f,
	};

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "type", type },
		{ "state", (double)(int)state },
		{ "durability", (double)durability },
	};
}
