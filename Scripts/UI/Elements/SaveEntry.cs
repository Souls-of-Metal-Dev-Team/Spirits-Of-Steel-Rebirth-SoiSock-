using Godot;

namespace Sosr;

[GlobalClass]
public partial class SaveEntry : HBoxContainer
{
	[Signal] public delegate void LoadRequestedEventHandler(string saveName);
	[Signal] public delegate void DeleteRequestedEventHandler(string saveName);

	[Export] public Label nameLabel;
	[Export] public Button loadButton;
	[Export] public Button deleteButton;

	public string saveName = "";

	public void Setup(string pSaveName, bool scenarioExists, string scenarioPath)
	{
		saveName = pSaveName;
		nameLabel.Text = saveName;
		if (!scenarioExists)
		{
			nameLabel.Modulate = new Color(0.5f, 0.5f, 0.5f);
			loadButton.Disabled = true;
			loadButton.TooltipText = "Scenario file missing: " + scenarioPath;
		}
	}

	public void _on_load_pressed()
	{
		EmitSignal(SignalName.LoadRequested, saveName);
	}

	public void _on_delete_pressed()
	{
		EmitSignal(SignalName.DeleteRequested, saveName);
	}
}
