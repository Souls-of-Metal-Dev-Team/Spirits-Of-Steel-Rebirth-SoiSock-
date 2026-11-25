using Godot;
using Sosr;

namespace Sosr;

// Port of Scripts/camera_controller.gd. No [GlobalClass]: the original had no class_name.
public partial class CameraController : Node
{
	// The scene assigns a NodePath ("..") to this exported property.
	[Export] public Camera2D camera;
	[Export] public float base_speed = 600.0f;

	public bool is_dragging = false;
	public int top_bar_height = 26;

	public override void _Ready()
	{
		// GDScript's `= get_parent()` default is not available to C# exports.
		if (camera == null)
			camera = GetParent() as Camera2D;
	}

	public override void _Process(double delta)
	{
		if (camera == null)
			return;

		if (GetViewport().GuiGetFocusOwner() == null
			&& !GameState.Instance.decision_menu_open)
		{
			var input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
			camera.Position += input * (base_speed / camera.Zoom.X) * (float)delta;
		}
		_clamp_y();
	}

	// Clamp after every camera move: keyboard, middle-drag and zoom.
	private void _clamp_y()
	{
		if (camera == null)
			return;

		var zoomY = Mathf.Max(Mathf.Abs(camera.Zoom.Y), 0.0001f);
		var minY = -(float)top_bar_height / zoomY;
		var maxY = MapManager.Instance.map_height
			- camera.GetViewportRect().Size.Y / zoomY;
		if (minY > maxY)
		{
			var mid = (minY + maxY) * 0.5f;
			minY = mid;
			maxY = mid;
		}
		var position = camera.Position;
		position.Y = Mathf.Clamp(position.Y, minY, maxY);
		camera.Position = position;
	}

	private bool _is_mouse_over_ui()
	{
		return GetViewport().GuiGetHoveredControl() != null;
	}

	public override void _Input(InputEvent @event)
	{
		if (camera == null)
			return;

		if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Middle)
		{
			if (button.Pressed)
			{
				if (!GameConsole.IsVisible() && !_is_mouse_over_ui())
				{
					is_dragging = true;
					GetViewport().SetInputAsHandled();
				}
			}
			else
			{
				is_dragging = false;
			}
		}

		if (@event is InputEventMouseMotion motion && is_dragging)
		{
			camera.Position -= motion.Relative / camera.Zoom.X;
			_clamp_y();
			return;
		}

		if (GameConsole.IsVisible() || _is_mouse_over_ui())
			return;

		if (@event is InputEventMouseButton pressedButton && pressedButton.IsPressed())
		{
			var zoomDir = 0.0f;
			if (pressedButton.ButtonIndex == MouseButton.WheelUp)
				zoomDir = 1.0f;
			else if (pressedButton.ButtonIndex == MouseButton.WheelDown)
				zoomDir = -1.0f;

			if (zoomDir != 0.0f)
				_perform_zoom(zoomDir * pressedButton.Factor);
		}
	}

	private void _perform_zoom(float direction)
	{
		var mousePosBefore = camera.GetGlobalMousePosition();

		camera.Zoom = (camera.Zoom * (1.0f + direction * 0.1f)).Clamp(
			Vector2.One * camera.GetViewportRect().Size.Y / MapManager.Instance.map_height,
			Vector2.One * 12f);

		camera.Position += mousePosBefore - camera.GetGlobalMousePosition();
		_clamp_y();
	}
}
