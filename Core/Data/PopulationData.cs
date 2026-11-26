using System.Collections.Generic;

namespace Sosr;

public sealed class PopulationData
{
	public string ethnicity = "";
	public string culture = "";
	public int amount;

	public static PopulationData FromDict(Dictionary<string, object?> aData) => new()
	{
		ethnicity = aData.ContainsKey("ethnicity") ? aData["ethnicity"].Str("unknown") : "unknown",
		culture = aData.ContainsKey("culture") ? aData["culture"].Str("unknown") : "unknown",
		amount = aData.Get("amount").Int(),
	};

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "ethnicity", ethnicity },
		{ "culture", culture },
		{ "amount", (double)amount },
	};
}
