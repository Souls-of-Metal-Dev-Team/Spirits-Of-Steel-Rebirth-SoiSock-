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

		load_settings();
	}

	// Original GDScript method name, kept as an alias of the engine callback.
	public void _ready() => _Ready();

	public void load_settings()
	{
		if (FileAccess.FileExists(SETTINGS_FILE))
			settings.Load(SETTINGS_FILE);
		else
			settings.Save(SETTINGS_FILE);
	}

	// Was a static func in GDScript; an instance method is required so that
	// GDScript autoload calls (SettingsManager.apply_settings(...)) still resolve.
	public void apply_settings(ConfigFile a_settings)
	{
		// Audio
		var music = MusicManager.Instance;
		music.set_sfx_volume(a_settings.GetValue("Sound", "sfx_volume").AsSingle());
		music.set_music_volume(a_settings.GetValue("Sound", "music_volume").AsSingle());
		var radios = a_settings.GetValue("Sound", "radios");
		if (radios.VariantType == Variant.Type.Array)
			music.radios = new Godot.Collections.Array<string>(radios.AsGodotArray().Select(r => r.AsString()));

		// Graphics (These require GameState.current_world to be set)
		var world = GameState.Instance.current_world;
		if (world == null || !GodotObject.IsInstanceValid(world))
			return;
		var sprite = world.map_sprite;
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
			mat.SetShaderParameter(param, a_settings.GetValue("Graphics", shaderMap[param]));

		// UI effects are applied by the settings menu itself.
	}

	public Variant diffValue(Variant a_sourceValue, Variant a_changeValue)
	{
		switch (a_sourceValue.VariantType)
		{
			case Variant.Type.Array:
			{
				var sourceArray = a_sourceValue.AsGodotArray();
				var changeArray = a_changeValue.AsGodotArray();
				var allArray = new Godot.Collections.Array();
				foreach (var value in sourceArray)
					allArray.Add(value);
				foreach (var value in changeArray)
					allArray.Add(value);

				var changeDict = new Godot.Collections.Dictionary();
				foreach (var value in allArray)
				{
					if (!sourceArray.Contains(value))
						AppendValue(changeDict, "add", value);
					else if (!changeArray.Contains(value))
						AppendValue(changeDict, "remove", value);
				}
				return changeDict;
			}
			case Variant.Type.Dictionary:
				return diff(a_sourceValue.AsGodotDictionary(), a_changeValue.AsGodotDictionary());
			case Variant.Type.Int:
			case Variant.Type.Float:
			case Variant.Type.String:
			case Variant.Type.Bool:
				return a_changeValue;
			default:
				return new Variant();
		}
	}

	private static bool VariantsEqual(Variant a, Variant b)
	{
		if (a.VariantType == Variant.Type.Nil || b.VariantType == Variant.Type.Nil)
			return a.VariantType == b.VariantType;
		if ((a.VariantType == Variant.Type.Int || a.VariantType == Variant.Type.Float)
			&& (b.VariantType == Variant.Type.Int || b.VariantType == Variant.Type.Float))
			return System.Math.Abs(a.AsDouble() - b.AsDouble()) < 0.0000001;
		return a.AsString() == b.AsString();
	}

	public Godot.Collections.Dictionary diff(Godot.Collections.Dictionary a_source, Godot.Collections.Dictionary a_change)
	{
		var sourceKeys = a_source.Keys;
		var changeKeys = a_change.Keys;
		var allKeys = new Godot.Collections.Array();
		foreach (var key in sourceKeys)
			allKeys.Add(key);
		foreach (var key in changeKeys)
			allKeys.Add(key);

		// Dictionary keys dedupe the combined key list.
		var diffKeys = new Godot.Collections.Dictionary();
		foreach (var key in allKeys)
			diffKeys[key] = new Variant();

		var diffDict = new Godot.Collections.Dictionary();
		foreach (var key in diffKeys.Keys)
		{
			if (!sourceKeys.Contains(key))
			{
				diffDict[key] = new Godot.Collections.Dictionary
				{
					{ "type", "add" },
					{ "value", a_change[key] },
				};
			}
			else if (!changeKeys.Contains(key))
			{
				diffDict[key] = new Godot.Collections.Dictionary
				{
					{ "type", "remove" },
					{ "value", a_source[key] },
				};
			}
			else if (!VariantsEqual(a_source[key], a_change[key]))
			{
				var sourceType = a_source[key].VariantType;
				var changeType = a_change[key].VariantType;
				if (sourceType == Variant.Type.Dictionary && changeType == Variant.Type.Dictionary)
					diffDict[key] = diff(a_source[key].AsGodotDictionary(), a_change[key].AsGodotDictionary());
				else if (sourceType == Variant.Type.Array && changeType == Variant.Type.Array)
				{
					var arrayDiff = diffValue(a_source[key], a_change[key]).AsGodotDictionary();
					arrayDiff["type"] = "change";
					diffDict[key] = arrayDiff;
				}
				else
				{
					diffDict[key] = new Godot.Collections.Dictionary
					{
						{ "type", "change" },
						{ "value", a_change[key] },
					};
				}
			}
		}
		return diffDict;
	}

	public Godot.Collections.Dictionary merge(Godot.Collections.Dictionary a_source, Godot.Collections.Dictionary a_diff)
	{
		var result = new Godot.Collections.Dictionary();
		foreach (var key in a_source.Keys)
			result[key] = a_source[key];

		foreach (var key in a_diff.Keys)
		{
			var entry = a_diff[key];
			if (entry.VariantType != Variant.Type.Dictionary)
			{
				result[key] = entry;
				continue;
			}

			var entryDict = entry.AsGodotDictionary();
			var entryType = entryDict.ContainsKey("type") ? entryDict["type"].AsString() : "";

			if (entryType == "remove")
			{
				result.Remove(key);
				continue;
			}

			if (entryType == "change" && (entryDict.ContainsKey("add") || entryDict.ContainsKey("remove")))
			{
				var mergedArray = new Godot.Collections.Array();
				if (result.ContainsKey(key) && result[key].VariantType == Variant.Type.Array)
				{
					foreach (var value in result[key].AsGodotArray())
						mergedArray.Add(value);
				}
				var removals = entryDict.ContainsKey("remove") ? entryDict["remove"].AsGodotArray() : new Godot.Collections.Array();
				var additions = entryDict.ContainsKey("add") ? entryDict["add"].AsGodotArray() : new Godot.Collections.Array();
				foreach (var value in removals)
					mergedArray.Remove(value);
				foreach (var value in additions)
					mergedArray.Add(value);
				result[key] = mergedArray;
				continue;
			}

			if (entryType == "add" || entryType == "change")
			{
				result[key] = entryDict.ContainsKey("value") ? entryDict["value"] : new Variant();
				continue;
			}

			if (result.ContainsKey(key) && result[key].VariantType == Variant.Type.Dictionary)
			{
				result[key] = merge(result[key].AsGodotDictionary(), entryDict);
				continue;
			}

			result[key] = entryDict;
		}

		return result;
	}

	private static void AppendValue(Godot.Collections.Dictionary dict, string key, Variant value)
	{
		if (!dict.ContainsKey(key))
			dict[key] = new Godot.Collections.Array();
		dict[key].AsGodotArray().Add(value);
	}
}
