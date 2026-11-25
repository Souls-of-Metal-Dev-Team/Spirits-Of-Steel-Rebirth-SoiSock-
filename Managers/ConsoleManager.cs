using Godot;
using Sosr;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sosr;

public partial class ConsoleManager : Node
{
	public static ConsoleManager Instance => Autoload.Get<ConsoleManager>("ConsoleManager");

	public Godot.Collections.Array<string> suffixes = new()
	{
		"Diege",
		"Rebellion",
		"Revolution",
		"Revolutioniary_Army",
		"Army",
		"Republic",
		"Reich",
		"Dominion",
		"Kingdom",
		"Empire",
		"Duchy",
	};

	public Godot.Collections.Array<string> prefixes = new()
	{
		"Independent",
		"Islamic",
		"Christian",
		"Hindu",
		"Anarchist",
		"Revolutionary",
		"Democratic",
		"Fascist",
		"Divine",
	};

	public override void _Ready()
	{
		// Console is the last autoload in project.godot, so its node may not exist
		// yet when this _Ready runs. Defer only in that case.
		if (GameConsole.Node == null)
		{
			CallDeferred("RegisterCommands");
			return;
		}
		RegisterCommands();
	}

	// Original GDScript method name, kept as an alias of the engine callback.
	public void _ready() => _Ready();

	private void RegisterCommands()
	{
		var console = GameConsole.Node;
		if (console == null)
			return;

		Register(console, "play_country", Callable.From((string a) => _play_country(a)), new[] { "country_name" }, 1, "Change player country");
		// Register(console, "play_as", Callable.From((string a) => _play_country(a)), new[] { "country_name" }, 1, "Change player country");
		// Register(console, "play_as", ... autocomplete list ...)
		// Register(console, "play", Callable.From((string a) => _play_country(a)), new[] { "country_name" }, 1, "Change player country");
		Register(console, "tag", Callable.From((string a) => _play_country(a)), new[] { "country_name" }, 1, "Change player country");
		Register(console, "drew_durnil_mode", Callable.From(_drew_durnil_mode), Array.Empty<string>(), 0, "Lets u spectate the map");
		Register(console, "access", Callable.From((string a, string b) => m_AddAccess(a, b)), new[] { "accesser", "accessee" }, 1, "Add military access to a country");
		Register(console, "puppet", Callable.From((string a) => _puppet_country(a)), new[] { "country_name" }, 1, "Change player country");
		Register(console, "invite", Callable.From((string a) => _invite_country(a)), new[] { "country_name" }, 1, "Change player country");
		Register(console, "debug_decisions", Callable.From(_debug_decisions), Array.Empty<string>(), 0, "Lets u do any focus and does it instantly");
		Register(console, "set_ideology", Callable.From((string a, string b) => _set_ideology(a, b)), new[] { "x", "y" }, 2, "Change player ideology");
		Register(console, "reload_decisions", Callable.From(_reload_decisions), Array.Empty<string>(), 0, "Reloads all the decision trees");
		Register(console, "instabuild", Callable.From(_instabuild), Array.Empty<string>(), 0, "Toggles instant construction");

		Register(console, "gates_of_hell", Callable.From(m_GatesOfHell), Array.Empty<string>(), 0, "Start armageddon");
		Register(console, "start_war", Callable.From((string a, string b) => _start_war(a, b)), new[] { "a", "b" }, 2, "Start a war between 2 countries");
		Register(console, "annex", Callable.From((string a, string b) => _annex(a, b)), new[] { "annexer", "annexee" }, 2, "Annex Country for Player");
		Register(console, "pp", Callable.From((string a) => _add_pp(a)), new[] { "amount" }, 1, "Add Poltical power to player");
		Register(console, "manpower", Callable.From((string a) => _add_manpower(a)), new[] { "amount" }, 1, "Add Manpower to Country");
		Register(console, "set_manpower", Callable.From((string a) => _set_manpower(a)), new[] { "amount" }, 1, "Sets manpower to a specific amount");
		Register(console, "peace_treaty", Callable.From((string a) => _peace_treaty(a)), new[] { "country" }, 1, "Spawns a peace treaty with country");
		// TODO: Rework this.
		Register(console, "release", Callable.From((string a) => _release_country(a)), new[] { "country" }, 1, "Releases a country based on all its claims; the country must exist, to create a new country, look to instantiate_country");
		Register(console, "releasables", Callable.From((string a) => _show_releasables_country(a)), new[] { "country" }, 1, "Shows the releasables of a country");
		Register(console, "cta", Callable.From((string a, string b) => _call_to_arms(a, b)), new[] { "caller", "target" }, 2, "Call a country to arms");
		Register(console, "editor", Callable.From(m_Editor), Array.Empty<string>(), 0, "Swap Editor");
		Register(console, "sink_rise", Callable.From((string a, string b, string c) => _sink_rise(a, b, c)), new[] { "pid", "type", "country_name" }, 3, "sink/rise province");
		Register(console, "random_figure", Callable.From((string a, string b) => m_RandomFigure(a, b)), new[] { "country", "budget" }, 2, "Add a randomly generated significant figure to a country.");
		Register(console, "start_revolution", Callable.From((string a, string b, string c) => m_StartRevolution(a, b, c)), new[] { "pid", "name", "count" }, 3, "Start a revolution.");
		Register(console, "start_rebellion", Callable.From((string a, string b) => m_StartRebellion(a, b)), new[] { "country", "count" }, 2, "Start a multi front rebellion.");
		Register(console, "partition_country", Callable.From((string a, string b) => m_PartitionCountry(a, b)), new[] { "country", "count" }, 2, "Evenly partition a country into X parts.");
		Register(console, "set_attr", Callable.From((string a, string b, string c) => _set_attr(a, b, c)), new[] { "country", "attr", "val" }, 3, "Sets the atribute of a country");

		var names = CountryManager.Instance.countryNames;
		console.Call("add_command_autocomplete_list", "tag", names);
		console.Call("add_command_autocomplete_list", "annex", names);
	}

