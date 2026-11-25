using Godot;
using Sosr;

namespace Sosr;

public partial class Starts : Control
{
	public Variant starts_folder;
	private readonly PackedScene _startEntryScene = GD.Load<PackedScene>("res://Scenes/Start.tscn");

	[Export] public HFlowContainer start_entry;

	public override void _Ready()
	{
		if (OS.HasFeature("standalone"))
		{
			var exe_dir = OS.GetExecutablePath().GetBaseDir();
			starts_folder = exe_dir + "starts";
		}
		else
		{
			starts_folder = "res://starts/";
		}

		var starts_dir = DirAccess.Open(starts_folder.AsString());
		foreach (var start in starts_dir.GetDirectories())
		{
			var entry = _startEntryScene.Instantiate<Node>();
			start_entry.AddChild(entry);
			entry.Call("setup", start,
				starts_folder.AsString() + start + "/thumbnail.png",
				starts_folder.AsString() + start + "/map_data.json");
			GD.Print(start);
		}
		//print(start_entry.get_children())
	}
}
