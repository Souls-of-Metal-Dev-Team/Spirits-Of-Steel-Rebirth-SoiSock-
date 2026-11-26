using System.Collections.Generic;

namespace Sosr;

public sealed class BuildingTemplate
{
	public string name = "";
	public float power;
	public float water;
	public float sewage;
	public float population;
	public float moneyCost;

	public List<object?> functionalities = new();
	public Dictionary<string, object?> assignedInputs = new();

	public bool onCoast;
	public float produces;
	public Dictionary<string, object?> gatherResources = new();
	public float storage;
	public float healAmount;
	public float supplyAmount;
	public float techPoints;

	public static BuildingTemplate FromDict(Dictionary<string, object?> aData) => new()
	{
		name = aData.Get("name").Str(),
		power = aData.Get("power").Single(),
		water = aData.Get("water").Single(),
		sewage = aData.Get("sewage").Single(),
		population = aData.Get("population").Single(),
		moneyCost = aData.Get("money_cost").Single(),
		onCoast = aData.Get("on_coast").Bool(),
		produces = aData.Get("produces").Single(),
		gatherResources = aData.Get("gather_resources").Dict(),
		storage = aData.Get("storage").Single(),
		healAmount = aData.Get("heal_amount").Single(),
		supplyAmount = aData.Get("supply_amount").Single(),
		techPoints = aData.Get("tech_points").Single(),
		functionalities = aData.Get("functionalities").List(),
		assignedInputs = aData.Get("assigned_inputs").Dict(),
	};

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "name", name },
		{ "power", (double)power },
		{ "water", (double)water },
		{ "sewage", (double)sewage },
		{ "population", (double)population },
		{ "money_cost", (double)moneyCost },
		{ "on_coast", onCoast },
		{ "produces", (double)produces },
		{ "gather_resources", gatherResources },
		{ "storage", (double)storage },
		{ "heal_amount", (double)healAmount },
		{ "supply_amount", (double)supplyAmount },
		{ "tech_points", (double)techPoints },
		{ "functionalities", functionalities },
		{ "assigned_inputs", assignedInputs },
	};
}
