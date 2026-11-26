using Godot;

namespace Sosr;

public partial class Settings : Control
{
	private readonly PackedScene SAVE_ENTRY_SCENE = GD.Load<PackedScene>("res://Scenes/SaveEntry.tscn");

	[Export] public VBoxContainer saveList;
	[Export] public TextEdit lineEdit;
	[Export] public Button saveButton;

	private enum Section { SAVE, AUDIO, GRAPHICS, EXIT }

	[Export] public TabContainer tabContainer;
	[Export] public Button saveSettings;
	[Export] public GridContainer graphicsPath;

	[ExportGroup("Audio")]
	[Export] public HSlider sfxSlider;
	[Export] public HSlider musicSlider;

	[ExportGroup("UI")]
	[Export] public HSlider uiUpperSlider;
	[Export] public HSlider uiLowerSlider;
	[Export] public HSlider uiDirtSlider;

	[ExportGroup("Map Effects")]
	[Export] public HSlider scanlinesSlider;
	[Export] public HSlider vignetteLowerSlider;
	[Export] public HSlider vignetteUpperSlider;
	[Export] public HSlider provinceBorderThicknessSlider;
	[Export] public HSlider provinceBordersSlider;
	[Export] public HSlider daynightContrastSlider;
	[Export] public HSlider daynightSmoothnessSlider;
	[Export] public HSlider cloudsSlider;
	[Export] public HSlider edgenessSlider;

	[Export] public CheckButton mapEffectsBtn;
	[Export] public CheckButton debugModeBtn;

	private static ConfigFile Config => GameState.Instance.settings;

	private void _initialize_ui_values()
	{
		var s = Config;
		sfxSlider.Value = s.GetValue("Sound", "sfx_volume").AsDouble();
		musicSlider.Value = s.GetValue("Sound", "music_volume").AsDouble();
		scanlinesSlider.Value = s.GetValue("Graphics", "scanlines").AsDouble();
		vignetteLowerSlider.Value = s.GetValue("Graphics", "vignette_lower").AsDouble();
		vignetteUpperSlider.Value = s.GetValue("Graphics", "vignette_upper").AsDouble();
		mapEffectsBtn.ButtonPressed = s.GetValue("Graphics", "map_effects").AsDouble() > 0.0;
		graphicsPath.Visible = mapEffectsBtn.ButtonPressed;
		provinceBorderThicknessSlider.Value = s.GetValue("Graphics", "provinceBorderThickness").AsDouble();
		provinceBordersSlider.Value = s.GetValue("Graphics", "province_borders").AsDouble();
		uiUpperSlider.Value = s.GetValue("Graphics", "ui_upper").AsDouble();
		uiLowerSlider.Value = s.GetValue("Graphics", "ui_lower").AsDouble();
		uiDirtSlider.Value = s.GetValue("Graphics", "ui_dirt").AsDouble();
		daynightContrastSlider.Value = s.GetValue("Graphics", "daynight_contrast").AsDouble();
		daynightSmoothnessSlider.Value = s.GetValue("Graphics", "daynight_smoothness").AsDouble();
		cloudsSlider.Value = s.GetValue("Graphics", "clouds").AsDouble();
		edgenessSlider.Value = s.GetValue("Graphics", "clouds_edgeness").AsDouble();
		debugModeBtn.ButtonPressed = s.GetValue("Developer Options", "debug_mode").AsBool();
	}

