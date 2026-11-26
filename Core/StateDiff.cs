using System;
using System.Collections.Generic;

namespace Sosr;

// Diff and merge for game-state trees. A state tree holds only
// Dictionary<string, object?>, List<object?>, double, string, bool and null (see StateJson).
// Save files store diff(start, current); loading runs merge(start, diff).
public static class StateDiff
{
	public static object? DiffValue(object? source, object? change)
	{
		switch (source)
		{
			case List<object?> sourceList:
			{
				var changeList = change as List<object?> ?? new List<object?>();
				var changeDict = new Dictionary<string, object?>();
				foreach (var value in Concat(sourceList, changeList))
				{
					if (!Contains(sourceList, value))
						AppendValue(changeDict, "add", value);
					else if (!Contains(changeList, value))
						AppendValue(changeDict, "remove", value);
				}
				return changeDict;
			}
			case Dictionary<string, object?> sourceDict:
				return Diff(sourceDict, change as Dictionary<string, object?> ?? new Dictionary<string, object?>());
			case double or string or bool:
				return change;
			default:
				return null;
		}
	}

	public static Dictionary<string, object?> Diff(Dictionary<string, object?> source, Dictionary<string, object?> change)
	{
		var diffDict = new Dictionary<string, object?>();
		var seen = new HashSet<string>();
		foreach (var key in Concat(source.Keys, change.Keys))
		{
			if (!seen.Add(key))
				continue;

			if (!source.TryGetValue(key, out var sourceValue))
				diffDict[key] = Entry("add", change[key]);
			else if (!change.TryGetValue(key, out var changeValue))
				diffDict[key] = Entry("remove", sourceValue);
			else if (!StateValue.Equal(sourceValue, changeValue))
			{
				if (sourceValue is Dictionary<string, object?> sd && changeValue is Dictionary<string, object?> cd)
					diffDict[key] = Diff(sd, cd);
				else if (sourceValue is List<object?> && changeValue is List<object?>)
				{
					var arrayDiff = (Dictionary<string, object?>)DiffValue(sourceValue, changeValue)!;
					arrayDiff["type"] = "change";
					diffDict[key] = arrayDiff;
				}
				else
					diffDict[key] = Entry("change", changeValue);
			}
		}
		return diffDict;
	}

	// Never mutates source: changed nested dictionaries and lists are rebuilt.
	public static Dictionary<string, object?> Merge(Dictionary<string, object?> source, Dictionary<string, object?> diff)
	{
		var result = new Dictionary<string, object?>(source);

		foreach (var (key, entry) in diff)
		{
			if (entry is not Dictionary<string, object?> entryDict)
			{
				result[key] = entry;
				continue;
			}

			var entryType = entryDict.TryGetValue("type", out var t) && t is string s ? s : "";

			if (entryType == "remove")
			{
				result.Remove(key);
				continue;
			}

			if (entryType == "change" && (entryDict.ContainsKey("add") || entryDict.ContainsKey("remove")))
			{
				var merged = result.TryGetValue(key, out var existing) && existing is List<object?> existingList
					? new List<object?>(existingList)
					: new List<object?>();
				if (entryDict.TryGetValue("remove", out var removals) && removals is List<object?> removeList)
					foreach (var value in removeList)
						RemoveFirst(merged, value);
				if (entryDict.TryGetValue("add", out var additions) && additions is List<object?> addList)
					merged.AddRange(addList);
				result[key] = merged;
				continue;
			}

			if (entryType == "add" || entryType == "change")
			{
				result[key] = entryDict.GetValueOrDefault("value");
				continue;
			}

			if (result.TryGetValue(key, out var current) && current is Dictionary<string, object?> currentDict)
			{
				result[key] = Merge(currentDict, entryDict);
				continue;
			}

			result[key] = entryDict;
		}

		return result;
	}

	private static Dictionary<string, object?> Entry(string type, object? value) =>
		new() { { "type", type }, { "value", value } };

	private static IEnumerable<T> Concat<T>(IEnumerable<T> a, IEnumerable<T> b)
	{
		foreach (var x in a) yield return x;
		foreach (var x in b) yield return x;
	}

	private static bool Contains(List<object?> list, object? value) =>
		list.Exists(item => StateValue.Equal(item, value));

	private static void RemoveFirst(List<object?> list, object? value)
	{
		var index = list.FindIndex(item => StateValue.Equal(item, value));
		if (index >= 0)
			list.RemoveAt(index);
	}

	private static void AppendValue(Dictionary<string, object?> dict, string key, object? value)
	{
		if (!dict.TryGetValue(key, out var list))
			dict[key] = list = new List<object?>();
		((List<object?>)list!).Add(value);
	}
}

public static class StateValue
{
	// Deep equality. Numbers match within 1e-7 so JSON float noise is not a change.
	public static bool Equal(object? a, object? b)
	{
		switch (a)
		{
			case null:
				return b is null;
			case double da:
				return b is double db && Math.Abs(da - db) < 0.0000001;
			case Dictionary<string, object?> ad:
				if (b is not Dictionary<string, object?> bd || ad.Count != bd.Count)
					return false;
				foreach (var (key, value) in ad)
					if (!bd.TryGetValue(key, out var other) || !Equal(value, other))
						return false;
				return true;
			case List<object?> al:
				if (b is not List<object?> bl || al.Count != bl.Count)
					return false;
				for (var i = 0; i < al.Count; i++)
					if (!Equal(al[i], bl[i]))
						return false;
				return true;
			default:
				return a.Equals(b);
		}
	}
}
