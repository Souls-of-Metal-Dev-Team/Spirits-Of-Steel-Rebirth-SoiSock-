using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Sosr;

// JSON <-> state tree. Every number becomes a double, matching Godot's JSON parser.
public static class StateJson
{
	public static object? Parse(string text)
	{
		using var doc = JsonDocument.Parse(text);
		return FromElement(doc.RootElement);
	}

	// Godot's JSON parser reads this output, so key order and float format do not matter.
	public static string Format(object? value)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
			Write(writer, value);
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static object? FromElement(JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				var dict = new Dictionary<string, object?>();
				foreach (var property in element.EnumerateObject())
					dict[property.Name] = FromElement(property.Value);
				return dict;
			case JsonValueKind.Array:
				var list = new List<object?>(element.GetArrayLength());
				foreach (var item in element.EnumerateArray())
					list.Add(FromElement(item));
				return list;
			case JsonValueKind.Number:
				return element.GetDouble();
			case JsonValueKind.String:
				return element.GetString();
			case JsonValueKind.True:
				return true;
			case JsonValueKind.False:
				return false;
			default:
				return null;
		}
	}

	private static void Write(Utf8JsonWriter writer, object? value)
	{
		switch (value)
		{
			case null:
				writer.WriteNullValue();
				break;
			case double d:
				writer.WriteNumberValue(d);
				break;
			case string s:
				writer.WriteStringValue(s);
				break;
			case bool b:
				writer.WriteBooleanValue(b);
				break;
			case Dictionary<string, object?> dict:
				writer.WriteStartObject();
				foreach (var (key, item) in dict)
				{
					writer.WritePropertyName(key);
					Write(writer, item);
				}
				writer.WriteEndObject();
				break;
			case List<object?> list:
				writer.WriteStartArray();
				foreach (var item in list)
					Write(writer, item);
				writer.WriteEndArray();
				break;
			default:
				throw new ArgumentException($"Not a state value: {value.GetType()}");
		}
	}
}
