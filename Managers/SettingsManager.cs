using Godot;
using Sosr;
using System.Collections.Generic;
using System.Linq;

namespace Sosr;

public partial class SettingsManager : Node
{
	public static SettingsManager Instance => Autoload.Get<SettingsManager>("SettingsManager");

	// Instance field rather than a const: GDScript call sites read
	// SettingsManager.SETTINGS_FILE on the autoload instance.
	public string SETTINGS_FILE = "res://settings.json";

	public ConfigFile settings = new ConfigFile();

	public override void _Ready()
	{
		settings.SetValue("Developer Options", "debug_mode", false);

		settings.SetValue("Sound", "radios", MusicManager.Instance.radios);
		settings.SetValue("Sound", "sfx_volume", 0.1);
		settings.SetValue("Sound", "music_volume", 1.0);

		settings.SetValue("Graphics", "scanlines", 0.0);
		settings.SetValue("Graphics", "vignette_lower", 0.0);
		settings.SetValue("Graphics", "vignette_upper", 0.0);
		settings.SetValue("Graphics", "map_effects", 1.0);
		settings.SetValue("Graphics", "provinceBorderThickness", 0.5);
		settings.SetValue("Graphics", "province_borders", 0.8);
		settings.SetValue("Graphics", "ui_upper", 0.8);
		settings.SetValue("Graphics", "ui_lower", 0.8);
		settings.SetValue("Graphics", "ui_dirt", 0.8);
		settings.SetValue("Graphics", "daynight_contrast", 0.01);
		settings.SetValue("Graphics", "daynight_smoothness", 0.01);
		settings.SetValue("Graphics", "clouds", 0.01);
		settings.SetValue("Graphics", "clouds_edgeness", 0.01);

		LoadSettings();
	}

	// Original GDScript method name, kept as an alias of the engine callback.
	public void _ready() => _Ready();

	public void LoadSettings()
	{
		if (FileAccess.FileExists(SETTINGS_FILE))
			settings.Load(SETTINGS_FILE);
		else
			settings.Save(SETTINGS_FILE);
	}

	// Was a static func in GDScript; an instance method is required so that
	// GDScript autoload calls (SettingsManager.apply_settings(...)) still resolve.
	public void ApplySettings(ConfigFile aSettings)
	{
		// Audio
		var music = MusicManager.Instance;
		music.SetSfxVolume(aSettings.GetValue("Sound", "sfx_volume").AsSingle());
		music.SetMusicVolume(aSettings.GetValue("Sound", "music_volume").AsSingle());
		var radios = aSettings.GetValue("Sound", "radios");
		if (radios.VariantType == Variant.Type.Array)
			music.radios = new Godot.Collections.Array<string>(radios.AsGodotArray().Select(r => r.AsString()));

		// Graphics (These require GameState.current_world to be set)
		var world = GameState.Instance.currentWorld;
		if (world == null || !GodotObject.IsInstanceValid(world))
			return;
		var sprite = world.mapSprite;
		if (sprite == null || !GodotObject.IsInstanceValid(sprite))
			return;
		if (sprite.Material is not ShaderMaterial mat)
			return;

		// shader param -> Graphics key
		var shaderMap = new Godot.Collections.Dictionary<string, string>
		{
			{ "scanlines", "scanlines" },
			{ "upper", "vignette_upper" },
			{ "lower", "vignette_lower" },
			{ "toggle", "map_effects" },
			{ "contrast", "daynight_contrast" },
			{ "smoothness", "daynight_smoothness" },
			{ "clouds", "clouds" },
			{ "edgeness", "clouds_edgeness" },
			{ "BorderThickness", "provinceBorderThickness" },
			{ "internal_border_darkness", "province_borders" },
		};
		foreach (var param in shaderMap.Keys)
			mat.SetShaderParameter(param, aSettings.GetValue("Graphics", shaderMap[param]));

		// UI effects are applied by the settings menu itself.
	}

	// Diff/merge live in Sosr.Core (Core/StateDiff.cs); these wrappers keep GDScript and Variant callers working.
	public Variant diffValue(Variant aSourceValue, Variant aChangeValue) =>
		VariantState.ToVariant(StateDiff.DiffValue(aSourceValue.AsState(), aChangeValue.AsState()));

	public Godot.Collections.Dictionary diff(Godot.Collections.Dictionary aSource, Godot.Collections.Dictionary aChange) =>
		VariantState.ToVariant(StateDiff.Diff(aSource.AsState(), aChange.AsState()));

	public Godot.Collections.Dictionary merge(Godot.Collections.Dictionary aSource, Godot.Collections.Dictionary aDiff) =>
		VariantState.ToVariant(StateDiff.Merge(aSource.AsState(), aDiff.AsState()));
}
