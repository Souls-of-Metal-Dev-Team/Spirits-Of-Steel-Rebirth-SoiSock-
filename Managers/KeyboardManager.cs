using Godot;
using Sosr;

namespace Sosr;

public partial class KeyboardManager : Node
{
	public static KeyboardManager Instance => Autoload.Get<KeyboardManager>("KeyboardManager");

	// Escape pressed with no troops selected. GameUI closes the side menu if open.
	[Signal]
	public delegate void SidemenuCloseRequestedEventHandler();

	// Open-menu key pressed. GameUI toggles the side menu.
	[Signal]
	public delegate void SidemenuToggleRequestedEventHandler();

	// Use the same names as your MapManager functions for clarity.
	public CanvasLayer settings;

	private const int TypeWorld = SceneManager.TypeWorld;
	private const int TypeEditor = SceneManager.TypeEditor;

	public override void _Ready()
	{
		settings = GetNodeOrNull<CanvasLayer>("/root/Main/SettingsLayer");
	}

	// Original GDScript method name, kept as an alias of the engine callback.
	public void _ready() => _Ready();

	public override void _UnhandledInput(InputEvent a_event)
	{
		if (GameConsole.IsVisible())
			return;

		if (a_event is InputEventKey)
		{
			var world = GameState.Instance.current_world;
			var clock = world?.clock;

			if (a_event.IsActionPressed("deselect_troops"))
			{
				if (settings != null && GodotObject.IsInstanceValid(settings))
					settings.Visible = !settings.Visible;

				if (SceneType() == TypeWorld)
				{
					var troopSelection = TroopManager.Instance.troop_selection;
					if (troopSelection != null && GodotObject.IsInstanceValid(troopSelection))
					{
						if (troopSelection.selected_troops.Count > 0)
							troopSelection.deselect_all();
						else
							EmitSignal(SignalName.SidemenuCloseRequested);
					}
					else
						EmitSignal(SignalName.SidemenuCloseRequested);
				}
			}

			if (a_event.IsActionPressed("open_menu"))
			{
				if (SceneType() != TypeEditor)
					EmitSignal(SignalName.SidemenuToggleRequested);
			}

			// --- 2. MAP MODE CYCLING (Independent of Menu) ---
			if (a_event.IsActionPressed("cycle_map_mode"))
				_cycle_map_mode((a_event as InputEventWithModifiers)?.ShiftPressed ?? false);

			if (world != null && GodotObject.IsInstanceValid(world) && SceneType() == TypeWorld)
			{
				if (a_event.IsActionPressed("pause_game"))
					clock?.toggle_pause();
				else if (a_event.IsActionPressed("increase_speed"))
					clock?.increase_speed();
				else if (a_event.IsActionPressed("decrease_speed"))
					clock?.decrease_speed();
			}
		}
	}

	// Original GDScript method name, kept as an alias of the engine callback.
	public void _unhandled_input(InputEvent a_event) => _UnhandledInput(a_event);

	public void _cycle_map_mode(bool shift)
	{
		MapManager.Instance.CycleMapMode(shift);
	}

	private static int SceneType() => SceneManager.Instance?._current_type ?? -1;
}
