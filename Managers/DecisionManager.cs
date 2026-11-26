using Godot;
using Sosr;
using System.Collections.Generic;

namespace Sosr;

public partial class DecisionManager : Node
{
	public static DecisionManager Instance => GameState.Instance.decisionManager;

	public const string ROOT_DEFAULT_PATH = "res://decisions/default.json";

	public Godot.Collections.Dictionary defaultCategories = new();
	public Godot.Collections.Dictionary countryDecisionsMap = new(); // { "China": { "Economy": [...] } }
	public Godot.Collections.Dictionary countryFilePaths = new(); // { "Germany": "res://starts/ModernDay/decisions/Germany.json" }
	public string currentBasePath = ""; // Decisions folder of the loaded start

	public Godot.Collections.Dictionary activeDecisions = new(); // { "Germany": { "eco_1": 5 } }
	public DecisionTreeUI uiOverlay = null;
	public bool debug = false;

	// Decisions are only loaded from a start's folder (see StartEntry / World).
	public void LoadDecisionsFromPath(string basePath)
	{
		_load_decisions(basePath);
	}

	private void _load_decisions(string basePath)
	{
		if (!basePath.EndsWith("/"))
			basePath += "/";
		currentBasePath = basePath;

		// Clear existing data before loading new ones
		defaultCategories.Clear();
		countryDecisionsMap.Clear();
		countryFilePaths.Clear();

		// 1. Load Default: start's default.json, else the root default.json
		var defaultPath = basePath + "default.json";
		if (!FileAccess.FileExists(defaultPath))
			defaultPath = ROOT_DEFAULT_PATH;
		var defaultJson = _read_json(defaultPath);
		if (defaultJson.VariantType == Variant.Type.Dictionary)
		{
			var dict = defaultJson.AsGodotDictionary();
			defaultCategories = dict.ContainsKey("categories") ? dict["categories"].AsGodotDictionary() : new Godot.Collections.Dictionary();
			countryFilePaths["_default"] = defaultPath;
		}

		// 2. Load Country Specific
		var dir = DirAccess.Open(basePath);
		if (dir == null)
			return;
		dir.ListDirBegin();
		var fileName = dir.GetNext();
		while (fileName != "")
		{
			if (!dir.CurrentIsDir() && fileName.EndsWith(".json") && fileName != "default.json")
			{
				var countryKey = fileName.Substring(0, fileName.Length - ".json".Length);
				var json = _read_json(basePath + fileName);
				if (json.VariantType == Variant.Type.Dictionary)
				{
					var dict = json.AsGodotDictionary();
					if (dict.ContainsKey("categories"))
					{
						countryDecisionsMap[countryKey] = dict["categories"];
						countryFilePaths[countryKey] = basePath + fileName;
					}
				}
			}
			fileName = dir.GetNext();
		}
		dir.ListDirEnd();
	}

	public Variant _read_json(string path)
	{
		return MapData.Instance.LoadJSON(path);
	}

	public Godot.Collections.Dictionary GetCountryCategories(string countryName)
	{
		return countryDecisionsMap.ContainsKey(countryName)
			? countryDecisionsMap[countryName].AsGodotDictionary()
			: defaultCategories;
	}

	public Godot.Collections.Dictionary GetCountryCategoriesEditable(string countryName)
	{
		if (!countryDecisionsMap.ContainsKey(countryName))
		{
			// Create a deep copy of default categories for this country
			var copiedCats = new Godot.Collections.Dictionary();
			foreach (var catName in defaultCategories.Keys)
			{
				copiedCats[catName] = defaultCategories[catName].AsGodotDictionary().Duplicate(true);
			}
			countryDecisionsMap[countryName] = copiedCats;

			// New country trees are saved next to the start's other decisions
			if (!countryFilePaths.ContainsKey(countryName))
				countryFilePaths[countryName] = currentBasePath + countryName + ".json";
		}

		return countryDecisionsMap[countryName].AsGodotDictionary();
	}

	// --- TICKING SYSTEM ---
	public void ProcessCountryDay(GodotObject country)
	{
		var countryName = country.Get("countryName").AsString();
		if (!activeDecisions.ContainsKey(countryName))
			return;

		var tasks = activeDecisions[countryName].AsGodotDictionary();
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
			if (IsSame(country, CountryManager.Instance.playerCountry))
			{
				// 1. Find the decision data to get the title
				var decisionTitle = "Unknown Decision";
				var countryCats = GetCountryCategories(countryName);

				// Look through all categories to find the matching ID
				foreach (var cat in countryCats.Values)
				{
					foreach (var decisionValue in cat.AsGodotArray())
					{
						var decision = decisionValue.AsGodotDictionary();
						if (decision.ContainsKey("id") && decision["id"].AsString() == key)
						{
							decisionTitle = decision.ContainsKey("title") ? decision["title"].AsString() : decisionTitle;
							break;
						}
					}
				}

				// 2. Show the alert with the actual title
				EventManager.Instance.ShowAlert(new Godot.Collections.Dictionary
				{
					{ "event", "default" },
					{ "c1", country },
					{ "text", decisionTitle + " completed" },
				});
			}

			tasks.Remove(key);
		}

