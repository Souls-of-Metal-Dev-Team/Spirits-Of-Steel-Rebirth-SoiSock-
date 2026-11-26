using Godot;
using Sosr;

namespace Sosr;

// Engine-facing wrapper around Sosr.Core.RandomStream. UI/audio keep Godot's
// global RNG, so they cannot shift the sequence a host and its peers replay.
public partial class SimRandom : RefCounted
{
	// Static so early constructors (country/AI) can draw without waiting for autoloads.
	public static readonly SimRandom Shared = new();

	private readonly RandomStream _stream = new();

	public ulong State
	{
		get => _stream.State;
		set => _stream.State = value;
	}

	public void Reset(ulong seed) => _stream.Reset(seed);

	public uint NextUInt() => _stream.NextUInt();

	public double NextDouble() => _stream.NextDouble();

	// Godot's RandRange is inclusive at both ends.
	public int RandRange(int min, int max) => _stream.RandRange(min, max);

	public double RandRange(double min, double max) => _stream.RandRange(min, max);

	public Variant Pick(Godot.Collections.Array items) =>
		items.Count == 0 ? new Variant() : items[(int)(_stream.NextUInt() % (uint)items.Count)];

	public T Pick<[MustBeVariant] T>(Godot.Collections.Array<T> items) =>
		items.Count == 0 ? default : items[(int)(_stream.NextUInt() % (uint)items.Count)];

	public void Shuffle(Godot.Collections.Array items)
	{
		for (var i = items.Count - 1; i > 0; i--)
		{
			var j = (int)(_stream.NextUInt() % (uint)(i + 1));
			(items[i], items[j]) = (items[j], items[i]);
		}
	}

	public void Shuffle<[MustBeVariant] T>(Godot.Collections.Array<T> items)
	{
		for (var i = items.Count - 1; i > 0; i--)
		{
			var j = (int)(_stream.NextUInt() % (uint)(i + 1));
			(items[i], items[j]) = (items[j], items[i]);
		}
	}
}
