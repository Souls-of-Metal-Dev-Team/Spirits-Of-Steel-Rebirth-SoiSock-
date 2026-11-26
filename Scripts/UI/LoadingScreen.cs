using Godot;

namespace Sosr;

public partial class LoadingScreen : CanvasLayer
{
	private ProgressBar progressBar;
	private Label statusLabel;
	private Panel panel;

	public override void _Ready()
	{
		progressBar = GetNode<ProgressBar>("Panel/ProgressBar");
		statusLabel = GetNode<Label>("Panel/Label");
		panel = GetNode<Panel>("Panel");
		// Start invisible
		panel.Modulate = panel.Modulate with { A = 0 };
		Hide();
	}

	public void SetProgress(float value)
	{
		progressBar.Value = value * 100;
	}

	public void SetStatus(string text)
	{
		statusLabel.Text = text;
	}

	public async void ShowScreen()
	{
		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);

		Show();
		var tween = CreateTween();
		tween.TweenProperty(panel, "modulate:a", 1.0, 0.3).SetTrans(Tween.TransitionType.Sine);
	}

	public async void HideScreen()
	{
		var tween = CreateTween();
		tween.TweenProperty(panel, "modulate:a", 0.0, 0.3).SetTrans(Tween.TransitionType.Sine);
		await ToSignal(tween, Tween.SignalName.Finished);
		Hide();
	}
}
