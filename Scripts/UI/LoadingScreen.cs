using Godot;

namespace Sosr;

public partial class LoadingScreen : CanvasLayer
{
	private ProgressBar progress_bar;
	private Label status_label;
	private Panel panel;

	public override void _Ready()
	{
		progress_bar = GetNode<ProgressBar>("Panel/ProgressBar");
		status_label = GetNode<Label>("Panel/Label");
		panel = GetNode<Panel>("Panel");
		// Start invisible
		panel.Modulate = panel.Modulate with { A = 0 };
		Hide();
	}

	public void set_progress(float value)
	{
		progress_bar.Value = value * 100;
	}

	public async void show_screen()
	{
		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);

		Show();
		var tween = CreateTween();
		tween.TweenProperty(panel, "modulate:a", 1.0, 0.3).SetTrans(Tween.TransitionType.Sine);
	}

	public async void hide_screen()
	{
		var tween = CreateTween();
		tween.TweenProperty(panel, "modulate:a", 0.0, 0.3).SetTrans(Tween.TransitionType.Sine);
		await ToSignal(tween, Tween.SignalName.Finished);
		Hide();
	}
}
