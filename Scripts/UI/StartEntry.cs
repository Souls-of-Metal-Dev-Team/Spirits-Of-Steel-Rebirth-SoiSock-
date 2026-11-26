using Godot;

namespace Sosr;

public partial class StartEntry : Control
{
	private TextureRect backgroundRect;
	private Button button;

	public override void _Ready()
	{
		backgroundRect = GetNode<TextureRect>("VBoxContainer/background");
		button = GetNode<Button>("VBoxContainer/Button");
	}

	public void setup(string aText, string aBackground, string mapdataPath)
	{
		if (button != null)
		{
			button.Text = aText;
			button.Pressed += () =>
			{
				// World.DoSetup reads map_data.json for this start and reports progress.
				GameState.Instance.currentStart = mapdataPath.GetBaseDir().GetFile();
				SceneManager.Instance.SwitchTo(SceneManager.TypeWorld);
			};
		}

		if (!string.IsNullOrEmpty(aBackground))
		{
			if (FileAccess.FileExists(aBackground) || ResourceLoader.Exists(aBackground))
				backgroundRect.Texture = GD.Load<Texture2D>(aBackground);
		}
	}
}
