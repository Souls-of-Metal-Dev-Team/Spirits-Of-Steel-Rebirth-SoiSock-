using Godot;
using Sosr;
using System.Collections.Generic;

namespace Sosr;

public partial class DecisionManager : Node
{
	public static DecisionManager Instance => Autoload.Get<DecisionManager>("DecisionManager");

	public const string ROOT_DEFAULT_PATH = "res://decisions/default.json";

	public Godot.Collections.Dictionary default_categories = new();
	public Godot.Collections.Dictionary country_decisions_map = new(); // { "China": { "Economy": [...] } }
	public Godot.Collections.Dictionary country_file_paths = new(); // { "Germany": "res://starts/ModernDay/decisions/Germany.json" }
	public string current_base_path = ""; // Decisions folder of the loaded start

	public Godot.Collections.Dictionary active_decisions = new(); // { "Germany": { "eco_1": 5 } }
	public DecisionTreeUI ui_overlay = null;
	public bool debug = false;

	// Decisions are only loaded from a start's folder (see StartEntry / World).
	public void load_decisions_from_path(string base_path)
	{
		_load_decisions(base_path);
	}

	private void _load_decisions(string base_path)
	{
		if (!base_path.EndsWith("/"))
			base_path += "/";
		current_base_path = base_path;

		// Clear existing data before loading new ones
		default_categories.Clear();
		country_decisions_map.Clear();
		country_file_paths.Clear();

		// 1. Load Default: start's default.json, else the root default.json
		var default_path = base_path + "default.json";
		if (!FileAccess.FileExists(default_path))
			default_path = ROOT_DEFAULT_PATH;
		var default_json = _read_json(default_path);
		if (default_json.VariantType == Variant.Type.Dictionary)
		{
			var dict = default_json.AsGodotDictionary();
			default_categories = dict.ContainsKey("categories") ? dict["categories"].AsGodotDictionary() : new Godot.Collections.Dictionary();
			country_file_paths["_default"] = default_path;
		}

		// 2. Load Country Specific
		var dir = DirAccess.Open(base_path);
		if (dir == null)
			return;
		dir.ListDirBegin();
		var file_name = dir.GetNext();
		while (file_name != "")
		{
			if (!dir.CurrentIsDir() && file_name.EndsWith(".json") && file_name != "default.json")
			{
				var country_key = file_name.Substring(0, file_name.Length - ".json".Length);
				var json = _read_json(base_path + file_name);
				if (json.VariantType == Variant.Type.Dictionary)
				{
					var dict = json.AsGodotDictionary();
					if (dict.ContainsKey("categories"))
					{
						country_decisions_map[country_key] = dict["categories"];
						country_file_paths[country_key] = base_path + file_name;
					}
				}
			}
			file_name = dir.GetNext();
		}
		dir.ListDirEnd();
	}

	public Variant _read_json(string path)
	{
		return MapData.Instance.LoadJSON(path);
	}

	public Godot.Collections.Dictionary get_country_categories(string country_name)
	{
		return country_decisions_map.ContainsKey(country_name)
			? country_decisions_map[country_name].AsGodotDictionary()
			: default_categories;
	}

	public Godot.Collections.Dictionary get_country_categories_editable(string country_name)
	{
		if (!country_decisions_map.ContainsKey(country_name))
		{
			// Create a deep copy of default categories for this country
			var copied_cats = new Godot.Collections.Dictionary();
			foreach (var cat_name in default_categories.Keys)
			{
				copied_cats[cat_name] = default_categories[cat_name].AsGodotDictionary().Duplicate(true);
			}
			country_decisions_map[country_name] = copied_cats;

			// New country trees are saved next to the start's other decisions
			if (!country_file_paths.ContainsKey(country_name))
				country_file_paths[country_name] = current_base_path + country_name + ".json";
		}

		return country_decisions_map[country_name].AsGodotDictionary();
	}

