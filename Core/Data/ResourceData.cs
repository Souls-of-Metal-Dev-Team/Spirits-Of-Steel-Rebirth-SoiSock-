using System.Collections.Generic;

namespace Sosr;

public sealed class ResourceData
{
	public string name = "";
	public string color = "#FFFFFF";
	public string icon = "";
	public Dictionary<string, object?> tags = new();

	public static ResourceData FromDict(Dictionary<string, object?> aData)
	{
		var resource = new ResourceData
		{
			name = aData.Get("name").Str(),
			color = aData.Get("color").Hex(),
			icon = aData.ContainsKey("icon") ? aData["icon"].Str() : "",
		};
		foreach (var (key, value) in aData.Get("tags").Dict())
			resource.tags[key] = value.Double();
		return resource;
	}

	public static ResourceData FromValues(string aName, string aColor, string aIcon, Dictionary<string, object?> aTags)
	{
		var resource = new ResourceData
		{
			name = aName,
			color = aColor.Hex(),
			icon = aIcon,
		};
		foreach (var (key, value) in aTags)
			resource.tags[key] = value.Double();
		return resource;
	}

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "name", name },
		{ "color", color },
		{ "icon", icon },
		{ "tags", tags },
	};
}
