using System.Collections.Generic;

namespace Sosr;

public sealed class TroopData
{
	public string countryName = "";
	// Live Godot country object, supplied by the host. Never serialized.
	public object? countryObj;
	public int provinceId;
	public Vec2 position = Vec2.Zero;

	public List<DivisionData> storedDivisions = new();
	public int divisionsCount => storedDivisions.Count;

	public bool isMoving;
	public List<object?> path = new();
	public Vec2 targetPosition = Vec2.Zero;
	public float progress;
	public Vec2 legStart = Vec2.Zero;
	public float legHours = 1.0f;

	public TroopData() { }

	public TroopData(string pCountry, int pProvinceId, int pDivisions, Vec2 pPosition)
	{
		if (pCountry == "")
			return;

		countryName = pCountry;
		provinceId = pProvinceId;
		position = pPosition;

		for (var i = 0; i < pDivisions; i++)
			storedDivisions.Add(new DivisionData { name = $"Division {i + 1}" });
	}

	public static TroopData Create(string pCountry, int pProvinceId, int pDivisions, Vec2 pPosition) =>
		new(pCountry, pProvinceId, pDivisions, pPosition);

	public Dictionary<string, object?> ToDict()
	{
		var divisions = new List<object?>();
		foreach (var div in storedDivisions)
			divisions.Add(div.ToDict());

		var pathValues = new List<object?>();
		foreach (var node in path)
			pathValues.Add(node);

		return new Dictionary<string, object?>
		{
			{ "country_name", countryName },
			{ "divisions", divisions },
			{ "is_moving", isMoving },
			{ "path", pathValues },
			{ "target_position", new List<object?> { (double)targetPosition.X, (double)targetPosition.Y } },
			{ "progress", (double)progress },
		};
	}

	public static TroopData FromDict(Dictionary<string, object?> data)
	{
		var troop = new TroopData(data.Get("country_name").Str(), 0, 0, Vec2.Zero);
		foreach (var divData in data.Get("divisions").List())
			troop.storedDivisions.Add(DivisionData.FromDict(divData.Dict()));

		troop.isMoving = data.Get("is_moving").Bool();
		foreach (var node in data.Get("path").List())
			troop.path.Add(node);

		var target = data.Get("target_position").List();
		troop.targetPosition = new Vec2(target[0].Single(), target[1].Single());
		troop.progress = data.Get("progress").Single();
		return troop;
	}
}
