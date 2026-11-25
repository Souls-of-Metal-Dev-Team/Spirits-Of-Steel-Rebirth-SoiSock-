using Godot;

namespace Sosr;

public partial class MainMenu : Control
{
	[Export] public Control StartsUI;
	[Export] public Node2D SettingsUI;
	[Export] public Control CreditsUI;

	public override void _Ready()
	{
		var tween = CreateTween().SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(this, "position:x", Position.X, 1.5);
		Position = Position with { X = -800 };
	}

	public void _on_quit_pressed()
	{
		GetTree().Quit();
	}

	public void _on_new_game_pressed()
	{
		StartsUI.Visible = !StartsUI.Visible;
		KeyboardManager.Instance.settings.Visible = false;
	}

	public void _on_settings_pressed()
	{
		var settings = KeyboardManager.Instance.settings;
		settings.Visible = !settings.Visible;
	}

	public void _on_exit_settings_pressed()
	{
		SettingsUI.Visible = false;
	}

	public void _on_credits_pressed()
	{
		CreditsUI.Visible = !CreditsUI.Visible;
		KeyboardManager.Instance.settings.Visible = false;
	}

	public void _on_exit_credits_pressed()
	{
		CreditsUI.Visible = false;
	}
}
