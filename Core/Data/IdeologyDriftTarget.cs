using System.Collections.Generic;

namespace Sosr;

public sealed class IdeologyDriftTarget
{
	public Vec2 finalPosition = Vec2.Zero;
	public float driftAmount;

	public static IdeologyDriftTarget FromDict(Dictionary<string, object?> aData)
	{
		var position = aData.Get("final_position").List();
		return new IdeologyDriftTarget
		{
			finalPosition = new Vec2(position[0].Single(), position[1].Single()),
			driftAmount = aData.Get("drift_amount").Single(),
		};
	}

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "final_position", new List<object?> { (double)finalPosition.X, (double)finalPosition.Y } },
		{ "drift_amount", (double)driftAmount },
	};
}
