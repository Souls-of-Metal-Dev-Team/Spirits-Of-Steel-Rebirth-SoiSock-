using System.Collections.Generic;
using Xunit;

namespace Sosr.Tests;

// Ported from scratch/test_settings_manager_diff.gd.
public class StateDiffTests
{
	internal static Dictionary<string, object?> D(params (string Key, object? Value)[] entries)
	{
		var dict = new Dictionary<string, object?>();
		foreach (var (key, value) in entries)
			dict[key] = value;
		return dict;
	}

	internal static List<object?> L(params object?[] items) => new(items);

	internal static void AssertState(object? expected, object? actual) =>
		Assert.True(StateValue.Equal(expected, actual),
			$"expected {StateJson.Format(expected)}\nactual {StateJson.Format(actual)}");

	private static void CheckDiff(Dictionary<string, object?> source, Dictionary<string, object?> change, Dictionary<string, object?> expected)
	{
		var diff = StateDiff.Diff(source, change);
		AssertState(expected, diff);
		AssertState(change, StateDiff.Merge(source, diff));
	}

	[Fact] public void Empty() => CheckDiff(D(), D(), D());

	[Fact] public void Unchanged() => CheckDiff(D(("a", 1.0), ("b", "x")), D(("a", 1.0), ("b", "x")), D());

	[Fact] public void AddKey() =>
		CheckDiff(D(), D(("new", 5.0)), D(("new", D(("type", "add"), ("value", 5.0)))));

	[Fact] public void RemoveKey() =>
		CheckDiff(D(("ol", 5.0)), D(), D(("ol", D(("type", "remove"), ("value", 5.0)))));

	[Theory]
	[InlineData(1.0, 2.0)]
	[InlineData(1.5, 2.5)]
	[InlineData("a", "b")]
	[InlineData(false, true)]
	public void ChangedScalar(object source, object change) =>
		CheckDiff(D(("k", source)), D(("k", change)), D(("k", D(("type", "change"), ("value", change)))));

	[Fact] public void ChangedDictionary() =>
		CheckDiff(D(("nested", D(("x", 1.0)))), D(("nested", D(("x", 2.0)))),
			D(("nested", D(("x", D(("type", "change"), ("value", 2.0)))))));

	[Fact] public void ChangedArrayReportsBothSides() =>
		CheckDiff(D(("arr", L(1.0, 2.0))), D(("arr", L(2.0, 3.0))),
			D(("arr", D(("type", "change"), ("add", L(3.0)), ("remove", L(1.0))))));

	[Fact] public void UnchangedArray() => CheckDiff(D(("arr", L(1.0, 2.0))), D(("arr", L(1.0, 2.0))), D());

	[Fact] public void MixedKeys() =>
		CheckDiff(
			D(("same", 1.0), ("change", 2.0), ("remove", 3.0), ("arr", L(1.0, 2.0))),
			D(("same", 1.0), ("change", 9.0), ("add", 5.0), ("arr", L(2.0, 3.0))),
			D(
				("change", D(("type", "change"), ("value", 9.0))),
				("remove", D(("type", "remove"), ("value", 3.0))),
				("arr", D(("type", "change"), ("add", L(3.0)), ("remove", L(1.0)))),
				("add", D(("type", "add"), ("value", 5.0)))));

	[Fact] public void FloatNoiseIsNotAChange() => CheckDiff(D(("n", 1.0)), D(("n", 1.0 + 1e-9)), D());

	[Theory]
	[InlineData(2.0, 9.0)]
	[InlineData("a", "b")]
	[InlineData(false, true)]
	public void DiffValueScalar(object source, object change) => AssertState(change, StateDiff.DiffValue(source, change));

	[Fact] public void DiffValueDictionary() =>
		AssertState(D(("x", D(("type", "change"), ("value", 2.0)))), StateDiff.DiffValue(D(("x", 1.0)), D(("x", 2.0))));

	[Fact] public void DiffValueArray() =>
		AssertState(D(("add", L(3.0)), ("remove", L(1.0))), StateDiff.DiffValue(L(1.0, 2.0), L(2.0, 3.0)));

	[Fact] public void MergeNestedAddAndRemove()
	{
		var source = D(("nested", D(("keep", 1.0), ("drop", 2.0))));
		var change = D(("nested", D(("keep", 1.0), ("new", 3.0))));
		AssertState(change, StateDiff.Merge(source, StateDiff.Diff(source, change)));
	}

	[Fact] public void MergeDoesNotMutateSource()
	{
		var source = D(("nested", D(("x", 1.0))), ("arr", L(1.0, 2.0)));
		var change = D(("nested", D(("x", 2.0))), ("arr", L(2.0, 3.0)));
		StateDiff.Merge(source, StateDiff.Diff(source, change));
		AssertState(D(("nested", D(("x", 1.0))), ("arr", L(1.0, 2.0))), source);
	}
}