	private static void Register(GodotObject console, string name, Callable function, string[] args, int required, string description)
	{
		var argArray = new Godot.Collections.Array<string>();
		foreach (var arg in args)
			argArray.Add(arg);
		console.Call("add_command", name, function, (Godot.Collections.Array)argArray, required, description);
	}

	public void m_GatesOfHell()
	{
		var names = CountryManager.Instance.countryNames;
		var sizer = names.Length;
		for (var countryA = 0; countryA < sizer; countryA++)
		{
			for (var countryB = countryA; countryB < sizer; countryB++)
				m_StartWarSilent(names[countryA], names[countryB]);
		}
	}

	public void m_Editor()
	{
		var currentType = SceneType();
		GD.Print($"Current Scene: {currentType}");
		if (currentType == 1) // SceneSwitcher.Type.WORLD
			SceneManager.Instance.switch_to(2); // EDITOR
		else if (currentType == 2) // SceneSwitcher.Type.EDITOR
			SceneManager.Instance.switch_to(1); // WORLD
	}

	public void m_StartRebellion(string a_country, string a_count)
	{
		var parentCountry = CountryRequired(a_country);
		var cityIds = MapArray("country_to_cities", a_country).Duplicate();
		var numRebels = Mathf.Min(ParseInt(a_count), cityIds.Count);

		if (numRebels <= 0)
		{
			ConsoleLine("No cities found in " + a_country + " to start rebellion.");
			return;
		}

		var seeds = new Godot.Collections.Array<int>();
		var names = new Godot.Collections.Array<string>();

		cityIds.Shuffle();
		for (var i = 0; i < numRebels; i++)
		{
			var cityId = (int)cityIds[cityIds.Count - 1].AsInt64();
			cityIds.RemoveAt(cityIds.Count - 1);
			seeds.Add(cityId);

			var cityName = ProvinceAt(cityId)?.city ?? "";
			// Bare `name` in the GDScript original resolved to Node.name.
			var nodeName = Name.ToString();
			switch ((int)(GD.Randi() % 3u))
			{
				case 0:
					cityName = prefixes.PickRandom() + "_" + nodeName;
					break;
				case 1:
					cityName = prefixes.PickRandom() + "_" + nodeName + "_" + suffixes.PickRandom();
					break;
				case 2:
					cityName = nodeName + "_" + suffixes.PickRandom();
					break;
			}
			names.Add(cityName);
		}

		var totalProvinces = MapArray("country_to_provinces", a_country).Count;
		var targetPerRebel = totalProvinces / (numRebels + 1);

		var rebels = MultiFill(seeds, names, targetPerRebel * numRebels);

		foreach (var rebel in rebels)
			WarManager.Instance.declare_war(parentCountry, rebel);

		for (var i = 0; i < rebels.Count; i++)
		{
			for (var j = i + 1; j < rebels.Count; j++)
				WarManager.Instance.declare_war(rebels[i], rebels[j]);
		}
	}

