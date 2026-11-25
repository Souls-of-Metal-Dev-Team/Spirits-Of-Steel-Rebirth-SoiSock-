using Godot;

namespace Sosr;

[GlobalClass]
public partial class ResourceData : RefCounted
{
	public string name = "";
	public Color color = Colors.White;
	public string icon = "";
	public Godot.Collections.Dictionary tags = new();

	public static ResourceData FromDict(Godot.Collections.Dictionary a_data)
	{
		var resource = new ResourceData();

		resource.name = (string)a_data["name"];
		resource.color = Color.FromHtml(a_data.ContainsKey("color") ? (string)a_data["color"] : "#FFFFFF");
		resource.icon = a_data.ContainsKey("icon") ? (string)a_data["icon"] : "";

		var typed_tags = new Godot.Collections.Dictionary();
		var raw_tags = a_data.ContainsKey("tags") ? a_data["tags"].AsGodotDictionary() : new Godot.Collections.Dictionary();
		foreach (var k in raw_tags.Keys)
		{
			typed_tags[k.ToString()] = raw_tags[k].AsSingle();
		}
		resource.tags = typed_tags;

		return resource;
	}

	public static ResourceData FromValues(string a_name, Color a_color, string a_icon, Godot.Collections.Dictionary a_tags)
	{
		var resource = new ResourceData();

		resource.name = a_name;
		resource.color = a_color;
		resource.icon = a_icon;
		resource.tags = new Godot.Collections.Dictionary();
		foreach (var k in a_tags.Keys)
		{
			resource.tags[k.ToString()] = a_tags[k].AsSingle();
		}

		return resource;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "color", "#" + color.ToHtml(false).ToUpperInvariant() },
			{ "icon", icon },
			{ "tags", tags },
		};
	}
}
