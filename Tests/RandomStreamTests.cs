using System.Collections.Generic;
using Xunit;

namespace Sosr.Tests;

public class RandomStreamTests
{
	[Fact]
	public void SameSeedReplaysDraws()
	{
		var a = new RandomStream();
		var b = new RandomStream();
		a.Reset(12345);
		b.Reset(12345);

		var drawsA = new List<int>();
		var drawsB = new List<int>();
		for (var i = 0; i < 100; i++)
		{
			drawsA.Add(a.RandRange(0, 9));
			drawsB.Add(b.RandRange(0, 9));
		}
		Assert.Equal(drawsA, drawsB);
	}

	[Fact]
	public void ShuffleAndPickAreDeterministic()
	{
		var a = new RandomStream();
		var b = new RandomStream();
		a.Reset(99);
		b.Reset(99);

		var first = new List<string> { "a", "b", "c", "d" };
		var second = new List<string>(first);
		a.Shuffle(first);
		b.Shuffle(second);
		Assert.Equal(first, second);
		Assert.Equal(a.Pick(first), b.Pick(second));
	}
}
