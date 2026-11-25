using Godot;
using Sosr;

namespace Sosr;

// Port of Scripts/Audio.gd. No [GlobalClass]: the original had no class_name.
public partial class Audio : Node
{
	public override void _Ready()
	{
		MusicManager.Instance.setup_audio();
	}
}
