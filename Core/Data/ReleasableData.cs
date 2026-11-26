using System;
using System.Collections.Generic;

namespace Sosr;

public sealed class ReleasableData
{
	public Dictionary<string, object?> data = new();
	public int[] claims = Array.Empty<int>();

	public static ReleasableData FromDict(Dictionary<string, object?> aData) => new()
	{
		data = aData.Get("data").Dict(),
		claims = ToIntArray(aData.Get("claims")),
	};

	private static int[] ToIntArray(object? value)
	{
		var result = new List<int>();
		foreach (var item in value.List())
			result.Add(item.Int());
		return result.ToArray();
	}

	public Dictionary<string, object?> ToDict()
	{
		var list = new List<object?>();
		foreach (var claim in claims)
			list.Add((double)claim);
		return new Dictionary<string, object?>
		{
			{ "data", data },
			{ "claims", list },
		};
	}
}
