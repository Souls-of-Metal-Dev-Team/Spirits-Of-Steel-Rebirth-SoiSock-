using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class World : Node2D
{
	[Export] public Shader map_shader;

	public Sprite2D map_sprite;
	public Node map_container;
	public GameClock clock;
	public Camera2D camera;

	public Vector2 water_offset = Vector2.Zero;

	public Image type_img;

	public ShaderMaterial mat;

	private void EnsureNodeRefs()
	{
		if (map_sprite == null || !GodotObject.IsInstanceValid(map_sprite))
			map_sprite = GetNodeOrNull<Sprite2D>("../../MapContainer/CultureSprite");
		if (map_container == null || !GodotObject.IsInstanceValid(map_container))
			map_container = GetNodeOrNull<Node>("../../MapContainer");
	}

	public override void _Process(double delta)
	{
		EnsureNodeRefs();
		if (map_sprite == null || MapManager.Instance.id_map_image == null)
			return;

		var material = map_sprite.Material as ShaderMaterial;
		if (material != null && clock != null && !clock.paused)
		{
			var move_amount = clock.time_scale * 0.001f * (float)delta;
			water_offset.X += move_amount;
			material.SetShaderParameter("ocean_offset", water_offset);
			material.SetShaderParameter("absolute_days", clock.get_absolute_days());
		}
	}

	public override void _EnterTree()
	{
		GameState.Instance.current_world = this;
		clock = GetNodeOrNull<GameClock>("/root/Main/Clock");
		if (clock != null)
			clock.FromDict(GameState.Instance.clock_cache);
		KeyboardManager.Instance.settings = GetNodeOrNull<CanvasLayer>("/root/Main/SettingsLayer");
		TroopManager.Instance.troop_selection = GetNodeOrNull<TroopSelection>("TroopSelection");
		camera = GetNodeOrNull<Camera2D>("/root/Main/Camera2D");
		// TODO(pol): Load CountryManager after map instead of an autoload to avoid this.
	}

	public void DoSetup(Godot.Collections.Array a_progress)
	{
		EnsureNodeRefs();

		var path = $"res://starts/{GameState.Instance.current_start}";
		var save_state = new Godot.Collections.Dictionary();

		var pending_save = GameState.Instance.pending_load_save;
		if (pending_save != "")
		{
			var save_path = "res://saves/" + pending_save + ".json";
			if (FileAccess.FileExists(save_path))
			{
				save_state = MapData.Instance.LoadSaveState(save_path);
				if (save_state.Count > 0)
				{
					var scenario_path = save_state.ContainsKey("scenario_path")
						? save_state["scenario_path"].AsString()
						: "";
					if (scenario_path != "")
					{
						path = MapData.Instance.ScenarioDirFromPath(scenario_path);
						GameState.Instance.current_start = path.GetFile();
					}
					GameState.Instance.is_loading_game = true;
					GD.Print("World: Pending save found. Loading from: ", pending_save);
				}
				else
				{
					GD.PushError("World: Failed to load save state from: " + save_path);
				}
			}
		}

		a_progress[0] = 0.05;

		// NOTE(soi): this is here bcuz sometimes main menu is used and im too lazy to comment this out
		if (MapManager.Instance.province_objects.Count == 0 ||
			GameState.Instance.is_loading_game)
		{
			MapData.Instance.LoadMapData(path, a_progress, save_state);
		}

		if (GameState.Instance.is_loading_game)
		{
			GameState.Instance.is_loading_game = false;
			GameState.Instance.pending_load_save = "";
		}
		a_progress[0] = 0.2;

		GD.Print("World: Map is ready -> configuring visuals...");

		MapManager.Instance.all_cities = MapManager.Instance.GetAllCities();
		a_progress[0] = 0.21;

		if (CountryManager.Instance.player_country == null)
		{
			// CountryManager.set_player_country(CountryManager.countries.keys().pick_random())
			GameState.Instance.interactionType = GameState.InteractionType.SELECT_COUNTRY;
			// NOTE(soi): wait so like when i click on a country this should magically play as it?????. damm
		}
		// For debugging purposes. Create some troops first
		a_progress[0] = 0.30;
		MapManager.Instance._BuildGlobalRegistry();
		a_progress[0] = 0.35;
		var map_width = MapManager.Instance.map_width;
		var map_height = MapManager.Instance.map_height;

		mat = new ShaderMaterial();
		mat.Shader = map_shader;
		mat.SetShaderParameter("region_id_map",
			ImageTexture.CreateFromImage(MapManager.Instance.id_map_image as Image));
		mat.SetShaderParameter("state_colors", MapManager.Instance.state_color_texture);

		// @warning_ignore("narrowing_conversion")
		type_img = Image.CreateEmpty(map_width, map_height, false, Image.Format.L8);

		GD.Print(map_width, map_height);
		MapManager.Instance._SetTypeMap(mat, type_img);

		var noise = new FastNoiseLite();
		noise.Seed = (int)GD.Randi();
		noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
		noise.Frequency = 0.005f;
		noise.FractalType = FastNoiseLite.FractalTypeEnum.Ridged;
		noise.FractalOctaves = 5;
		noise.FractalLacunarity = 3.0f;
		noise.FractalGain = 0.5f;

		var noise_tex = new NoiseTexture2D();
		noise_tex.Seamless = true;
		noise_tex.Width = 512;
		noise_tex.Height = 512;
		noise_tex.Noise = noise;

		// GDScript: await noise_tex.changed
		noise_tex.Connect("changed",
			Callable.From(() => FinishSetupAfterNoise(a_progress, map_width, map_height, noise_tex)),
			(uint)GodotObject.ConnectFlags.OneShot);
	}

	private void FinishSetupAfterNoise(Godot.Collections.Array a_progress, int map_width, int map_height, NoiseTexture2D noise_tex)
	{
		EnsureNodeRefs();

		mat.SetShaderParameter("ocean_noise", noise_tex);
		mat.SetShaderParameter("original_texture", map_sprite.Texture);
		mat.SetShaderParameter("sea_speed", 0.00); // Changed by MainClock
		mat.SetShaderParameter("tex_size", new Vector2(map_width, map_height));
		mat.SetShaderParameter("country_border_color", Colors.Black);

		map_sprite.Material = mat;
		a_progress[0] = 0.9;

		if (map_container != null)
		{
			map_container.Set("map_width", map_width);
		}
		else
		{
			GD.PushError("MapContainer node not found!");
		}

		SettingsManager.Instance.apply_settings(SettingsManager.Instance.settings);

		a_progress[0] = 1.0;

		if (clock != null)
		{
			clock.Connect(GameClock.SignalName.HourPassed,
				Callable.From(() => CountryManager.Instance._on_hour_passed()));
			clock.Connect(GameClock.SignalName.DayPassed,
				Callable.From(() => CountryManager.Instance._on_day_passed()));
			clock.Connect(GameClock.SignalName.DayPassed,
				Callable.From(() => EventManager.Instance._on_day_passed()));
		}

		_refresh_map_visuals();
	}

	public void _refresh_map_visuals()
	{
		var state_color_texture = MapManager.Instance.state_color_texture;
		if (mat != null && state_color_texture != null)
			mat.SetShaderParameter("state_colors", state_color_texture);
	}

	public void SaveGame(string slot)
	{
		// Construct a unified save dictionary
		var clock_dict = clock != null ? clock.ToDict() : new Godot.Collections.Dictionary();
		var save_data = new Godot.Collections.Dictionary
		{
			{ "clock", clock_dict },
			{ "resources", MapManager.Instance.SaveResourcesData() },
			{ "biomes", MapManager.Instance.SaveBiomeData() },
			{ "buildings", MapManager.Instance.SaveBuildingData() },
			{ "provinces", MapManager.Instance.SaveProvinces() },
			{ "polities", CountryManager.Instance.SaveCountries() },
			{ "ideologies", IdeologyManager.Instance.ideologies },
			{ "factions", FactionManager.Instance.SaveFactions() },
			{ "wars", WarManager.Instance.save_wars() },
			{ "original_territories", WarManager.Instance.save_original_territories() },
			{ "scheduled_events", EventManager.Instance.save_events() },
		};

		// Store only what changed since the start; the start is loaded again on load.
		// Normalize both sides through JSON so ints in live state match floats parsed
		// from the start files. Otherwise every number looks changed.
		var scenario_path = $"res://starts/{GameState.Instance.current_start}/map_data.json";
		var start_state = MapData.Instance.LoadStartState(
			MapData.Instance.ScenarioDirFromPath(scenario_path));
		if (start_state.Count == 0)
		{
			GD.PushError("Save failed! Could not load start state: " + scenario_path);
			return;
		}

		var baseline = Json.ParseString(Json.Stringify(start_state)).AsGodotDictionary();
		var current = Json.ParseString(Json.Stringify(save_data)).AsGodotDictionary();
		var save_file_data = new Godot.Collections.Dictionary
		{
			{ "scenario_path", scenario_path },
			{ "diff", SettingsManager.Instance.diff(baseline, current) },
		};

		// Ensure directory exists
		using var dir = DirAccess.Open("res://");
		if (dir != null && !dir.DirExists("saves/"))
			dir.MakeDirRecursive("saves/");

		var path = "res://saves/" + slot + ".json";
		if (MapData.Instance.SaveJSON(path, save_file_data))
			GD.Print("Game State saved successfully to: ", ProjectSettings.GlobalizePath(path));
	}

	public void load_game(string save_name)
	{
		var path = "res://saves/" + save_name + ".json";

		if (!FileAccess.FileExists(path))
		{
			GD.PushError("Save file not found: " + path);
			return;
		}

		// Diff saves are merged with their start; legacy full saves are used as-is.
		var save_data = MapData.Instance.LoadSaveState(path);
		if (save_data.Count == 0)
		{
			GD.PushError("Failed to load save state: " + path);
			return;
		}

		var scenario_path = save_data.ContainsKey("scenario_path")
			? save_data["scenario_path"].AsString()
			: $"res://starts/{GameState.Instance.current_start}/map_data.json";
		var start_dir = MapData.Instance.ScenarioDirFromPath(scenario_path);

		// Pause systems that react to state
		if (map_container != null)
			map_container.SetProcess(false);

		// Full State Reset
		TroopManager.Instance.clear_all_troops();
		WarManager.Instance.reset_state();

		// Close Peace UI if open
		var peace_ui = GetTree().Root.FindChild("PeaceProcessUI", true, false);
		if (peace_ui != null && peace_ui.HasMethod("force_close"))
			peace_ui.Call("force_close");
		GameState.Instance.in_peace_process = false;

		GameState.Instance.current_start = start_dir.GetFile();
		GameState.Instance.is_loading_game = true;
		MapData.Instance.LoadMapData(start_dir, new Godot.Collections.Array { 0.0 }, save_data);
		GameState.Instance.is_loading_game = false;
		_refresh_map_visuals();

		if (map_container != null)
			map_container.SetProcess(true);

		GD.Print("Game loaded successfully:", save_name);
	}
}
