using Godot;

namespace Sosr;

public partial class AvatarOfHate : Control
{
	private int mPolitburo = 0;
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
		AvatarTexture = ussr[mPolitburo];
	}

	public void OnTimerTimeout()
	{
		if (ussr.Count == 0)
			return;
		mPolitburo = sankara ? GD.RandRange(0, ussr.Count - 1) : (mPolitburo + 1) % ussr.Count;
		AvatarTexture = ussr[mPolitburo];
	}

	public void ForceReset()
	{
		mPolitburo = 0;
		if (ussr.Count > 0)
			AvatarTexture = ussr[mPolitburo];
	}
}
