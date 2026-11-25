using Godot;

namespace Sosr;

[GlobalClass]
public partial class ResourceNode : RefCounted
{
	public string type = "";
	public int amount;
	public float quality;

	public static ResourceNode FromDict(Godot.Collections.Dictionary a_data)
	{
		var resource = new ResourceNode();

		resource.type = (string)a_data["type"];
		resource.amount = a_data.ContainsKey("amount") ? a_data["amount"].AsInt32() : 1;
		resource.quality = a_data.ContainsKey("quality") ? a_data["quality"].AsSingle() : 1.0f;

		return resource;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "type", type },
			{ "amount", amount },
			{ "quality", quality },
		};
	}
}
