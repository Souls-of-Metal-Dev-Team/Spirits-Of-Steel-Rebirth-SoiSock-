using Godot;

namespace Sosr;

public partial class StartEntryUI : Node2D
{
	private TextureRect background_rect;
	private Label desc_label;
	private Button button;

	public override void _Ready()
	{
		background_rect = GetNode<TextureRect>("Panel/background");
		desc_label = GetNode<Label>("Panel/description");
		button = GetNode<Button>("Button");
		button.Pressed += _on_button_pressed;
	}

	public void setup(Godot.Collections.Dictionary data)
	{
		// Data format: { "background": "ww3", "desc": "...", "button": "..." }
		if (data.ContainsKey("desc"))
			desc_label.Text = data["desc"].AsString();

		if (data.ContainsKey("button"))
			button.Text = data["button"].AsString();

		if (data.ContainsKey("background"))
		{
			var path = "res://assets/superevents/" + data["background"].AsString() + ".png";
			if (FileAccess.FileExists(path) || ResourceLoader.Exists(path))
				background_rect.Texture = GD.Load<Texture2D>(path);
			else
				GD.PushWarning($"SuperEvent: Background image not found at {path}");
		}
	}

	private void _on_button_pressed()
	{
		QueueFree();
	}
}
