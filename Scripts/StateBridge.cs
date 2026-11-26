using Godot;

namespace Sosr;

// Conversions between engine value types and the engine-free values in Sosr.Core.
public static class StateBridge
{
	public static Vec2 ToCore(this Vector2 value) => new(value.X, value.Y);
	public static Vector2 ToGodot(this Vec2 value) => new(value.X, value.Y);

	public static Vec2I ToCore(this Vector2I value) => new(value.X, value.Y);
	public static Vector2I ToGodot(this Vec2I value) => new(value.X, value.Y);

	public static Color ToColor(this string hex) => Color.FromHtml(hex);
	public static string ToHex(this Color color) => "#" + color.ToHtml(false).ToUpperInvariant();
}