	// --- TICKING SYSTEM ---
	public void process_country_day(GodotObject country)
	{
		var country_name = country.Get("country_name").AsString();
		if (!active_decisions.ContainsKey(country_name))
			return;

		var tasks = active_decisions[country_name].AsGodotDictionary();
		var finished = new Godot.Collections.Array();

		var keys = new List<string>();
		foreach (var key in tasks.Keys)
			keys.Add(key.AsString());

		foreach (var key in keys)
		{
			var days = tasks[key].AsInt32() - 1;
			tasks[key] = days;
			if (days <= 0 || debug)
			{
				finished.Add(key);
				_finalize_decision(country, key);
			}
		}

		foreach (var keyValue in finished)
		{
			var key = keyValue.AsString();
			if (IsSame(country, CountryManager.Instance.player_country))
			{
				// 1. Find the decision data to get the title
				var decision_title = "Unknown Decision";
				var country_cats = get_country_categories(country_name);

				// Look through all categories to find the matching ID
				foreach (var cat in country_cats.Values)
				{
					foreach (var decisionValue in cat.AsGodotArray())
					{
						var decision = decisionValue.AsGodotDictionary();
						if (decision.ContainsKey("id") && decision["id"].AsString() == key)
						{
							decision_title = decision.ContainsKey("title") ? decision["title"].AsString() : decision_title;
							break;
						}
					}
				}

				// 2. Show the alert with the actual title
				EventManager.Instance.show_alert(new Godot.Collections.Dictionary
				{
					{ "event", "default" },
					{ "c1", country },
					{ "text", decision_title + " completed" },
				});
			}

			tasks.Remove(key);
		}

		// RefreshStatusOnly returns early while the menu is hidden.
		if (IsInstanceValid(ui_overlay) && country.Get("is_player").AsBool())
			ui_overlay.RefreshStatusOnly(); // Efficient refresh
	}

	// --- ACTIONS ---
	public bool can_take_decision(GodotObject country, string cat, int index)
	{
		var data = get_country_categories(country.Get("country_name").AsString())[cat].AsGodotArray()[index].AsGodotDictionary();
		var id = data["id"].AsString();
		if (debug) return true;

		// 1. NEW: Check if busy with ANY decision
		if (is_country_busy(country))
			return false;

		// 2. Check if already done or currently this specific one (redundant but safe)
		if (country.HasMeta("finished_" + id) || is_in_progress(country, id))
			return false;

		// 3. Check Prerequisite
		if (data.ContainsKey("prereq"))
		{
			var parent_id = data["prereq"].AsString();
			if (!country.HasMeta("finished_" + parent_id))
				return false;
		}

		// 3.5 Check Mutually Exclusive
		if (data.ContainsKey("exclusive"))
		{
			var exclusives = data["exclusive"];
			if (exclusives.VariantType != Variant.Type.Array)
				exclusives = new Godot.Collections.Array { exclusives };
			foreach (var exValue in exclusives.AsGodotArray())
			{
				var ex_id = exValue.AsString();
				if (country.HasMeta("finished_" + ex_id) || is_in_progress(country, ex_id))
					return false;
			}
		}

		// 4. Check Cost
		var cost_pp = data.ContainsKey("cost_pp") ? data["cost_pp"].AsSingle() : 0f;
		if (country.Get("political_power").AsSingle() < cost_pp)
			return false;

		// 5. Check Cost
		if (data.TryGetValue("reqs", out var reqs) && reqs.VariantType == Variant.Type.Array)
		{
			foreach (var req in reqs.AsGodotArray())
			{
				if (!InterpreterManager.Instance.get_function(req).AsBool())
					return false;
			}
		}
		return true;
	}

	public void start_decision(GodotObject country, string cat, int index)
	{
		if (!can_take_decision(country, cat, index))
			return;

		var data = get_country_categories(country.Get("country_name").AsString())[cat].AsGodotArray()[index].AsGodotDictionary();
		var cost_pp = data.ContainsKey("cost_pp") ? data["cost_pp"].AsSingle() : 0f;
		country.Set("political_power", country.Get("political_power").AsSingle() - cost_pp);

		var country_name = country.Get("country_name").AsString();
		if (!active_decisions.ContainsKey(country_name))
			active_decisions[country_name] = new Godot.Collections.Dictionary();

		var id = data["id"].AsString();
		var tasks = active_decisions[country_name].AsGodotDictionary();
		tasks[id] = data.ContainsKey("days") ? data["days"].AsInt32() : 5;

		if (debug)
			process_country_day(country);

		if (IsInstanceValid(ui_overlay) && country.Get("is_player").AsBool())
			ui_overlay.RefreshStatusOnly();
	}

