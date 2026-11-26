using Godot;
using Sosr;

namespace Sosr;

public partial class KeyboardManager : Node
{
	public static KeyboardManager Instance => GameState.Instance.keyboardManager;

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

	public override void _UnhandledInput(InputEvent aEvent)
	{
		if (GameConsole.IsVisible())
			return;

		if (aEvent is InputEventKey)
		{
			var world = GameState.Instance.currentWorld;
			var clock = world?.clock;

			if (aEvent.IsActionPressed("deselect_troops"))
			{
				if (settings != null && GodotObject.IsInstanceValid(settings))
					settings.Visible = !settings.Visible;

				if (SceneType() == TypeWorld)
				{
					var troopSelection = TroopManager.Instance.troopSelection;
					if (troopSelection != null && GodotObject.IsInstanceValid(troopSelection))
					{
						if (troopSelection.selectedTroops.Count > 0)
							troopSelection.DeselectAll();
						else
							EmitSignal(SignalName.SidemenuCloseRequested);
					}
					else
						EmitSignal(SignalName.SidemenuCloseRequested);
				}
			}

			if (aEvent.IsActionPressed("open_menu"))
			{
				if (SceneType() != TypeEditor)
					EmitSignal(SignalName.SidemenuToggleRequested);
			}

			// --- 2. MAP MODE CYCLING (Independent of Menu) ---
			if (aEvent.IsActionPressed("cycle_map_mode"))
				_cycle_map_mode((aEvent as InputEventWithModifiers)?.ShiftPressed ?? false);

			if (world != null && GodotObject.IsInstanceValid(world) && SceneType() == TypeWorld)
			{
				if (aEvent.IsActionPressed("pause_game"))
					clock?.TogglePause();
				else if (aEvent.IsActionPressed("increase_speed"))
					clock?.IncreaseSpeed();
				else if (aEvent.IsActionPressed("decrease_speed"))
					clock?.DecreaseSpeed();
			}
		}
	}

	public void _cycle_map_mode(bool shift)
	{
		MapManager.Instance.CycleMapMode(shift);
	}

	private static int SceneType() => SceneManager.Instance?._current_type ?? -1;
}
