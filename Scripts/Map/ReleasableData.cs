using Godot;

namespace Sosr;

[GlobalClass]
public partial class ReleasableData : RefCounted
{
	public Godot.Collections.Dictionary data = new();
	public int[] claims = System.Array.Empty<int>();

	public static ReleasableData FromDict(Godot.Collections.Dictionary a_data)
	{
		var releasable = new ReleasableData();

		releasable.data = a_data["data"].AsGodotDictionary();
		releasable.claims = a_data.ContainsKey("claims") ? ToIntArray(a_data["claims"]) : System.Array.Empty<int>();

		return releasable;
	}

	private static int[] ToIntArray(Variant a_value)
	{
		var result = new System.Collections.Generic.List<int>();
		foreach (var item in a_value.AsGodotArray())
		{
			result.Add(item.AsInt32());
		}
		return result.ToArray();
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "data", data },
			{ "claims", claims },
		};
	}
}