	public void m_PartitionCountry(string a_country, string a_count)
	{
		if (!CountryManager.Instance.countries.ContainsKey(a_country))
		{
			ConsoleLine("Unknown country: " + a_country);
			return;
		}

		var numParts = ParseInt(a_count);
		if (numParts <= 1)
		{
			ConsoleLine("Parts must be > 1");
			return;
		}

		var countryProvinces = MapArray("country_to_provinces", a_country);
		var cityIds = MapArray("country_to_cities", a_country).Duplicate();

		var seeds = new Godot.Collections.Array<int>();
		var names = new Godot.Collections.Array<string>();

		cityIds.Shuffle();
		while (seeds.Count < numParts && cityIds.Count > 0)
		{
			var last = cityIds.Count - 1;
			seeds.Add((int)cityIds[last].AsInt64());
			cityIds.RemoveAt(last);
		}

		if (seeds.Count < numParts)
		{
			var available = countryProvinces.Duplicate();
			available.Shuffle();
			while (seeds.Count < numParts && available.Count > 0)
			{
				var last = available.Count - 1;
				var p = available[last].AsInt64();
				available.RemoveAt(last);
				if (!seeds.Contains((int)p))
					seeds.Add((int)p);
			}
		}

		for (var i = 0; i < seeds.Count; i++)
			names.Add(a_country + "_" + (i + 1).ToString(CultureInfo.InvariantCulture));

		MultiFill(seeds, names, countryProvinces.Count);
	}

	public void m_StartRevolution(string a_pid, string a_name, string a_count)
	{
		var pid = ParseInt(a_pid);
		var country = ProvinceAt(pid)?.country ?? "";
		QuickFill(pid, a_name, ParseInt(a_count));
		var rebel = CountryRequired(a_name);
		var original = CountryRequired(country);
		WarManager.Instance.declare_war(rebel, original);
		MapManager.Instance.AllowPids(rebel, original);
	}

	public CountryData QuickFill(int a_pid, string a_name, int a_count)
	{
		var results = MultiFill(
			new Godot.Collections.Array<int> { a_pid },
			new Godot.Collections.Array<string> { a_name },
			a_count
		);
		return results.Count > 0 ? results[0] : null;
	}

	public Godot.Collections.Array<CountryData> MultiFill(Godot.Collections.Array<int> a_seeds, Godot.Collections.Array<string> a_names, int a_total_count)
	{
		if (a_seeds.Count == 0)
			return new Godot.Collections.Array<CountryData>();

		var parentCountryName = ProvinceAt(a_seeds[0])?.country ?? "";
		var results = new Godot.Collections.Array<CountryData>();

		var assignments = new Godot.Collections.Array();
		var frontiers = new Godot.Collections.Array();
		var allTaken = new Godot.Collections.Dictionary();

		for (var i = 0; i < a_seeds.Count; i++)
		{
			var seed = a_seeds[i];
			var assignment = new Godot.Collections.Array();
			assignment.Add(seed);
			assignments.Add(assignment);
			allTaken[seed] = true;

			var frontier = new Godot.Collections.Array();
			foreach (var opt in Neighbors(seed))
			{
				if ((ProvinceAt(opt)?.country ?? "") == parentCountryName && !allTaken.ContainsKey(opt))
					frontier.Add(opt);
			}
			frontiers.Add(frontier);
		}

		var remainingCount = a_total_count - a_seeds.Count;
		var tries = a_total_count * 2;

		while (remainingCount > 0 && tries > 0)
		{
			tries -= 1;
			var expandedAny = false;

			for (var i = 0; i < a_seeds.Count; i++)
			{
				if (remainingCount <= 0)
					break;

				var frontier = frontiers[i].AsGodotArray();
				if (frontier.Count == 0)
					continue;

				var choiceIdx = (int)(GD.Randi() % (uint)frontier.Count);
				var choice = (int)frontier[choiceIdx].AsInt64();
				frontier.RemoveAt(choiceIdx);

				if (allTaken.ContainsKey(choice))
					continue;

				assignments[i].AsGodotArray().Add(choice);
				allTaken[choice] = true;
				remainingCount -= 1;
				expandedAny = true;

				foreach (var opt in Neighbors(choice))
				{
					if ((ProvinceAt(opt)?.country ?? "") == parentCountryName && !allTaken.ContainsKey(opt) && !frontier.Contains(opt))
						frontier.Add(opt);
				}
			}

			if (!expandedAny)
				break;
		}

		// Instantiate countries
		for (var i = 0; i < a_seeds.Count; i++)
		{
			var color = new Color((float)GD.Randf(), (float)GD.Randf(), (float)GD.Randf()).ToHtml(false).ToUpperInvariant();
			var countryData = new Godot.Collections.Dictionary
			{
				{ "name", a_names[i] },
				{ "color", "#" + color },
				{ "money", 10000 },
				{ "ideology", new Godot.Collections.Array { 0, 0 } },
				{ "political_power", 100 },
				{ "stability", 0.5 },
				{ "war_support", 1.5 },
				{ "puppets", new Godot.Collections.Array() },
				{ "accepted_cultures", new Godot.Collections.Array() },
				{ "hostedGovernments", new Godot.Collections.Array() },
				{ "figures", new Godot.Collections.Array() },
			};
			var claims = new System.Collections.Generic.List<int>();
			foreach (var pid in assignments[i].AsGodotArray())
				claims.Add((int)pid.AsInt64());

			var country = MapManager.Instance.InstantiateCountryFromProvinces(countryData, claims.ToArray()) as CountryData;
			results.Add(country);
		}

		return results;
	}

