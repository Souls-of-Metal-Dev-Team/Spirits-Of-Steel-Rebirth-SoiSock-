using Godot;
using Sosr;

namespace Sosr;

public partial class Starts : Control
{
	public Variant startsFolder;
	private readonly PackedScene _startEntryScene = GD.Load<PackedScene>("res://Scenes/Start.tscn");

	[Export] public HFlowContainer startEntry;

	public override void _Ready()
	{
		if (OS.HasFeature("standalone"))
		{
			var exeDir = OS.GetExecutablePath().GetBaseDir();
			startsFolder = exeDir + "starts";
		}
		else
		{
			startsFolder = "res://starts/";
		}

		var startsDir = DirAccess.Open(startsFolder.AsString());
		foreach (var start in startsDir.GetDirectories())
		{
			var entry = _startEntryScene.Instantiate<Node>();
			startEntry.AddChild(entry);
			entry.Call("setup", start,
				startsFolder.AsString() + start + "/thumbnail.png",
				startsFolder.AsString() + start + "/map_data.json");
			GD.Print(start);
		}
		//print(start_entry.get_children())
	}
}
