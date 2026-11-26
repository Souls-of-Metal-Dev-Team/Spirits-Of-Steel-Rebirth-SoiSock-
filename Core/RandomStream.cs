using System;
using System.Collections.Generic;

namespace Sosr;

// The simulation's only RNG. xorshift64*, identical to the engine-side SimRandom
// wrapper so headless tests and the game replay the same stream.
public sealed class RandomStream
{
	public ulong State = 0x9E3779B97F4A7C15UL;

	public void Reset(ulong seed) => State = seed == 0 ? 1UL : seed;

	private ulong NextBits()
	{
		var x = State;
		x ^= x << 13;
		x ^= x >> 7;
		x ^= x << 17;
		State = x;
		return x;
	}

	public uint NextUInt() => (uint)(NextBits() >> 32);

	public double NextDouble() => NextBits() * (1.0 / 18446744073709551616.0);

	// Inclusive at both ends, matching Godot's RandRange.
	public int RandRange(int min, int max) => min + (int)(NextUInt() % (uint)(max - min + 1));

	public double RandRange(double min, double max) => min + NextDouble() * (max - min);

	public T Pick<T>(IList<T> items) =>
		items.Count == 0 ? default! : items[(int)(NextUInt() % (uint)items.Count)];

	public void Shuffle<T>(IList<T> items)
	{
		for (var i = items.Count - 1; i > 0; i--)
		{
			var j = (int)(NextUInt() % (uint)(i + 1));
			(items[i], items[j]) = (items[j], items[i]);
		}
	}
}
