using System;
using System.Collections.Generic;

namespace Sosr;

// Typed reads from the state tree produced by StateJson.
public static class StateAccess
{
	public static bool Has(this Dictionary<string, object?> dict, string key) => dict.ContainsKey(key);

	public static object? Get(this Dictionary<string, object?> dict, string key) =>
		dict.TryGetValue(key, out var value) ? value : null;

	public static string Str(this object? value, string fallback = "") =>
		value is string s ? s : fallback;

	public static int Int(this object? value, int fallback = 0) =>
		value is double d ? (int)Math.Round(d) : fallback;

	public static double Double(this object? value, double fallback = 0.0) =>
		value is double d ? d : fallback;

	public static float Single(this object? value, float fallback = 0.0f) =>
		value is double d ? (float)d : fallback;

	public static bool Bool(this object? value, bool fallback = false) =>
		value is bool b ? b : fallback;

	public static Dictionary<string, object?> Dict(this object? value) =>
		value as Dictionary<string, object?> ?? new Dictionary<string, object?>();

	public static List<object?> List(this object? value) =>
		value as List<object?> ?? new List<object?>();

	// Colors are stored as "#RRGGBB" strings, the same shape Godot's Color.ToHtml produces.
	public static string Hex(this object? value, string fallback = "#FFFFFF")
	{
		var text = value.Str(fallback);
		if (text.Length == 0)
			return fallback;
		return text.StartsWith('#') ? text.ToUpperInvariant() : "#" + text.ToUpperInvariant();
	}
}
