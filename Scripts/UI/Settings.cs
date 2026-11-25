using Godot;

namespace Sosr;

public partial class Settings : Control
{
	private readonly PackedScene SAVE_ENTRY_SCENE = GD.Load<PackedScene>("res://Scenes/SaveEntry.tscn");

	[Export] public VBoxContainer save_list;
	[Export] public TextEdit line_edit;
	[Export] public Button saveButton;

	private enum Section { SAVE, AUDIO, GRAPHICS, EXIT }

	[Export] public TabContainer tab_container;
	[Export] public Button save_settings;
	[Export] public GridContainer graphics_path;

	[ExportGroup("Audio")]
	[Export] public HSlider sfx_slider;
	[Export] public HSlider music_slider;

	[ExportGroup("UI")]
	[Export] public HSlider ui_upper_slider;
	[Export] public HSlider ui_lower_slider;
	[Export] public HSlider ui_dirt_slider;

	[ExportGroup("Map Effects")]
	[Export] public HSlider scanlines_slider;
	[Export] public HSlider vignette_lower_slider;
	[Export] public HSlider vignette_upper_slider;
	[Export] public HSlider provinceBorderThicknessSlider;
	[Export] public HSlider province_borders_slider;
	[Export] public HSlider daynight_contrast_slider;
	[Export] public HSlider daynight_smoothness_slider;
	[Export] public HSlider clouds_slider;
	[Export] public HSlider edgeness_slider;

	[Export] public CheckButton map_effects_btn;
	[Export] public CheckButton debug_mode_btn;

	private static ConfigFile Config => SettingsManager.Instance.settings;

	private void _initialize_ui_values()
	{
		var s = Config;
		sfx_slider.Value = s.GetValue("Sound", "sfx_volume").AsDouble();
		music_slider.Value = s.GetValue("Sound", "music_volume").AsDouble();
		scanlines_slider.Value = s.GetValue("Graphics", "scanlines").AsDouble();
		vignette_lower_slider.Value = s.GetValue("Graphics", "vignette_lower").AsDouble();
		vignette_upper_slider.Value = s.GetValue("Graphics", "vignette_upper").AsDouble();
		map_effects_btn.ButtonPressed = s.GetValue("Graphics", "map_effects").AsDouble() > 0.0;
		graphics_path.Visible = map_effects_btn.ButtonPressed;
		provinceBorderThicknessSlider.Value = s.GetValue("Graphics", "provinceBorderThickness").AsDouble();
		province_borders_slider.Value = s.GetValue("Graphics", "province_borders").AsDouble();
		ui_upper_slider.Value = s.GetValue("Graphics", "ui_upper").AsDouble();
		ui_lower_slider.Value = s.GetValue("Graphics", "ui_lower").AsDouble();
		ui_dirt_slider.Value = s.GetValue("Graphics", "ui_dirt").AsDouble();
		daynight_contrast_slider.Value = s.GetValue("Graphics", "daynight_contrast").AsDouble();
		daynight_smoothness_slider.Value = s.GetValue("Graphics", "daynight_smoothness").AsDouble();
		clouds_slider.Value = s.GetValue("Graphics", "clouds").AsDouble();
		edgeness_slider.Value = s.GetValue("Graphics", "clouds_edgeness").AsDouble();
		debug_mode_btn.ButtonPressed = s.GetValue("Developer Options", "debug_mode").AsBool();
	}

	// Set a value in the ConfigFile, optionally apply it, then persist.
	private static void _set_setting(string section, string key, Variant value, bool apply = true)
	{
		var manager = SettingsManager.Instance;
		manager.settings.SetValue(section, key, value);
		if (apply)
			manager.apply_settings(manager.settings);
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
		Config.Save(SettingsManager.Instance.SETTINGS_FILE);
	}

