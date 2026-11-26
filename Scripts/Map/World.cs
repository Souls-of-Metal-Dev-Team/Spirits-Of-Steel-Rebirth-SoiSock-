using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class World : Node2D
{
	[Export] public Shader mapShader;

	public Sprite2D mapSprite;
	public Node mapContainer;
	public GameClock clock;
	public Camera2D camera;

	public Vector2 waterOffset = Vector2.Zero;

	public Image typeImg;

	public ShaderMaterial mat;

	private bool _clockConnected;

	private void EnsureNodeRefs()
	{
		if (mapSprite == null || !GodotObject.IsInstanceValid(mapSprite))
			mapSprite = GetNodeOrNull<Sprite2D>("../../MapContainer/CultureSprite");
		if (mapContainer == null || !GodotObject.IsInstanceValid(mapContainer))
			mapContainer = GetNodeOrNull<Node>("../../MapContainer");
	}

	public override void _Process(double delta)
	{
		EnsureNodeRefs();
		if (mapSprite == null || MapManager.Instance.idMapImage == null)
			return;

		var material = mapSprite.Material as ShaderMaterial;
		if (material != null && clock != null && !clock.paused)
		{
			var moveAmount = clock.timeScale * 0.001f * (float)delta;
			waterOffset.X += moveAmount;
			material.SetShaderParameter("ocean_offset", waterOffset);
			material.SetShaderParameter("absolute_days", clock.GetAbsoluteDays());
		}
	}

	public override void _EnterTree()
	{
		GameState.Instance.currentWorld = this;
		clock = GetNodeOrNull<GameClock>("/root/Main/Clock");
		if (clock != null)
			clock.FromDict(GameState.Instance.clockCache);
		KeyboardManager.Instance.settings = GetNodeOrNull<CanvasLayer>("/root/Main/SettingsLayer");
		TroopManager.Instance.troopSelection = GetNodeOrNull<TroopSelection>("TroopSelection");
		camera = GetNodeOrNull<Camera2D>("/root/Main/Camera2D");
		// TODO(pol): Load CountryManager after map instead of an autoload to avoid this.
	}

	public void DoSetup(Godot.Collections.Array aProgress)
	{
		EnsureNodeRefs();

		var path = $"res://starts/{GameState.Instance.currentStart}";
		var saveState = new Godot.Collections.Dictionary();

		var pendingSave = GameState.Instance.pendingLoadSave;
		if (pendingSave != "")
		{
			var savePath = "user://saves/" + pendingSave + ".json";
			if (FileAccess.FileExists(savePath))
			{
				saveState = MapData.Instance.LoadSaveState(savePath);
				if (saveState.Count > 0)
				{
					var scenarioPath = saveState.ContainsKey("scenario_path")
						? saveState["scenario_path"].AsString()
						: "";
					if (scenarioPath != "")
					{
						path = MapData.Instance.ScenarioDirFromPath(scenarioPath);
						GameState.Instance.currentStart = path.GetFile();
					}
					GameState.Instance.isLoadingGame = true;
					GD.Print("World: Pending save found. Loading from: ", pendingSave);
				}
				else
				{
					GD.PushError("World: Failed to load save state from: " + savePath);
				}
			}
		}

		aProgress[0] = 0.05;

		// NOTE(soi): this is here bcuz sometimes main menu is used and im too lazy to comment this out
		var otherStart = MapData.Instance.loadedStart != "" && MapData.Instance.loadedStart != path;
		if (MapManager.Instance.provinceObjects.Count == 0 ||
			GameState.Instance.isLoadingGame || otherStart)
		{
			// Same reset as load_game: the last game's troops and wars would survive a reload.
			TroopManager.Instance.ClearAllTroops();
			WarManager.Instance.ResetState();
			MapData.Instance.LoadMapData(path, aProgress, saveState);
		}

		if (GameState.Instance.isLoadingGame)
		{
			GameState.Instance.isLoadingGame = false;
			GameState.Instance.pendingLoadSave = "";
		}
		aProgress[0] = 0.2;

		GD.Print("World: Map is ready -> configuring visuals...");

		MapManager.Instance.allCities = MapManager.Instance.GetAllCities();
		aProgress[0] = 0.21;

		if (CountryManager.Instance.playerCountry == null)
		{
			GameState.Instance.interactionType = GameState.InteractionType.SELECT_COUNTRY;
			// NOTE(soi): wait so like when i click on a country this should magically play as it?????. damm
		}
		// For debugging purposes. Create some troops first
		aProgress[0] = 0.30;
		MapManager.Instance._BuildGlobalRegistry();
		aProgress[0] = 0.35;
		var mapWidth = MapManager.Instance.mapWidth;
		var mapHeight = MapManager.Instance.mapHeight;

		mat = new ShaderMaterial();
		mat.Shader = mapShader;
		mat.SetShaderParameter("region_id_map",
			ImageTexture.CreateFromImage(MapManager.Instance.idMapImage as Image));
		mat.SetShaderParameter("state_colors", MapManager.Instance.stateColorTexture);

		// @warning_ignore("narrowing_conversion")
		typeImg = Image.CreateEmpty(mapWidth, mapHeight, false, Image.Format.L8);

		GD.Print(mapWidth, mapHeight);
		MapManager.Instance._SetTypeMap(mat, typeImg);

		var noise = new FastNoiseLite();
		noise.Seed = (int)GD.Randi();
		noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
		noise.Frequency = 0.005f;
		noise.FractalType = FastNoiseLite.FractalTypeEnum.Ridged;
		noise.FractalOctaves = 5;
		noise.FractalLacunarity = 3.0f;
		noise.FractalGain = 0.5f;

		var noiseTex = new NoiseTexture2D();
		noiseTex.Seamless = true;
		noiseTex.Width = 512;
		noiseTex.Height = 512;
		noiseTex.Noise = noise;

		// GDScript: await noise_tex.changed
		noiseTex.Connect("changed",
			Callable.From(() => FinishSetupAfterNoise(aProgress, mapWidth, mapHeight, noiseTex)),
			(uint)GodotObject.ConnectFlags.OneShot);
	}

	private void FinishSetupAfterNoise(Godot.Collections.Array aProgress, int mapWidth, int mapHeight, NoiseTexture2D noiseTex)
	{
		EnsureNodeRefs();

		mat.SetShaderParameter("ocean_noise", noiseTex);
		mat.SetShaderParameter("original_texture", mapSprite.Texture);
		mat.SetShaderParameter("sea_speed", 0.00); // Changed by MainClock
		mat.SetShaderParameter("tex_size", new Vector2(mapWidth, mapHeight));
		mat.SetShaderParameter("country_border_color", Colors.Black);

		mapSprite.Material = mat;
		aProgress[0] = 0.9;

		if (mapContainer != null)
		{
			mapContainer.Set("mapWidth", mapWidth);
		}
		else
		{
			GD.PushError("MapContainer node not found!");
		}

		GameState.Instance.ApplySettings(GameState.Instance.settings);

		aProgress[0] = 1.0;

		// The world scene is cached, so DoSetup runs again for every new game; connect once.
		if (clock != null && !_clockConnected)
		{
			_clockConnected = true;
			clock.Connect(GameClock.SignalName.HourPassed,
				Callable.From(() => CountryManager.Instance._on_hour_passed()));
			clock.Connect(GameClock.SignalName.HourPassed,
				Callable.From(() => TroopManager.Instance.OnHourPassed()));
			clock.Connect(GameClock.SignalName.HourPassed,
				Callable.From(() => WarManager.Instance.OnHourPassed()));
			clock.Connect(GameClock.SignalName.DayPassed,
				Callable.From(() => CountryManager.Instance._on_day_passed()));
		}

		_refresh_map_visuals();
	}

	public void _refresh_map_visuals()
	{
		var stateColorTexture = MapManager.Instance.stateColorTexture;
		if (mat != null && stateColorTexture != null)
			mat.SetShaderParameter("state_colors", stateColorTexture);
	}

	public void SaveGame(string slot)
	{
		CountryManager.Instance.FlushDayQueue();

		// Construct a unified save dictionary
		var clockDict = clock != null ? clock.ToDict() : new Godot.Collections.Dictionary();
		var saveData = new Godot.Collections.Dictionary
		{
			{ "clock", clockDict },
			{ "sim_seed", GameState.Instance.SimSeed.ToString() },
			{ "resources", MapManager.Instance.SaveResourcesData() },
			{ "biomes", MapManager.Instance.SaveBiomeData() },
			{ "buildings", MapManager.Instance.SaveBuildingData() },
			{ "provinces", MapManager.Instance.SaveProvinces() },
			{ "polities", CountryManager.Instance.SaveCountries() },
			{ "ideologies", IdeologyManager.Instance.ideologies },
			{ "factions", FactionManager.Instance.SaveFactions() },
			{ "wars", WarManager.Instance.SaveWars() },
			{ "original_territories", WarManager.Instance.SaveOriginalTerritories() },
			{ "scheduled_events", EventManager.Instance.SaveEvents() },
			{ "significant_figures", SaveFigures() },
		};

		// Store only what changed since the start; the start is loaded again on load.
		// Normalize both sides through JSON so ints in live state match floats parsed
		// from the start files. Otherwise every number looks changed.
		var scenarioPath = $"res://starts/{GameState.Instance.currentStart}/map_data.json";
		var startState = MapData.Instance.LoadStartState(
			MapData.Instance.ScenarioDirFromPath(scenarioPath));
		if (startState.Count == 0)
		{
			GD.PushError("Save failed! Could not load start state: " + scenarioPath);
			return;
		}

		var saveFileData = VariantState.ToVariant(
			SaveService.CreateSave(startState.AsState(), saveData.AsState(), scenarioPath));

		DirAccess.MakeDirRecursiveAbsolute("user://saves");
		var path = "user://saves/" + slot + ".json";
		if (MapData.Instance.SaveJSON(path, saveFileData))
			GD.Print("Game State saved successfully to: ", ProjectSettings.GlobalizePath(path));
	}

	private static Godot.Collections.Array SaveFigures() => FigureManager.Instance.Serialize();

	public void LoadGame(string saveName)
	{
		var path = "user://saves/" + saveName + ".json";

		if (!FileAccess.FileExists(path))
		{
			GD.PushError("Save file not found: " + path);
			return;
		}

		// Diff saves are merged with their start; legacy full saves are used as-is.
		var saveData = MapData.Instance.LoadSaveState(path);
		if (saveData.Count == 0)
		{
			GD.PushError("Failed to load save state: " + path);
			return;
		}

		var scenarioPath = saveData.ContainsKey("scenario_path")
			? saveData["scenario_path"].AsString()
			: $"res://starts/{GameState.Instance.currentStart}/map_data.json";
		var startDir = MapData.Instance.ScenarioDirFromPath(scenarioPath);

		// Pause systems that react to state
		if (mapContainer != null)
			mapContainer.SetProcess(false);

		// Full State Reset
		TroopManager.Instance.ClearAllTroops();
		WarManager.Instance.ResetState();

		// Close Peace UI if open
		var peaceUi = GetTree().Root.FindChild("PeaceProcessUI", true, false);
		if (peaceUi != null && peaceUi.HasMethod("ForceClose"))
			peaceUi.Call("ForceClose");
		GameState.Instance.inPeaceProcess = false;

		GameState.Instance.currentStart = startDir.GetFile();
		GameState.Instance.isLoadingGame = true;
		MapData.Instance.LoadMapData(startDir, new Godot.Collections.Array { 0.0 }, saveData);
		GameState.Instance.isLoadingGame = false;
		_refresh_map_visuals();

		if (mapContainer != null)
			mapContainer.SetProcess(true);

		GD.Print("Game loaded successfully:", saveName);
	}
}