	public void m_RandomFigure(string country, string budget)
	{
		var figure = ImportantFigure.FromRandom(country, ParseInt(budget));
		var message = $"{country} {figure.name}";
		ConsoleInfo(message);
		GD.Print(message);
		var map = MapManager.Instance as MapManager;
		if (map != null)
			map.significantFigures[figure.name] = figure;
		CountryManager.Instance.GetCountry(country)?.Get("figures").AsGodotArray().Add(figure.name);
	}

	public void _show_releasables_country(string country)
	{
		var releasables = MapManager.Instance.GetAllReleasables(country);
		var names = new Godot.Collections.Array<string>();
		foreach (var value in releasables)
		{
			var data = value;
			names.Add(data.ContainsKey("country") ? data["country"].AsString() : "");
		}
		ConsoleInfo(Json.Stringify(names));
	}

	public void _release_country(string country)
	{
		MapManager.Instance.ReleaseCountry(country);
	}

	public void _add_pp(string amount)
	{
		var player = Player();
		if (player == null)
			return;
		player.Set("political_power", player.Get("political_power").AsDouble() + ParseFloat(amount));
	}

	public void _set_ideology(string x, string y)
	{
		Player()?.Set("ideology", new Vector2(ParseInt(x), ParseInt(y)));
	}

	public void _add_manpower(string amount)
	{
		var player = Player();
		if (player == null)
			return;
		player.Set("manpower", player.Get("manpower").AsInt32() + ParseInt(amount));
	}

	public void _set_manpower(string amount)
	{
		Player()?.Set("manpower", ParseInt(amount));
	}

	public void _peace_treaty(string country)
	{
		var player = Player();
		WarManager.Instance._handle_total_collapse(country, player == null ? "" : CountryData.NameOf(player));
	}

	public void _annex(string annexer, string annexee)
	{
		if (CountryManager.Instance.countries.ContainsKey(annexee))
		{
			MapManager.Instance.AnnexCountry(annexer, annexee);
			CountryManager.Instance.cleanup_empty_countries();
			return;
		}

		ConsoleLine("Unknown country: " + annexee);
	}

	public void _play_country(string country_name)
	{
		if (CountryManager.Instance.countries.ContainsKey(country_name))
		{
			CountryManager.Instance.set_player_country(country_name);
			return;
		}

		ConsoleLine("Unknown country: " + country_name);
	}

	public void _start_war(string country_name1, string country_name2)
	{
		var country1 = CountryOrNull(country_name1);
		var country2 = CountryOrNull(country_name2);

		if (country1 != null && country2 != null)
		{
			WarManager.Instance.declare_war(country1, country2);
			return;
		}

		if (country1 == null)
			ConsoleLine("Unknown country: " + country_name1);
		if (country2 == null)
			ConsoleLine("Unknown country: " + country_name2);
	}

	public void m_AddAccess(string country_name1, string country_name2)
	{
		var country1 = CountryOrNull(country_name1);

		if (country1 != null)
		{
			var allowed = country1.Get("allowedCountries").AsGodotArray();
			if (allowed != null && !allowed.Contains(country_name2))
			{
				allowed.Add(country_name2);
				return;
			}
		}

		if (country1 == null)
			ConsoleLine("Unknown country: " + country_name1);
		if (!CountryManager.Instance.countries.ContainsKey(country_name2))
			ConsoleLine("Unknown country: " + country_name2);
	}

