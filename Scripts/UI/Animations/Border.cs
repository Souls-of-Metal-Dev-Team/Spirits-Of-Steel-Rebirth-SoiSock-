using Godot;

namespace Sosr;

public partial class Border : Button
{
	private StyleBoxFlat _normal_sb;
	private StyleBoxFlat _hover_sb;
	private Tween _tween;

	private int BORDER_WIDTH = 2;
	private const double DURATION = 0.25;
	private static readonly string[] ANIMATION_PROPERTIES =
		["border_width_left", "border_width_top", "border_width_right", "border_width_bottom"];

	public override void _Ready()
	{
		_normal_sb = (StyleBoxFlat)GetThemeStylebox("normal").Duplicate();
		_hover_sb = (StyleBoxFlat)GetThemeStylebox("hover").Duplicate();
		BORDER_WIDTH = _hover_sb.BorderWidthLeft;
		AddThemeStyleboxOverride("normal", _normal_sb);
		AddThemeStyleboxOverride("hover", _normal_sb);

		MouseEntered += () => _tween_border(BORDER_WIDTH);
		MouseExited += () => _tween_border(0);
	}

	private void _tween_border(int target)
	{
		if (_tween != null && _tween.IsValid())
			_tween.Kill();
		_tween = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

		var first = true;
		foreach (var sb in new[] { _normal_sb, _hover_sb })
		{
			foreach (var prop in ANIMATION_PROPERTIES)
			{
				if (first)
				{
					_tween.TweenProperty(sb, prop, target, DURATION);
					first = false;
				}
				else
				{
					_tween.Parallel().TweenProperty(sb, prop, target, DURATION);
				}
			}
		}
	}
}
