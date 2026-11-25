using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class TroopData : RefCounted
{
	// --- Core Properties ---
	public string country_name = "";
	public RefCounted country_obj; // CountryData (now RefCounted, not Resource)
	public int province_id;
	public Vector2 position;

	public Godot.Collections.Array<DivisionData> stored_divisions = new Godot.Collections.Array<DivisionData>();

	public int divisions_count => stored_divisions.Count;

	public bool is_moving = false;
	public Godot.Collections.Array path = new Godot.Collections.Array();
	public Vector2 target_position = Vector2.Zero;
	public float progress = 0.0f;
	public Tween move_tween = null;

	public TroopData() { }

	public static TroopData Create(string p_country, int p_province_id, int p_divisions, Vector2 p_position, Texture2D p_flag)
	{
		var troop = new TroopData();
		troop.Initialize(p_country, p_province_id, p_divisions, p_position, p_flag);
		return troop;
	}

	public TroopData(string p_country, int p_province_id, int p_divisions, Vector2 p_position, Texture2D p_flag)
	{
		Initialize(p_country, p_province_id, p_divisions, p_position, p_flag);
	}

	private void Initialize(string p_country, int p_province_id, int p_divisions, Vector2 p_position, Texture2D p_flag)
	{
		if (p_country == "")
			return;

		country_name = p_country;
		province_id = p_province_id;
		position = p_position;

		var countries = CountryManager.Instance.countries;
		country_obj = countries.ContainsKey(p_country)
			? countries[p_country].AsGodotObject() as RefCounted
			: null;

		for (var i = 0; i < p_divisions; i++)
		{
			var div = new DivisionData();
			div.name = $"Division {i + 1}";
			stored_divisions.Add(div);
		}
	}

	public Godot.Collections.Dictionary ToDict()
	{
		var divisions = new Godot.Collections.Array();
		foreach (var div in stored_divisions)
			divisions.Add(div.ToDict());

		return new Godot.Collections.Dictionary
		{
			{ "country_name", country_name },
			{ "divisions", divisions },
			{ "is_moving", is_moving },
			{ "path", path },
			{ "target_position", new Godot.Collections.Array { target_position.X, target_position.Y } },
			{ "progress", progress },
		};
	}

	public static TroopData FromDict(Godot.Collections.Dictionary data)
	{
		// p_country: String, p_province_id: int, p_divisions: int, p_position: Vector2, _p_flag: Texture2D
		var troop = new TroopData(data["country_name"].AsString(), 0, 0, Vector2.Zero, null);

		troop.stored_divisions.Clear();
		foreach (var divData in data["divisions"].AsGodotArray())
			troop.stored_divisions.Add(DivisionData.FromDict(divData.AsGodotDictionary()));

		troop.is_moving = data.ContainsKey("is_moving") ? data["is_moving"].AsBool() : false;
		troop.path = data.ContainsKey("path") ? data["path"].AsGodotArray() : new Godot.Collections.Array();

		var tp = data.ContainsKey("target_position")
			? data["target_position"].AsGodotArray()
			: new Godot.Collections.Array { 0, 0 };
		troop.target_position = new Vector2(tp[0].AsSingle(), tp[1].AsSingle());

		troop.progress = data.ContainsKey("progress") ? data["progress"].AsSingle() : 0.0f;

		return troop;
	}
}
