using Godot;
using System.Collections.Generic;

namespace Sosr;

// Converts between Godot Variants and Sosr.Core state trees (see Core/StateJson.cs).
public static class VariantState
{
	// Numbers become doubles and dictionary keys become strings, as a JSON round-trip would do.
	public static object AsState(this Variant value)
	{
		switch (value.VariantType)
		{
			case Variant.Type.Nil:
				return null;
			case Variant.Type.Bool:
				return value.AsBool();
			case Variant.Type.Int:
			case Variant.Type.Float:
				return value.AsDouble();
			case Variant.Type.String:
			case Variant.Type.StringName:
				return value.AsString();
			case Variant.Type.Dictionary:
				return value.AsGodotDictionary().AsState();
			case Variant.Type.Array:
				var list = new List<object>();
				foreach (var item in value.AsGodotArray())
					list.Add(item.AsState());
				return list;
			default:
				// Same fallback as Json.Stringify for engine types.
				return value.AsString();
		}
	}

	public static Dictionary<string, object> AsState(this Godot.Collections.Dictionary dict)
	{
		var result = new Dictionary<string, object>();
		foreach (var (key, item) in dict)
			result[key.AsString()] = item.AsState();
		return result;
	}

	public static Variant ToVariant(object value)
	{
		switch (value)
		{
			case null:
				return new Variant();
			case bool b:
				return b;
			case double d:
				return d;
			case string s:
				return s;
			case Dictionary<string, object> dict:
				return ToVariant(dict);
			case List<object> list:
				var array = new Godot.Collections.Array();
				foreach (var item in list)
					array.Add(ToVariant(item));
				return array;
			default:
				GD.PushError("VariantState: not a state value: " + value.GetType());
				return new Variant();
		}
	}

	public static Godot.Collections.Dictionary ToVariant(Dictionary<string, object> dict)
	{
		var result = new Godot.Collections.Dictionary();
		foreach (var (key, item) in dict)
			result[key] = ToVariant(item);
		return result;
	}
}
