using Godot;

namespace Sosr;

public partial class AvatarOfHate : Control
{
	private int m_politburo = 0;
	[Export] public bool sankara = false;
	[Export] public Godot.Collections.Array<Texture> ussr = new();
	// Usually a TextureRect, but scenes also point it at this node itself (a TextureRect
	// running this Control script), so the texture is read and written as a property.
	[Export] public Control avatarImage;

	private Texture AvatarTexture
	{
		get => avatarImage.Get(TextureRect.PropertyName.Texture).As<Texture>();
		set => avatarImage.Set(TextureRect.PropertyName.Texture, value);
	}

	public override void _Ready()
	{
		if (ussr.Count <= 0)
			ussr.Add(AvatarTexture);
		AvatarTexture = ussr[m_politburo];
	}

	public void OnTimerTimeout()
	{
		if (ussr.Count == 0)
			return;
		m_politburo = sankara ? GD.RandRange(0, ussr.Count - 1) : (m_politburo + 1) % ussr.Count;
		AvatarTexture = ussr[m_politburo];
	}

	public void ForceReset()
	{
		m_politburo = 0;
		if (ussr.Count > 0)
			AvatarTexture = ussr[m_politburo];
	}
}
