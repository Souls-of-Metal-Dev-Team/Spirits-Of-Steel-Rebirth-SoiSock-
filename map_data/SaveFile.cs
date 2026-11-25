using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class SaveGame : Resource
{
	// --- MapManager Data ---
	[Export] public Godot.Collections.Dictionary province_to_country = new Godot.Collections.Dictionary();
	[Export] public Godot.Collections.Dictionary country_to_provinces = new Godot.Collections.Dictionary();
	[Export] public Godot.Collections.Dictionary province_objects = new Godot.Collections.Dictionary(); // Generic dict is safer

	[Export] public Godot.Collections.Array troops = new Godot.Collections.Array();
	[Export] public Godot.Collections.Array moving_troops = new Godot.Collections.Array();
	[Export] public Godot.Collections.Dictionary troops_by_province = new Godot.Collections.Dictionary();
	[Export] public Godot.Collections.Dictionary troops_by_country = new Godot.Collections.Dictionary();

	[Export] public Godot.Collections.Dictionary countries = new Godot.Collections.Dictionary();
	[Export] public string player_country_name = "";
}
