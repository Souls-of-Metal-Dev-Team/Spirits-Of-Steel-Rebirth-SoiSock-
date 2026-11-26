using Godot;
using Sosr;
using System.Collections.Generic;

namespace Sosr;

public partial class MapDebugOverlay : Node2D
{
	[Export]
	public bool enabled
	{
		get => _enabled;
		set => ApplyEnabled(value);
	}
	private bool _enabled = false;

	[Export] public bool showCentroids = true;
	[Export] public bool showLabels = true;

	[Export] public float dotSize = 1.0f; // Size in WORLD units (perfect at any zoom)
	[Export] public Color dotColor = new Color(0, 1, 0, 0.8f);
	[Export] public Color hoveredColor = new Color(1, 0.8f, 0, 1);
	[Export] public Color selectedColor = new Color(1, 1, 0, 1);

	public Sprite2D mapSprite;
	public Dictionary<int, Vector2> provinceCenters = new();
	public int selectedPid = -1;
	public int hoveredPid = -1;

	public Camera2D camera;

	public int[] pathToHighlight = System.Array.Empty<int>();

	public void HighlightPath(int[] path)
	{
		pathToHighlight = path == null ? System.Array.Empty<int>() : (int[])path.Clone();
		QueueRedraw();
	}

	public override void _Ready()
	{
		ZIndex = 999;
		SetProcessInput(true);
		camera = GetViewport().GetCamera2D();
	}

	public override void _EnterTree()
	{
		// Auto-find the map sprite (adjust path if needed)
		if (GetTree().CurrentScene != null)
			mapSprite = GetTree().CurrentScene.GetNodeOrNull<Sprite2D>("MapContainer/CultureSprite");

		// Make sure this overlay follows the camera perfectly
		GetViewport().GetCamera2D()?.CallDeferred("make_current");
	}

	public override void _Process(double _delta)
	{
		// Force this Node2D to be a child of the camera's canvas
		// This makes all drawing world-space and zoom-perfect
		if (GetParent() != GetViewport())
		{
			GetViewport().AddChild(this);
			GlobalPosition = Vector2.Zero;
		}
	}

	public override void _Draw()
	{
		if (!enabled || mapSprite == null || provinceCenters.Count == 0)
			return;

		foreach (var center in provinceCenters.Values)
		{
			var worldPos = center + mapSprite.Position;
			DrawCircle(worldPos, dotSize * 0.5f, Colors.Green);
		}

		if (pathToHighlight.Length > 1)
		{
			for (var i = 0; i < pathToHighlight.Length - 1; i++)
			{
				var pos1 = CenterOrZero(pathToHighlight[i]) + mapSprite.Offset;
				var pos2 = CenterOrZero(pathToHighlight[i + 1]) + mapSprite.Offset;
				DrawLine(pos1, pos2, Colors.Red, 3.0f);
			}
		}
	}

	private Vector2 CenterOrZero(int pid)
		=> provinceCenters.TryGetValue(pid, out var center) ? center : Vector2.Zero;

	public void SetCenters(Dictionary<int, Vector2> centers)
	{
		provinceCenters = new Dictionary<int, Vector2>(centers);
		QueueRedraw();
	}

	public void SelectProvince(int pid)
	{
		selectedPid = pid;
		QueueRedraw();
	}

	public void HoverProvince(int pid)
	{
		hoveredPid = pid;
		QueueRedraw();
	}

	public void ApplyEnabled(bool v)
	{
		_enabled = v;
		Visible = v;
		QueueRedraw();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed)
		{
			if (key.Keycode == Key.F3)
			{
				enabled = !enabled;
				GD.Print("MapDebugOverlay: ", enabled ? "ON" : "OFF");
			}
			if (key.Keycode == Key.F4)
				showLabels = !showLabels;
		}
	}
}
