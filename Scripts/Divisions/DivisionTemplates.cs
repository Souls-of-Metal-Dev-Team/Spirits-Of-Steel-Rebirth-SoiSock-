using Godot;
using Sosr;

namespace Sosr;

// Port of Scripts/Divisions/DivisionTemplates.gd. This is an AUTOLOAD (a Node),
// not a preloaded RefCounted script, so it deliberately has no [GlobalClass].
public partial class DivisionTemplates : Node
{
	public static DivisionTemplates Instance => GameState.Instance.divisionTemplates;

	public Godot.Collections.Dictionary TEMPLATES = new Godot.Collections.Dictionary
	{
		{
			"infantry", new Godot.Collections.Dictionary
			{
				{ "hp", 100.0f }, { "manpower", 10000 }, { "cost", 500 },
				{ "days", 9 }, { "attack", 1 }, { "defense", 1 }, { "speed", 1.0f },
			}
		},
		{
			"tank", new Godot.Collections.Dictionary
			{
				{ "hp", 280.0f }, { "manpower", 20000 }, { "cost", 10000 },
				{ "days", 30 }, { "attack", 5 }, { "defense", 7 }, { "speed", 2.5f },
			}
		},
		{
			"artillery", new Godot.Collections.Dictionary
			{
				{ "hp", 50.0f }, { "manpower", 1000 }, { "cost", 10000 },
				{ "days", 15 }, { "attack", 5 }, { "defense", 0.3f }, { "speed", 0.8f },
			}
		},
	};
}