	public void m_StartWarSilent(string country_name1, string country_name2)
	{
		var country1 = CountryOrNull(country_name1);
		var country2 = CountryOrNull(country_name2);

		if (country1 != null && country2 != null)
		{
			WarManager.Instance.declare_war(country1, country2, true);
			return;
		}

		if (country1 == null)
			ConsoleLine("Unknown country: " + country_name1);
		if (country2 == null)
			ConsoleLine("Unknown country: " + country_name2);
	}

	public void _call_to_arms(string caller_name, string target_name)
	{
		var caller = CountryOrNull(caller_name);
		var target = CountryOrNull(target_name);

		if (caller != null && target != null)
		{
			WarManager.Instance.call_to_arms(caller, target);
			return;
		}

		if (caller == null)
			ConsoleLine("Unknown country: " + caller_name);
		if (target == null)
			ConsoleLine("Unknown country: " + target_name);
	}

	public void _reload_decisions()
	{
		// C# DecisionManager exposes the formerly-private loader as load_decisions_from_path.
		DecisionManager.Instance.load_decisions_from_path("res://starts/" + GameState.Instance.current_start + "/decisions/");
	}

	public void _debug_decisions()
	{
		var settings = SettingsManager.Instance.settings;
		// Faithful to the original, which called SettingsManager.get_value(...).
		var value = SettingsManager.Instance.settings.GetValue("Developer Options", "debug_mode");
		settings?.Call("set_value", "Developer Options", "debug_mode", value);
	}

	public void _puppet_country(string country_name)
	{
		CountryManager.Instance.make_puppet(Player(), CountryRequired(country_name));
	}

	public void _invite_country(string country_name)
	{
		FactionManager.Instance.invite_faction(Player(), CountryRequired(country_name));
	}

	public void _drew_durnil_mode()
	{
		var player = Player();
		if (player != null)
			player.Set("is_player", false);
	}

	public void _instabuild()
	{
		var value = !GameState.Instance.instabuild;
		GameState.Instance.instabuild = value;
		ConsoleLine("Instabuild: " + (value ? "true" : "false"));
	}

	public void _sink_rise(string pid, string type, string country_name)
	{
		GD.Print("skibidi");
		MapManager.Instance.ChangeProvinceTypes(new Godot.Collections.Array { ParseInt(pid) }, ParseInt(type), country_name);
	}

	public void _set_attr(string country, string attr, Variant val)
	{
		CountryRequired(country).Set(attr, val);
	}

	// --- Small helpers for the dynamic GDScript/C# objects used by this manager ---

	private static int ParseInt(string value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

	private static float ParseFloat(string value)
		=> float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0f;

	private static void ConsoleLine(string text) => GameConsole.Line(text);

	private static void ConsoleInfo(string text) => GameConsole.Info(text);

	private static GodotObject Player() => CountryManager.Instance.player_country;

	private static GodotObject CountryOrNull(string name)
	{
		var countries = CountryManager.Instance.countries;
		return countries.ContainsKey(name) ? countries[name].AsGodotObject() : null;
	}

	private static GodotObject CountryRequired(string name) => CountryManager.Instance.GetCountry(name);

	private static Godot.Collections.Array MapArray(string property, string key)
	{
		var map = MapManager.Instance;
		List<int> ids = property switch
		{
			"country_to_cities" when map.country_to_cities.TryGetValue(key, out var cities) => cities,
			"country_to_provinces" when map.country_to_provinces.TryGetValue(key, out var provinces) => provinces,
			_ => null
		};
		var result = new Godot.Collections.Array();
		if (ids != null)
			foreach (var id in ids)
				result.Add(id);
		return result;
	}

	private static Province ProvinceAt(int pid)
	{
		var provinces = MapManager.Instance.province_objects;
		return provinces.TryGetValue(pid, out var province) ? province : null;
	}

	private static Godot.Collections.Array<int> Neighbors(int pid)
	{
		var graph = MapManager.Instance.province_graph;
		var result = new Godot.Collections.Array<int>();
		if (graph == null || !GodotObject.IsInstanceValid(graph))
			return result;
		foreach (var id in graph.Call("get_point_connections", (long)pid).AsInt64Array())
			result.Add((int)id);
		return result;
	}

	private static int SceneType() => SceneManager.Instance?._current_type ?? -1;
}
