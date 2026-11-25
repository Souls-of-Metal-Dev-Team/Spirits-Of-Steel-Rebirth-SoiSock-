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
		set => apply_enabled(value);
	}
	private bool _enabled = false;

	[Export] public bool show_centroids = true;
	[Export] public bool show_labels = true;

	[Export] public float dot_size = 1.0f; // Size in WORLD units (perfect at any zoom)
	[Export] public Color dot_color = new Color(0, 1, 0, 0.8f);
	[Export] public Color hovered_color = new Color(1, 0.8f, 0, 1);
	[Export] public Color selected_color = new Color(1, 1, 0, 1);

	public Sprite2D map_sprite;
	public Dictionary<int, Vector2> province_centers = new();
	public int selected_pid = -1;
	public int hovered_pid = -1;

	public Camera2D camera;

	public int[] path_to_highlight = System.Array.Empty<int>();

	public void highlight_path(int[] path)
	{
		path_to_highlight = path == null ? System.Array.Empty<int>() : (int[])path.Clone();
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
			map_sprite = GetTree().CurrentScene.GetNodeOrNull<Sprite2D>("MapContainer/CultureSprite");

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
		if (!enabled || map_sprite == null || province_centers.Count == 0)
			return;

		foreach (var center in province_centers.Values)
		{
			var world_pos = center + map_sprite.Position;
			DrawCircle(world_pos, dot_size * 0.5f, Colors.Green);
		}

		if (path_to_highlight.Length > 1)
		{
			for (var i = 0; i < path_to_highlight.Length - 1; i++)
			{
				var pos1 = CenterOrZero(path_to_highlight[i]) + map_sprite.Offset;
				var pos2 = CenterOrZero(path_to_highlight[i + 1]) + map_sprite.Offset;
				DrawLine(pos1, pos2, Colors.Red, 3.0f);
			}
		}
	}

	private Vector2 CenterOrZero(int pid)
		=> province_centers.TryGetValue(pid, out var center) ? center : Vector2.Zero;

	public void set_centers(Dictionary<int, Vector2> centers)
	{
		province_centers = new Dictionary<int, Vector2>(centers);
		QueueRedraw();
	}

	public void select_province(int pid)
	{
		selected_pid = pid;
		QueueRedraw();
	}

	public void hover_province(int pid)
	{
		hovered_pid = pid;
		QueueRedraw();
	}

	public void apply_enabled(bool v)
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
				show_labels = !show_labels;
		}
	}
}
