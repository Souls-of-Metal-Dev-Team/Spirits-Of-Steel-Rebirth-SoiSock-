using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class IdeologyDriftTarget : RefCounted
{
	public Vector2 finalPosition = new Vector2(0, 0);
	public float driftAmount = 0;

	public static IdeologyDriftTarget FromDict(Godot.Collections.Dictionary aData)
	{
		var ideologyDriftTarget = new IdeologyDriftTarget();

		var finalPositionData = aData["final_position"].AsGodotArray();
		ideologyDriftTarget.finalPosition = new Vector2(finalPositionData[0].AsSingle(), finalPositionData[1].AsSingle());
		ideologyDriftTarget.driftAmount = aData["drift_amount"].AsSingle();

		return ideologyDriftTarget;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "final_position", new Godot.Collections.Array { finalPosition.X, finalPosition.Y } },
			{ "drift_amount", driftAmount },
		};
	}
}
