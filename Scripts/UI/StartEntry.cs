using Godot;

namespace Sosr;

public partial class StartEntry : Control
{
	private TextureRect background_rect;
	private Button button;

	public override void _Ready()
	{
		background_rect = GetNode<TextureRect>("VBoxContainer/background");
		button = GetNode<Button>("VBoxContainer/Button");
	}

	public void setup(string a_text, string a_background, string mapdata_path)
	{
		if (button != null)
		{
			button.Text = a_text;
			button.Pressed += () =>
			{
				// World.DoSetup reads map_data.json for this start and reports progress.
				GameState.Instance.current_start = mapdata_path.GetBaseDir().GetFile();
				SceneManager.Instance.switch_to(SceneManager.TypeWorld);
			};
		}

		if (!string.IsNullOrEmpty(a_background))
		{
			if (FileAccess.FileExists(a_background) || ResourceLoader.Exists(a_background))
				background_rect.Texture = GD.Load<Texture2D>(a_background);
		}
	}
}
