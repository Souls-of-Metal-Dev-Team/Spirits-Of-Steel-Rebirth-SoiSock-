using Godot;

namespace Sosr;

[GlobalClass]
public partial class SaveEntry : HBoxContainer
{
	[Signal] public delegate void load_requestedEventHandler(string save_name);
	[Signal] public delegate void delete_requestedEventHandler(string save_name);

	[Export] public Label name_label;
	[Export] public Button load_button;
	[Export] public Button delete_button;

	public string save_name = "";

	public void Setup(string p_save_name, bool scenario_exists, string scenario_path)
	{
		save_name = p_save_name;
		name_label.Text = save_name;
		if (!scenario_exists)
		{
			name_label.Modulate = new Color(0.5f, 0.5f, 0.5f);
			load_button.Disabled = true;
			load_button.TooltipText = "Scenario file missing: " + scenario_path;
		}
	}

	public void _on_load_pressed()
	{
		EmitSignal(SignalName.load_requested, save_name);
	}

	public void _on_delete_pressed()
	{
		EmitSignal(SignalName.delete_requested, save_name);
	}
}