		// RefreshStatusOnly returns early while the menu is hidden.
		if (IsInstanceValid(uiOverlay) && country.Get("isPlayer").AsBool())
			uiOverlay.RefreshStatusOnly(); // Efficient refresh
	}

	// --- ACTIONS ---
	public bool CanTakeDecision(GodotObject country, string cat, int index)
	{
		var data = GetCountryCategories(country.Get("countryName").AsString())[cat].AsGodotArray()[index].AsGodotDictionary();
		var id = data["id"].AsString();
		if (debug) return true;

		// 1. NEW: Check if busy with ANY decision
		if (IsCountryBusy(country))
			return false;

		// 2. Check if already done or currently this specific one (redundant but safe)
		if (country.HasMeta("finished_" + id) || IsInProgress(country, id))
			return false;

		// 3. Check Prerequisite
		if (data.ContainsKey("prereq"))
		{
			var parentId = data["prereq"].AsString();
			if (!country.HasMeta("finished_" + parentId))
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
				var exId = exValue.AsString();
				if (country.HasMeta("finished_" + exId) || IsInProgress(country, exId))
					return false;
			}
		}

		// 4. Check Cost
		var costPp = data.ContainsKey("cost_pp") ? data["cost_pp"].AsSingle() : 0f;
		if (country.Get("politicalPower").AsSingle() < costPp)
			return false;

		// 5. Check Cost
		if (data.TryGetValue("reqs", out var reqs) && reqs.VariantType == Variant.Type.Array)
		{
			foreach (var req in reqs.AsGodotArray())
			{
				if (!InterpreterManager.Instance.GetFunction(req).AsBool())
					return false;
			}
		}
		return true;
	}

	public void StartDecision(GodotObject country, string cat, int index)
	{
		if (!CanTakeDecision(country, cat, index))
			return;

		var data = GetCountryCategories(country.Get("countryName").AsString())[cat].AsGodotArray()[index].AsGodotDictionary();
		var costPp = data.ContainsKey("cost_pp") ? data["cost_pp"].AsSingle() : 0f;
		country.Set("politicalPower", country.Get("politicalPower").AsSingle() - costPp);

		var countryName = country.Get("countryName").AsString();
		if (!activeDecisions.ContainsKey(countryName))
			activeDecisions[countryName] = new Godot.Collections.Dictionary();

		var id = data["id"].AsString();
		var tasks = activeDecisions[countryName].AsGodotDictionary();
		tasks[id] = data.ContainsKey("days") ? data["days"].AsInt32() : 5;

		if (debug)
			ProcessCountryDay(country);

		if (IsInstanceValid(uiOverlay) && country.Get("isPlayer").AsBool())
			uiOverlay.RefreshStatusOnly();
	}

	private void _finalize_decision(GodotObject country, string id)
	{
		country.Get("finishedDecisions").AsGodotArray().Add(id);
		country.SetMeta("finished_" + id.Replace("#", "_"), true);

		// Find the data to get the action (Slow search, but happens rarely)
		foreach (var cat in GetCountryCategories(country.Get("countryName").AsString()).Values)
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
		InterpreterManager.Instance.GetFunction(action, country);
	}

	// --- HELPERS ---
	public bool IsInProgress(GodotObject country, string id)
	{
		var countryName = country.Get("countryName").AsString();
		return activeDecisions.ContainsKey(countryName) && activeDecisions[countryName].AsGodotDictionary().ContainsKey(id);
	}

	public int GetDaysLeft(GodotObject country, string id)
	{
		var countryName = country.Get("countryName").AsString();
		if (!activeDecisions.ContainsKey(countryName))
			return 0;
		var tasks = activeDecisions[countryName].AsGodotDictionary();
		return tasks.ContainsKey(id) ? tasks[id].AsInt32() : 0;
	}

	// Add/Update these functions in DecisionManager.gd

	// Check if the country has ANY active timers
	public bool IsCountryBusy(GodotObject country)
	{
		var countryName = country.Get("countryName").AsString();
		return activeDecisions.ContainsKey(countryName) && activeDecisions[countryName].AsGodotDictionary().Count > 0;
	}

	public bool HasAvailableDecisions(GodotObject country)
	{
		var categories = GetCountryCategories(country.Get("countryName").AsString());
		var choices = new Godot.Collections.Array();
		foreach (var catValue in categories.Keys)
		{
			var cat = catValue.AsString();
			var decisions = categories[catValue].AsGodotArray();
			for (var i = 0; i < decisions.Count; i++)
			{
				if (CanTakeDecision(country, cat, i))
				{
					if (country.Get("isPlayer").AsBool())
						return true;
					choices.Add(new Godot.Collections.Array { country, cat, i });
				}
			}
		}
		if (choices.Count == 0)
		{
			// GD.Print($"No available decisions for {country.Get("countryName").AsString()}");
			return false;
		}
		var choice = choices[SimRandom.Shared.RandRange(0, choices.Count - 1)].AsGodotArray();
		StartDecision(choice[0].AsGodotObject(), choice[1].AsString(), choice[2].AsInt32());
		return false;
	}

	public void SaveCountryDecisions(string countryName)
	{
		var path = countryFilePaths.ContainsKey(countryName) ? countryFilePaths[countryName].AsString() : "";
		var cats = countryDecisionsMap.ContainsKey(countryName) ? countryDecisionsMap[countryName].AsGodotDictionary() : new Godot.Collections.Dictionary();

		if (cats.Count == 0)
		{
			// If country doesn't have a specific file, check default
			path = countryFilePaths.ContainsKey("_default") ? countryFilePaths["_default"].AsString() : "";
			cats = defaultCategories;
		}

		if (string.IsNullOrEmpty(path) || cats.Count == 0)
		{
			GD.Print("Error: Could not find path or categories for ", countryName);
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
