using Godot;

[GlobalClass]
public partial class BiomeData : RefCounted
{
	public string name = "";
	public Color color = Colors.White;

	public static BiomeData FromDict(Godot.Collections.Dictionary aData)
	{
		var biomeData = new BiomeData();
		biomeData.name = (string)aData["name"];
		biomeData.color = aData.ContainsKey("color") ? Color.FromHtml((string)aData["color"]) : Colors.White;
		return biomeData;
	}

	public static BiomeData FromValues(string aName, Color aColor)
	{
		var biomeData = new BiomeData();
		biomeData.name = aName;
		biomeData.color = aColor;
		return biomeData;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "color", "#" + color.ToHtml(false).ToUpperInvariant() },
		};
	}
}
