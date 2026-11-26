using System;

namespace Sosr;

// Engine-free replacement for Godot.Vector2 in data state.
public readonly struct Vec2 : IEquatable<Vec2>
{
	public static readonly Vec2 Zero = new(0f, 0f);

	public readonly float X;
	public readonly float Y;

	public Vec2(float x, float y) { X = x; Y = y; }

	public float Length() => MathF.Sqrt(X * X + Y * Y);

	public float DistanceTo(Vec2 other) => (other - this).Length();

	public Vec2 Lerp(Vec2 other, float weight) => new(X + (other.X - X) * weight, Y + (other.Y - Y) * weight);

	public Vec2 DirectionTo(Vec2 other) => (other - this).Normalized();

	public Vec2 Normalized()
	{
		var length = Length();
		return length == 0f ? Zero : new Vec2(X / length, Y / length);
	}

	public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
	public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
	public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
	public static Vec2 operator *(Vec2 a, float scale) => new(a.X * scale, a.Y * scale);
	public static Vec2 operator *(float scale, Vec2 a) => a * scale;

	public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
	public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y);

	public override string ToString() => $"({X}, {Y})";
}

// Engine-free replacement for Godot.Vector2I.
public readonly struct Vec2I : IEquatable<Vec2I>
{
	public static readonly Vec2I Zero = new(0, 0);

	public readonly int X;
	public readonly int Y;

	public Vec2I(int x, int y) { X = x; Y = y; }

	public bool Equals(Vec2I other) => X == other.X && Y == other.Y;
	public override bool Equals(object? obj) => obj is Vec2I other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(X, Y);
}