	private void _refresh_saves()
	{
		if (save_list == null)
			return;
		foreach (var n in save_list.GetChildren())
			n.QueueFree();

		const string path = "res://saves/";
		if (!DirAccess.DirExistsAbsolute(path))
			DirAccess.MakeDirRecursiveAbsolute(path);

		var files = DirAccess.GetFilesAt(path);
		var found_any = false;

		GD.Print("Settings UI: Scanning for saves in: ", ProjectSettings.GlobalizePath(path));

		foreach (var file_name in files)
		{
			if (!file_name.EndsWith(".json"))
				continue;
			// Skip empty names or hidden system files
			if (file_name == ".json" || file_name.StartsWith("."))
				continue;

			found_any = true;
			_add_save_row(save_list, file_name.Replace(".json", ""), path + file_name);
		}

		if (!found_any)
		{
			GD.Print("Settings UI: No JSON saves found.");
			var lbl = new Label { Text = "No save files found.", Modulate = new Color(0.5f, 0.5f, 0.5f) };
			save_list.AddChild(lbl);
		}
	}

	public void _on_sfx_changed(float value) => _set_setting("Sound", "sfx_volume", value);

	public void _on_music_changed(float value) => _set_setting("Sound", "music_volume", value);

	private void _add_save_row(Node parent, string save_name, string file_path)
	{
		// Validate scenario path before allowing load
		var scenario_exists = true;
		var scenario_path = "";

		// Release the parsed save now instead of leaving it to the .NET finalizer.
		using var data = MapData.Instance.LoadJSON(file_path);
		if (data.VariantType == Variant.Type.Dictionary)
		{
			using var dict = data.AsGodotDictionary();
			scenario_path = dict.TryGetValue("scenario_path", out var sp)
				? sp.AsString()
				: "res://starts/ModernDay/map_data.json";
			if (!FileAccess.FileExists(scenario_path))
				scenario_exists = false;
		}

		var entry = SAVE_ENTRY_SCENE.Instantiate<SaveEntry>();
		parent.AddChild(entry);
		entry.Setup(save_name, scenario_exists, scenario_path);
		entry.load_requested += _on_save_load_requested;
		entry.delete_requested += _on_save_delete_requested;
	}

	private void _on_save_load_requested(string save_name)
	{
		var world = GameState.Instance.current_world;
		if (world != null)
		{
			world.load_game(save_name);
		}
		else
		{
			// We are in the Main Menu
			GameState.Instance.pending_load_save = save_name;
			SceneManager.Instance.switch_to(SceneManager.TypeWorld);
		}
	}

	private void _on_save_delete_requested(string save_name)
	{
		DirAccess.RemoveAbsolute("res://saves/" + save_name + ".json");
		_refresh_saves();
	}

	public void _on_save_game_pressed()
	{
		GameState.Instance.current_world.SaveGame(line_edit.Text.StripEdges());
		_refresh_saves();
	}

	public void _on_scanlines_changed(float value) => _set_setting("Graphics", "scanlines", value);

	public void _on_vignette_upper_changed(float value) => _set_setting("Graphics", "vignette_upper", value);

	public void _on_vignette_lower_changed(float value) => _set_setting("Graphics", "vignette_lower", value);

	public void _on_map_effects_pressed()
	{
		var enabled = map_effects_btn.ButtonPressed;
		graphics_path.Visible = enabled;
		_set_setting("Graphics", "map_effects", enabled ? 1.0 : 0.0);
	}

	public void m_OnProvinceBorderThicknessChanged(float value) => _set_setting("Graphics", "provinceBorderThickness", value);

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
		SceneManager.Instance.switch_to(SceneManager.TypeMenu);
	}

	public void _on_tab_container_tab_changed(int tab)
	{
		save_settings.Visible = !(tab == (int)Section.EXIT || tab == (int)Section.SAVE);
	}

	private void _update_tabs_visibility()
	{
		if (tab_container != null)
		{
			var is_menu = SceneManager.Instance._current_type == SceneManager.TypeMenu;
			tab_container.SetTabHidden((int)Section.EXIT, is_menu);
		}
	}

	public void m_OnTextEditTextChanged()
	{
		saveButton.Disabled = line_edit.Text.Length == 0;
	}
}
