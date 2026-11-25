using Godot;

namespace Sosr;

[GlobalClass]
public partial class BuildingTemplate : RefCounted
{
	public string name = "";
	public float power;
	public float water;
	public float sewage;
	public float population;
	public float money_cost;

	public Godot.Collections.Array functionalities = new();
	public Godot.Collections.Dictionary assigned_inputs = new();

	public bool on_coast;
	public float produces;
	public Godot.Collections.Dictionary gather_resources = new();
	public float storage;
	public float heal_amount;
	public float supply_amount;
	public float tech_points;

	public static BuildingTemplate FromDict(Godot.Collections.Dictionary a_data)
	{
		var building = new BuildingTemplate();

		building.name = a_data.ContainsKey("name") ? (string)a_data["name"] : "";
		building.power = a_data.ContainsKey("power") ? a_data["power"].AsSingle() : 0.0f;
		building.water = a_data.ContainsKey("water") ? a_data["water"].AsSingle() : 0.0f;
		building.sewage = a_data.ContainsKey("sewage") ? a_data["sewage"].AsSingle() : 0.0f;
		building.population = a_data.ContainsKey("population") ? a_data["population"].AsSingle() : 0.0f;
		building.money_cost = a_data.ContainsKey("money_cost") ? a_data["money_cost"].AsSingle() : 0.0f;

		building.on_coast = a_data.ContainsKey("on_coast") && a_data["on_coast"].AsBool();
		building.produces = a_data.ContainsKey("produces") ? a_data["produces"].AsSingle() : 0.0f;
		building.gather_resources = a_data.ContainsKey("gather_resources") ? a_data["gather_resources"].AsGodotDictionary() : new Godot.Collections.Dictionary();
		building.storage = a_data.ContainsKey("storage") ? a_data["storage"].AsSingle() : 0.0f;
		building.heal_amount = a_data.ContainsKey("heal_amount") ? a_data["heal_amount"].AsSingle() : 0.0f;
		building.supply_amount = a_data.ContainsKey("supply_amount") ? a_data["supply_amount"].AsSingle() : 0.0f;
		building.tech_points = a_data.ContainsKey("tech_points") ? a_data["tech_points"].AsSingle() : 0.0f;

		building.functionalities = a_data.ContainsKey("functionalities") ? a_data["functionalities"].AsGodotArray() : new Godot.Collections.Array();
		building.assigned_inputs = a_data.ContainsKey("assigned_inputs") ? a_data["assigned_inputs"].AsGodotDictionary() : new Godot.Collections.Dictionary();

		return building;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "power", power },
			{ "water", water },
			{ "sewage", sewage },
			{ "population", population },
			{ "money_cost", money_cost },
			{ "on_coast", on_coast },
			{ "produces", produces },
			{ "gather_resources", gather_resources },
			{ "storage", storage },
			{ "heal_amount", heal_amount },
			{ "supply_amount", supply_amount },
			{ "tech_points", tech_points },
			{ "functionalities", functionalities },
			{ "assigned_inputs", assigned_inputs },
		};
	}
}