	// Set a value in the ConfigFile, optionally apply it, then persist.
	private static void _set_setting(string section, string key, Variant value, bool apply = true)
	{
		var manager = GameState.Instance;
		manager.settings.SetValue(section, key, value);
		if (apply)
			manager.ApplySettings(manager.settings);
		manager.settings.Save(manager.SETTINGS_FILE);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationVisibilityChanged && IsVisibleInTree())
		{
			_refresh_saves();
			_update_tabs_visibility();
		}
	}

	public override void _Ready()
	{
		_refresh_saves();
		_initialize_ui_values();
		_update_tabs_visibility();
	}

	public void _on_save_settings_pressed()
	{
		Config.Save(GameState.Instance.SETTINGS_FILE);
	}

	private void _refresh_saves()
	{
		if (saveList == null)
			return;
		foreach (var n in saveList.GetChildren())
			n.QueueFree();

		const string path = "user://saves/";
		if (!DirAccess.DirExistsAbsolute(path))
			DirAccess.MakeDirRecursiveAbsolute(path);

		var files = DirAccess.GetFilesAt(path);
		var foundAny = false;

		GD.Print("Settings UI: Scanning for saves in: ", ProjectSettings.GlobalizePath(path));

		foreach (var fileName in files)
		{
			if (!fileName.EndsWith(".json"))
				continue;
			// Skip empty names or hidden system files
			if (fileName == ".json" || fileName.StartsWith("."))
				continue;

			foundAny = true;
			_add_save_row(saveList, fileName.Replace(".json", ""), path + fileName);
		}

		if (!foundAny)
		{
			GD.Print("Settings UI: No JSON saves found.");
			var lbl = new Label { Text = "No save files found.", Modulate = new Color(0.5f, 0.5f, 0.5f) };
			saveList.AddChild(lbl);
		}
	}

	public void _on_sfx_changed(float value) => _set_setting("Sound", "sfx_volume", value);

	public void _on_music_changed(float value) => _set_setting("Sound", "music_volume", value);

	private void _add_save_row(Node parent, string saveName, string filePath)
	{
		// Validate scenario path before allowing load
		var scenarioExists = true;
		var scenarioPath = "";

		// Release the parsed save now instead of leaving it to the .NET finalizer.
		using var data = MapData.Instance.LoadJSON(filePath);
		if (data.VariantType == Variant.Type.Dictionary)
		{
			using var dict = data.AsGodotDictionary();
			scenarioPath = dict.TryGetValue("scenario_path", out var sp)
				? sp.AsString()
				: "res://starts/ModernDay/map_data.json";
			if (!FileAccess.FileExists(scenarioPath))
				scenarioExists = false;
		}

		var entry = SAVE_ENTRY_SCENE.Instantiate<SaveEntry>();
		parent.AddChild(entry);
		entry.Setup(saveName, scenarioExists, scenarioPath);
		entry.LoadRequested += _on_save_LoadRequested;
		entry.DeleteRequested += _on_save_DeleteRequested;
	}

	private void _on_save_LoadRequested(string saveName)
	{
		var world = GameState.Instance.currentWorld;
		// current_world is never null and stays set while the cached world sits out of the tree.
		if (world != null && world.IsInsideTree())
		{
			world.LoadGame(saveName);
		}
		else
		{
			// We are in the Main Menu
			GameState.Instance.pendingLoadSave = saveName;
			SceneManager.Instance.SwitchTo(SceneManager.TypeWorld);
		}
	}

	private void _on_save_DeleteRequested(string saveName)
	{
		DirAccess.RemoveAbsolute("user://saves/" + saveName + ".json");
		_refresh_saves();
	}

	public void _on_save_game_pressed()
	{
		GameState.Instance.currentWorld.SaveGame(lineEdit.Text.StripEdges());
		_refresh_saves();
	}

	public void _on_scanlines_changed(float value) => _set_setting("Graphics", "scanlines", value);

	public void _on_vignette_upper_changed(float value) => _set_setting("Graphics", "vignette_upper", value);

	public void _on_vignette_lower_changed(float value) => _set_setting("Graphics", "vignette_lower", value);

	public void _on_map_effects_pressed()
	{
		var enabled = mapEffectsBtn.ButtonPressed;
		graphicsPath.Visible = enabled;
		_set_setting("Graphics", "map_effects", enabled ? 1.0 : 0.0);
	}

	public void MOnProvinceBorderThicknessChanged(float value) => _set_setting("Graphics", "provinceBorderThickness", value);

	public void _on_province_borders_changed(float value) => _set_setting("Graphics", "province_borders", value);

	public void _on_ui_upper_changed(float value)
	{
		_set_setting("Graphics", "ui_upper", value, false);
		(Material as ShaderMaterial)?.SetShaderParameter("upper", value);
	}

	public void _on_ui_lower_changed(float value)
	{
		_set_setting("Graphics", "ui_lower", value, false);
		(Material as ShaderMaterial)?.SetShaderParameter("lower", value);
	}

	public void _on_ui_dirt_changed(float value)
	{
		_set_setting("Graphics", "ui_dirt", value, false);
		(Material as ShaderMaterial)?.SetShaderParameter("dirt", value);
	}

	public void _on_daynight_contrast_changed(float value) => _set_setting("Graphics", "daynight_contrast", value);

	public void _on_daynight_smoothness_changed(float value) => _set_setting("Graphics", "daynight_smoothness", value);

	public void _on_clouds_changed(float value) => _set_setting("Graphics", "clouds", value);

	public void _on_edgeness_changed(float value) => _set_setting("Graphics", "clouds_edgeness", value);

	public void _on_debug_mode_pressed()
	{
		var current = Config.GetValue("Developer Options", "debug_mode").AsBool();
		_set_setting("Developer Options", "debug_mode", !current);
	}

	public void _on_exit_start_pressed()
	{
		KeyboardManager.Instance.settings.Visible = false;
		SceneManager.Instance.SwitchTo(SceneManager.TypeMenu);
	}

	public void _on_tab_container_tab_changed(int tab)
	{
		saveSettings.Visible = !(tab == (int)Section.EXIT || tab == (int)Section.SAVE);
	}

	private void _update_tabs_visibility()
	{
		if (tabContainer != null)
		{
			var isMenu = SceneManager.Instance._current_type == SceneManager.TypeMenu;
			tabContainer.SetTabHidden((int)Section.EXIT, isMenu);
		}
	}

	public void MOnTextEditTextChanged()
	{
		saveButton.Disabled = lineEdit.Text.Length == 0;
	}
}
