using Godot;

namespace Sosr;

public partial class ScrollingLabel : ScrollContainer
{
	[Export] public int scrollPixels = 5;
	[Export] public Control child;

	public void Ignite()
	{
		if (child.Size.X - ScrollHorizontal <= Size.X)
			ScrollHorizontal = 0;
		else
			ScrollHorizontal += scrollPixels;
	}
}
