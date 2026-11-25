using Godot;

namespace Sosr;

[GlobalClass]
public partial class PopulationData : RefCounted
{
	public string ethnicity = "";
	public string culture = "";
	public int amount;

	public static PopulationData FromDict(Godot.Collections.Dictionary a_data)
	{
		var population = new PopulationData();

		population.ethnicity = a_data.ContainsKey("ethnicity") ? (string)a_data["ethnicity"] : "unknown";
		population.culture = a_data.ContainsKey("culture") ? (string)a_data["culture"] : "unknown";
		population.amount = a_data["amount"].AsInt32();

		return population;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "ethnicity", ethnicity },
			{ "culture", culture },
			{ "amount", amount },
		};
	}
}
