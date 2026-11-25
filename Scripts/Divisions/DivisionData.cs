using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class DivisionData : RefCounted
{
	// --- CONFIGURATION (Game Balance) ---

	// NOTE(soi):ideally the player should make their own division templates ala hoi4 style but for now its enum time

	public static Dictionary<string, Godot.Collections.Dictionary> TEMPLATES { get; } = new()
	{
		{
			"infantry", new Godot.Collections.Dictionary
			{
				{ "hp", 100.0f },
				{ "manpower", 10000 },
				{ "cost", 500 },
				{ "days", 9 },
				{ "attack", 1 },
				{ "defense", 1 },
				{ "speed", 1.0f },
			}
		},
		{
			"tank", new Godot.Collections.Dictionary
			{
				{ "hp", 280.0f },
				{ "manpower", 20000 },
				{ "cost", 10000 },
				{ "days", 30 },
				{ "attack", 5 },
				{ "defense", 7 },
				{ "speed", 2.5f },
			}
		},
		{
			"artillery", new Godot.Collections.Dictionary
			{
				{ "hp", 50.0f },
				{ "manpower", 1000 },
				{ "cost", 10000 },
				{ "days", 15 },
				{ "attack", 5 },
				{ "defense", 0.3f },
				{ "speed", 0.8f },
			}
		},
	};

	// --- Instance Properties ---
	[Export] public string name = "Infantry Division";
	[Export] public string type = "infantry";
	[Export] public float hp = 100.0f; // Current HP
	[Export] public float max_hp = 100.0f; // Max HP (for UI bars)
	[Export] public float experience = 0.0f;
	[Export] public int max_manpower = 10000;
	[Export] public int manpowerPerHP = 100;

	public DivisionData()
	{
		manpowerPerHP = (int)(max_manpower / max_hp);
	}

	// NOTE(sockmit): This isnt sockmit BUT SOILAD
	// NOTE(Sockmit2007): Soilad's errors:
	//  - Didn't make FromDict static
	//  - Had no defaults for missing arguments
	//  - Forgot to add return type
	public static DivisionData FromDict(Godot.Collections.Dictionary a_dict)
	{
		var division = new DivisionData();
		division.name = a_dict["name"].AsString();
		division.type = a_dict.ContainsKey("type") ? a_dict["type"].AsString() : "infantry";
		division.hp = a_dict.ContainsKey("hp") ? a_dict["hp"].AsSingle() : 100f;
		division.max_hp = a_dict.ContainsKey("max_hp") ? a_dict["max_hp"].AsSingle() : 100f;
		division.experience = a_dict.ContainsKey("experience") ? a_dict["experience"].AsSingle() : 0.0f;
		division.max_manpower = a_dict.ContainsKey("max_manpower") ? a_dict["max_manpower"].AsInt32() : 10000;
		division.manpowerPerHP = (int)(division.max_manpower / division.max_hp);
		return division;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "type", type },
			{ "hp", hp },
			{ "max_hp", max_hp },
			{ "experience", experience },
			{ "max_manpower", max_manpower },
		};
	}

	// --- Helper to get stats safely ---
	private Godot.Collections.Dictionary GetStats(string p_type)
	{
		return TEMPLATES.ContainsKey(p_type)
			? TEMPLATES[p_type]
			: TEMPLATES["infantry"];
	}

	public float get_attack_power()
	{
		return GetStats(type)["attack"].AsSingle() * (1.0f + (experience * 0.5f));
	}

	public float get_defense_power()
	{
		return GetStats(type)["defense"].AsSingle() * (1.0f + (experience * 0.5f));
	}

	public static DivisionData create_division(string p_type)
	{
		var div = new DivisionData();
		div.type = p_type;

		// Load stats from template
		var stats = TEMPLATES.ContainsKey(p_type)
			? TEMPLATES[p_type]
			: TEMPLATES["infantry"];

		div.hp = stats["hp"].AsSingle(); // Set starting HP
		div.max_hp = stats["hp"].AsSingle(); // Set Max HP
		div.max_manpower = stats["manpower"].AsInt32();
		div.manpowerPerHP = (int)(div.max_manpower / div.max_hp);

		div.name = p_type.Capitalize();

		return div;
	}
}
