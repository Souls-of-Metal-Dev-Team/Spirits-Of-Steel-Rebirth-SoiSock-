using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class SaveGame : Resource
{
	// --- MapManager Data ---
	[Export] public Godot.Collections.Dictionary provinceToCountry = new Godot.Collections.Dictionary();
	[Export] public Godot.Collections.Dictionary countryToProvinces = new Godot.Collections.Dictionary();
	[Export] public Godot.Collections.Dictionary provinceObjects = new Godot.Collections.Dictionary(); // Generic dict is safer

	[Export] public Godot.Collections.Array troops = new Godot.Collections.Array();
	[Export] public Godot.Collections.Array movingTroops = new Godot.Collections.Array();
	[Export] public Godot.Collections.Dictionary troopsByProvince = new Godot.Collections.Dictionary();
	[Export] public Godot.Collections.Dictionary troopsByCountry = new Godot.Collections.Dictionary();

	[Export] public Godot.Collections.Dictionary countries = new Godot.Collections.Dictionary();
	[Export] public string playerCountryName = "";
}
