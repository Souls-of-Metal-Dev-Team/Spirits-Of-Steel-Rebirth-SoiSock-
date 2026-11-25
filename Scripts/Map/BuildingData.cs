using Godot;

namespace Sosr;

[GlobalClass]
public partial class BuildingData : RefCounted
{
	public enum BuildingState
	{
		CONSTRUCTION = 0,
		FUNCTIONAL = 1,
		RUIN = 2,
	}

	// GDScript cannot see nested C# enums; keep flat constants for ported call sites.
	public const int CONSTRUCTION = 0;
	public const int FUNCTIONAL = 1;
	public const int RUIN = 2;

	public string type = "";
	public BuildingState state;
	public float durability;

	public static BuildingData FromValues(string a_type, BuildingState a_state, float a_durability = 1.0f)
	{
		var building = new BuildingData();

		building.type = a_type;
		building.state = a_state;
		building.durability = a_durability;

		return building;
	}

	public static BuildingData FromDict(Godot.Collections.Dictionary a_data)
	{
		var building = new BuildingData();

		building.type = (string)a_data["type"];
		building.state = a_data.ContainsKey("state") ? (BuildingState)a_data["state"].AsInt32() : BuildingState.FUNCTIONAL;
		building.durability = a_data.ContainsKey("durability") ? a_data["durability"].AsSingle() : 1.0f;

		return building;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "type", type },
			{ "state", (int)state },
			{ "durability", durability },
		};
	}
}
