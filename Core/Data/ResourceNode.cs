using System.Collections.Generic;

namespace Sosr;

public sealed class ResourceNode
{
	public string type = "";
	public int amount;
	public float quality;

	public static ResourceNode FromDict(Dictionary<string, object?> aData) => new()
	{
		type = aData.Get("type").Str(),
		amount = aData.ContainsKey("amount") ? aData["amount"].Int() : 1,
		quality = aData.ContainsKey("quality") ? aData["quality"].Single() : 1.0f,
	};

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "type", type },
		{ "amount", (double)amount },
		{ "quality", (double)quality },
	};
}