	private void _finalize_decision(GodotObject country, string id)
	{
		country.Get("finished_decisions").AsGodotArray().Add(id);
		country.SetMeta("finished_" + id.Replace("#", "_"), true);

		// Find the data to get the action (Slow search, but happens rarely)
		foreach (var cat in get_country_categories(country.Get("country_name").AsString()).Values)
		{
			foreach (var nodeValue in cat.AsGodotArray())
			{
				var node = nodeValue.AsGodotDictionary();
				if (node.ContainsKey("id") && node["id"].AsString() == id)
				{
					Variant action = new Godot.Collections.Dictionary();
					if (node.ContainsKey("action"))
						action = node["action"];
					_apply_reward(country, action);
					return;
				}
			}
		}
	}

	private void _apply_reward(GodotObject country, Variant action)
	{
		InterpreterManager.Instance.get_function(action, country);
	}

	// --- HELPERS ---
	public bool is_in_progress(GodotObject country, string id)
	{
		var country_name = country.Get("country_name").AsString();
		return active_decisions.ContainsKey(country_name) && active_decisions[country_name].AsGodotDictionary().ContainsKey(id);
	}

	public int get_days_left(GodotObject country, string id)
	{
		var country_name = country.Get("country_name").AsString();
		if (!active_decisions.ContainsKey(country_name))
			return 0;
		var tasks = active_decisions[country_name].AsGodotDictionary();
		return tasks.ContainsKey(id) ? tasks[id].AsInt32() : 0;
	}

	// Add/Update these functions in DecisionManager.gd

	// Check if the country has ANY active timers
	public bool is_country_busy(GodotObject country)
	{
		var country_name = country.Get("country_name").AsString();
		return active_decisions.ContainsKey(country_name) && active_decisions[country_name].AsGodotDictionary().Count > 0;
	}

	public bool has_available_decisions(GodotObject country)
	{
		var categories = get_country_categories(country.Get("country_name").AsString());
		var choices = new Godot.Collections.Array();
		foreach (var catValue in categories.Keys)
		{
			var cat = catValue.AsString();
			var decisions = categories[catValue].AsGodotArray();
			for (var i = 0; i < decisions.Count; i++)
			{
				if (can_take_decision(country, cat, i))
				{
					if (country.Get("is_player").AsBool())
						return true;
					choices.Add(new Godot.Collections.Array { country, cat, i });
				}
			}
		}
		if (choices.Count == 0)
		{
			GD.Print($"No available decisions for {country.Get("country_name").AsString()}");
			return false;
		}
		var choice = choices[GD.RandRange(0, choices.Count - 1)].AsGodotArray();
		start_decision(choice[0].AsGodotObject(), choice[1].AsString(), choice[2].AsInt32());
		return false;
	}

	public void save_country_decisions(string country_name)
	{
		var path = country_file_paths.ContainsKey(country_name) ? country_file_paths[country_name].AsString() : "";
		var cats = country_decisions_map.ContainsKey(country_name) ? country_decisions_map[country_name].AsGodotDictionary() : new Godot.Collections.Dictionary();

		if (cats.Count == 0)
		{
			// If country doesn't have a specific file, check default
			path = country_file_paths.ContainsKey("_default") ? country_file_paths["_default"].AsString() : "";
			cats = default_categories;
		}

		if (string.IsNullOrEmpty(path) || cats.Count == 0)
		{
			GD.Print("Error: Could not find path or categories for ", country_name);
			return;
		}

		var data = new Godot.Collections.Dictionary { { "categories", cats } };
		if (MapData.Instance.SaveJSON(path, data))
			GD.Print("Saved decisions to ", path);
	}

	private static bool IsSame(GodotObject a, GodotObject b)
	{
		if (ReferenceEquals(a, b))
			return true;
		if (a is null || b is null)
			return false;
		return a.Equals(b); // GodotObject.Equals compares the underlying Godot instance.
	}
}
