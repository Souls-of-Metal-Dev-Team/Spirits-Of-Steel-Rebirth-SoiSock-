using Godot;

namespace Sosr;

public partial class Links : RichTextLabel
{
	public void _on_meta_clicked(Variant meta)
	{
		OS.ShellOpen(meta.AsString());
	}
}
