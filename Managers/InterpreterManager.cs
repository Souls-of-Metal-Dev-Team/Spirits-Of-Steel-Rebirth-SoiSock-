using System;
using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

public partial class InterpreterManager : Node
{
	public enum ExpressionTypes {
		Variable,
		List,
		Event,
		Function,
		Loop,
		Match,
		Element,
		IdeologyDrift,
		Close,
	}

	public Godot.Collections.Dictionary functions = new()
	{
		{ "return", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "and", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "not", new Godot.Collections.Array { (int)ExpressionTypes.Function } },
		{ "or", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "xor", new Godot.Collections.Array { (int)ExpressionTypes.Function, (int)ExpressionTypes.Function } },
		{ "eq", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "gt", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "lt", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "all", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "any", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "probability", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "get", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "set_country_attr", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "add_country_attr", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "add", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "is_at_war", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "army_level_up", new Godot.Collections.Array() },
		{ "build_factory", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "declare_war", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "release", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "play_as", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "annex", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "add_ideology_drift", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable, (int)ExpressionTypes.IdeologyDrift } },
		{ "make_puppet", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "release_puppet", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "has_pids", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "add_plans", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "remove_plan", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "event", new Godot.Collections.Array { (int)ExpressionTypes.Variable } },
		{ "schedule_event", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "invite", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "change_province_types", new Godot.Collections.Array { (int)ExpressionTypes.List, (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
		{ "set_fig_attr", new Godot.Collections.Array { (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable, (int)ExpressionTypes.Variable } },
	};

	// Parameter names per function, shown as placeholder text in the decision editor.
	// Variadic functions repeat their last name for extra args.
	public Godot.Collections.Dictionary param_names = new()
	{
		{ "return", new Godot.Collections.Array { "value" } },
		{ "and", new Godot.Collections.Array { "condition" } },
		{ "not", new Godot.Collections.Array { "condition" } },
		{ "or", new Godot.Collections.Array { "condition" } },
		{ "xor", new Godot.Collections.Array { "condition_a", "condition_b" } },
		{ "eq", new Godot.Collections.Array { "left", "right" } },
		{ "gt", new Godot.Collections.Array { "left", "right" } },
		{ "lt", new Godot.Collections.Array { "left", "right" } },
		{ "all", new Godot.Collections.Array { "condition" } },
		{ "any", new Godot.Collections.Array { "condition" } },
		{ "probability", new Godot.Collections.Array { "chance" } },
		{ "get", new Godot.Collections.Array { "country", "attribute" } },
		{ "set_country_attr", new Godot.Collections.Array { "attribute", "value" } },
		{ "add_country_attr", new Godot.Collections.Array { "attribute", "amount" } },
		{ "add", new Godot.Collections.Array { "a", "b" } },
		{ "is_at_war", new Godot.Collections.Array { "country_a", "country_b" } },
		{ "army_level_up", new Godot.Collections.Array() },
		{ "build_factory", new Godot.Collections.Array { "amount" } },
		{ "declare_war", new Godot.Collections.Array { "attacker", "defender" } },
		{ "release", new Godot.Collections.Array { "country" } },
		{ "play_as", new Godot.Collections.Array { "country" } },
		{ "annex", new Godot.Collections.Array { "annexer", "annexee" } },
		{ "add_ideology_drift", new Godot.Collections.Array { "country", "ideology", "drift" } },
		{ "make_puppet", new Godot.Collections.Array { "puppeteer", "puppet" } },
		{ "release_puppet", new Godot.Collections.Array { "puppeteer", "puppet" } },
		{ "has_pids", new Godot.Collections.Array { "owner", "pid" } },
		{ "add_plans", new Godot.Collections.Array { "plan" } },
		{ "remove_plan", new Godot.Collections.Array { "plan" } },
		{ "event", new Godot.Collections.Array { "event" } },
		{ "schedule_event", new Godot.Collections.Array { "days", "event" } },
		{ "invite", new Godot.Collections.Array { "inviter", "invitee" } },
		{ "change_province_types", new Godot.Collections.Array { "pids", "type", "country_name" } },
		{ "set_fig_attr", new Godot.Collections.Array { "figure", "attribute", "value" } },
	};

	public Godot.Collections.Dictionary heap = new();

	public Godot.Collections.Dictionary DUMMY_FUNC = new()
	{
		{ "func", "return" },
		{ "args", new Godot.Collections.Array { true } },
	};

	private static Variant GetOrDefault(Godot.Collections.Dictionary dict, Variant key, Variant fallback)
		=> dict.TryGetValue(key, out var value) ? value : fallback;

	// Mirrors GDScript str() for the values the editor produces.
	private static string Str(Variant value)
		=> value.VariantType == Variant.Type.Nil ? "<null>" : value.AsString();

	private static bool _all_truthy(Godot.Collections.Array args)
	{
		foreach (var value in args)
			if (!value.AsBool())
				return false;
		return true;
	}

	private static bool _any_truthy(Godot.Collections.Array args)
	{
		foreach (var value in args)
			if (value.AsBool())
				return true;
		return false;
	}

	private static bool IsNumeric(Variant value)
		=> value.VariantType == Variant.Type.Int || value.VariantType == Variant.Type.Float;

	private static bool VariantEquals(Variant left, Variant right)
	{
		if (IsNumeric(left) && IsNumeric(right))
			return System.Math.Abs(left.AsDouble() - right.AsDouble()) < 0.0000001;
		if (left.VariantType == Variant.Type.Nil || right.VariantType == Variant.Type.Nil)
			return left.VariantType == right.VariantType;
		return left.AsString() == right.AsString();
	}

	private static int VariantCompare(Variant left, Variant right)
	{
		if (IsNumeric(left) && IsNumeric(right))
			return left.AsDouble().CompareTo(right.AsDouble());
		return string.CompareOrdinal(left.AsString(), right.AsString());
	}

	private static Variant VariantAdd(Variant left, Variant right)
	{
		if (left.VariantType == Variant.Type.Int && right.VariantType == Variant.Type.Int)
			return left.AsInt64() + right.AsInt64();
		if (IsNumeric(left) && IsNumeric(right))
			return left.AsDouble() + right.AsDouble();
		return left.AsString() + right.AsString();
	}

	public string get_param_name(string func_name, int index)
	{
		var names = param_names.ContainsKey(func_name)
			? param_names[func_name].AsGodotArray()
			: new Godot.Collections.Array();
		if (names.Count == 0)
			return "";
		return names[Math.Min(index, names.Count - 1)].AsString();
	}

	public Variant get_variable(Variant variable)
	{
		if (variable.VariantType != Variant.Type.String)
			return variable;
		// if CountryManager.countries.has(variable):
		// 	return CountryManager.countries[variable]
		switch (variable.AsString())
		{
			case "player":
				return GdBridge.PlayerCountry().Get("country_name");
			case "current_date":
				return GdBridge.CurrentWorld().Get("clock").AsGodotObject().Call("get_date_string");
			case "null":
				return new Variant();
			default:
				return heap.ContainsKey(variable) ? heap[variable] : variable;
		}
	}

	public void get_element(Variant element, Node grid, GodotObject country = null)
	{
		if (element.VariantType == Variant.Type.Nil)
			return;
		var elementDict = element.AsGodotDictionary();

		string text = "";
		if (elementDict.ContainsKey("text"))
		{
			var textValue = elementDict["text"];
			if (textValue.VariantType == Variant.Type.Array)
				text = Str(get_function(textValue));
			else if (textValue.VariantType == Variant.Type.String)
				text = textValue.AsString();
		}

		var condition = GetOrDefault(elementDict, "condition", DUMMY_FUNC);
		var finished = GetOrDefault(elementDict, "finished", DUMMY_FUNC);

		switch (GetOrDefault(elementDict, "type", "").AsString())
		{
			case "country_header":
			{
				var entry = new HBoxContainer();
				entry.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

				var tr1 = new TextureRect();
				tr1.Texture = GdBridge.Call("TroopManager", "get_flag", elementDict["country1"]).AsGodotObject() as Texture2D;
				tr1.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				tr1.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
				tr1.CustomMinimumSize = new Vector2(80, 50);
				entry.AddChild(tr1);

				var spacer = new Control();
				spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				entry.AddChild(spacer);

				var tr2 = new TextureRect();
				tr2.Texture = GdBridge.Call("TroopManager", "get_flag", elementDict["country2"]).AsGodotObject() as Texture2D;
				tr2.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				tr2.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
				tr2.CustomMinimumSize = new Vector2(80, 50);
				entry.AddChild(tr2);

				grid.AddChild(entry);
				entry.Material = (grid as CanvasItem)?.Material;
				tr1.Material = (grid as CanvasItem)?.Material;
				tr2.Material = (grid as CanvasItem)?.Material;
				tr1.MouseFilter = Control.MouseFilterEnum.Ignore;
				tr2.MouseFilter = Control.MouseFilterEnum.Ignore;
				break;
			}
			case "button":
			{
				var entry = new Button();
				entry.Text = text;

				if (!get_function(condition, country).AsBool())
					entry.Disabled = true;

				entry.SetMeta("condition", condition);
				entry.SetMeta("country_context", country);

				var capturedCondition = condition;
				var capturedFinished = finished;
				var capturedCountry = country;
				entry.Pressed += () =>
				{
					if (get_function(capturedCondition, capturedCountry).AsBool())
						get_function(capturedFinished, capturedCountry);
				};
				grid.AddChild(entry);
				entry.Material = (grid as CanvasItem)?.Material;
				entry.TooltipText = $"Condition - {format_functions(condition)}\nFinished - {format_functions(finished)}";
				break;
			}
			case "image":
			{
				var entry = new TextureRect();
				var startFolder = GdBridge.Get("PlansManager", "current_start_folder").AsString();
				entry.Texture = GD.Load<Texture2D>(startFolder + "assets/plans/" + elementDict["path"].AsString());
				entry.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				entry.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
				entry.CustomMinimumSize = new Vector2(100, 100);
				entry.MouseFilter = Control.MouseFilterEnum.Ignore;
				grid.AddChild(entry);
				entry.Material = (grid as CanvasItem)?.Material;
				break;
			}
			case "paragraph":
			{
				var entry = new RichTextLabel();
				entry.AddThemeFontSizeOverride("normal_font_size", 18);
				entry.AddText(text);
				entry.FitContent = true;
				entry.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				entry.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
				entry.MouseFilter = Control.MouseFilterEnum.Ignore;
				grid.AddChild(entry);
				break;
			}
		}
	}

	public Variant get_function(Variant block, GodotObject country = null)
	{
		if (!block.AsBool())
			return new Variant();

		if (country == null)
			country = GdBridge.PlayerCountry();

		// Handle multiple functions (Recusive Array Support)
		if (block.VariantType == Variant.Type.Array)
		{
			var lastResult = new Variant();
			foreach (var item in block.AsGodotArray())
				lastResult = get_function(item, country);
			return lastResult;
		}

		if (block.VariantType != Variant.Type.Dictionary)
			return block;

		var blockDict = block.AsGodotDictionary();
		var storeKey = GetOrDefault(blockDict, "store", "").AsString();
		var result = new Variant();

		// match statement
		if (blockDict.ContainsKey("match"))
		{
			var matchVal = Str(get_function(blockDict["match"], country));
			if (matchVal != "match") // Prevent collision with 'match' key itself
				result = get_function(GetOrDefault(blockDict, matchVal, new Godot.Collections.Dictionary()), country);
		}

		// for loop
		if (blockDict.ContainsKey("for"))
		{
			var varName = blockDict["for"];
			var hadPrev = heap.ContainsKey(varName);
			var prevVal = hadPrev ? heap[varName] : new Variant();
			var inValues = GetOrDefault(blockDict, "in", new Godot.Collections.Array());
			if (inValues.VariantType == Variant.Type.Array)
			{
				foreach (var item in inValues.AsGodotArray())
				{
					heap[varName] = item;
					result = get_function(GetOrDefault(blockDict, "do", new Godot.Collections.Dictionary()), country);
				}
			}
			if (hadPrev)
				heap[varName] = prevVal;
			else
				heap.Remove(varName);
		}

		var args = GetOrDefault(blockDict, "args", new Godot.Collections.Array()).AsGodotArray().Duplicate();
		var evaledArgs = new Godot.Collections.Array();
		foreach (var rawArg in args)
		{
			Variant arg;
			if (rawArg.VariantType == Variant.Type.Dictionary || rawArg.VariantType == Variant.Type.Array)
				arg = get_function(rawArg, country);
			else
				arg = get_variable(rawArg);
			evaledArgs.Add(arg);
		}

		var funcName = GetOrDefault(blockDict, "func", "").AsString();
		if (funcName == "" && !(blockDict.ContainsKey("match") || blockDict.ContainsKey("for")))
			return block;

		if (funcName != "" && functions.ContainsKey(funcName))
		{
			var expectedArgs = functions[funcName].AsGodotArray();
			if (evaledArgs.Count < expectedArgs.Count)
			{
				GD.PushError($"Interpreter: Function '{funcName}' expects at least {expectedArgs.Count} args, got {evaledArgs.Count}");
				return new Variant();
			}
		}

		switch (funcName)
		{
			// idk stuff
			case "return":
				if (evaledArgs.Count == 1)
					result = evaledArgs[0];
				break;
			// Comparison stuff
			case "and":
			case "all":
				result = _all_truthy(evaledArgs);
				break;
			case "not":
				if (evaledArgs.Count >= 1)
					result = !evaledArgs[0].AsBool();
				break;
			case "or":
			case "any":
				result = _any_truthy(evaledArgs);
				break;
			case "xor":
				if (evaledArgs.Count >= 2)
					result = evaledArgs[0].AsBool() != evaledArgs[1].AsBool();
				break;
			case "eq":
				if (evaledArgs.Count >= 2)
					result = VariantEquals(evaledArgs[0], evaledArgs[1]);
				break;
			case "gt":
				if (evaledArgs.Count >= 2)
					result = VariantCompare(evaledArgs[0], evaledArgs[1]) > 0;
				break;
			case "lt":
				if (evaledArgs.Count >= 2)
					result = VariantCompare(evaledArgs[0], evaledArgs[1]) < 0;
				break;
			case "probability":
				result = (long)(GD.Randi() % (ulong)Math.Max(1L, evaledArgs[0].AsInt64())) != 0;
				break;
			case "get":
				if (evaledArgs.Count >= 2)
				{
					var countries = GdBridge.Countries();
					if (countries.ContainsKey(evaledArgs[0]) && countries[evaledArgs[0]].VariantType != Variant.Type.Nil)
						result = countries[evaledArgs[0]].AsGodotObject().Get((StringName)evaledArgs[1].AsString());
					else
						result = new Variant();
				}
				break;
			case "set_country_attr":
			{
				var attr = evaledArgs[0].AsString();
				country.Set(attr, evaledArgs[1]);
				result = country.Get(attr);
				break;
			}
			case "add_country_attr":
			{
				var attr = evaledArgs[0].AsString();
				country.Set(attr, VariantAdd(country.Get(attr), evaledArgs[1]));
				result = country.Get(attr);
				break;
			}
			case "set_fig_attr":
				if (evaledArgs.Count >= 3)
				{
					var significantFigures = GdBridge.Autoload("MapManager").Get("significantFigures").AsGodotDictionary();
					significantFigures[evaledArgs[0]].AsGodotDictionary()[evaledArgs[1]] = evaledArgs[2];
				}
				break;
			// Math stuff**
			case "add":
				if (evaledArgs.Count >= 2)
					result = VariantAdd(evaledArgs[0], evaledArgs[1]);
				break;
			// Country stuff
			case "is_at_war":
				if (evaledArgs.Count >= 2)
					result = GdBridge.Call("WarManager", "is_at_war_names", evaledArgs[0], evaledArgs[1]);
				break;
			case "army_level_up":
				country.Set("army_level", country.Get("army_level").AsInt64() + 1);
				result = country.Get("army_level");
				break;
			case "build_factory":
				country.Set("factories_amount", country.Get("factories_amount").AsInt64() + (evaledArgs.Count > 0 ? evaledArgs[0].AsInt64() : 1));
				result = country.Get("factories_amount");
				break;
			case "declare_war":
				if (evaledArgs.Count >= 2)
				{
					var countries = GdBridge.Countries();
					var attacker = countries.ContainsKey(evaledArgs[0]) ? countries[evaledArgs[0]].AsGodotObject() : null;
					var defender = countries.ContainsKey(evaledArgs[1]) ? countries[evaledArgs[1]].AsGodotObject() : null;
					if (attacker != null && defender != null)
					{
						GdBridge.Call("WarManager", "declare_war", attacker, defender);
						result = true;
					}
					else
						result = false;
				}
				break;
			case "release":
				if (evaledArgs.Count >= 1)
				{
					GdBridge.Call("MapManager", "ReleaseCountry", evaledArgs[0]);
					result = true;
				}
				break;
			case "play_as":
				if (evaledArgs.Count >= 1)
				{
					var countryName = args.Count > 0 ? args[0].AsString() : "";
					if (GdBridge.Countries().ContainsKey(countryName))
					{
						GD.Print("player country now " + countryName);
						GdBridge.Call("CountryManager", "set_player_country", countryName);
					}
					GD.Print("Unknown country: " + countryName);
					result = true;
				}
				break;
			case "annex":
				if (evaledArgs.Count >= 2)
				{
					GdBridge.Call("MapManager", "AnnexCountry", evaledArgs[0], evaledArgs[1]);
					result = true;
				}
				break;
			case "add_ideology_drift":
			{
				var countryObj = GdBridge.Countries()[evaledArgs[0]].AsGodotObject();
				var driftTargets = countryObj.Get("driftTargets").AsGodotDictionary();
				driftTargets[args[1]] = IdeologyDriftTarget.FromDict(args[2].AsGodotDictionary());
				result = driftTargets;
				break;
			}
			case "make_puppet":
				if (evaledArgs.Count >= 2)
				{
					var countries = GdBridge.Countries();
					GdBridge.Call("CountryManager", "make_puppet", countries[evaledArgs[0]], countries[evaledArgs[1]]);
					result = true;
				}
				break;
			case "release_puppet":
				if (evaledArgs.Count >= 2)
				{
					var countries = GdBridge.Countries();
					GdBridge.Call("CountryManager", "release_puppet", countries[evaledArgs[0]], countries[evaledArgs[1]]);
					result = true;
				}
				break;
			case "has_pids":
				if (evaledArgs.Count >= 2)
				{
					var i = 1;
					var provinceOwner = evaledArgs[0];
					var allOwned = true;
					var provinceObjects = GdBridge.Autoload("MapManager").Get("province_objects").AsGodotDictionary();
					while (i < evaledArgs.Count)
					{
						var province = provinceObjects[evaledArgs[i].AsInt64()].AsGodotObject();
						if (!VariantEquals(provinceOwner, province.Call("GetFunctionalOwner")))
						{
							allOwned = false;
							break;
						}
						i++;
					}
					result = allOwned;
				}
				break;
			case "add_plans":
			{
				var plans = GdBridge.Get("PlansManager", "plans").AsGodotDictionary();
				var countryName = country.Get("country_name").AsString();
				if (!plans.ContainsKey(countryName))
					plans[countryName] = new Godot.Collections.Array();
				plans[countryName].AsGodotArray().Add(evaledArgs[0]);
				break;
			}
			case "remove_plan":
			{
				var plans = GdBridge.Get("PlansManager", "plans").AsGodotDictionary();
				var countryName = country.Get("country_name").AsString();
				var countryPlans = plans[countryName].AsGodotArray();
				var index = -1;
				for (var i = 0; i < countryPlans.Count; i++)
				{
					if (VariantEquals(countryPlans[i], args[0]))
					{
						index = i;
						break;
					}
				}
				if (index >= 0)
					countryPlans.RemoveAt(index);
				break;
			}
			case "event":
				if (evaledArgs.Count > 0 && evaledArgs[0].VariantType == Variant.Type.Dictionary)
				{
					GdBridge.Call("EventManager", "show_alert", evaledArgs[0]);
					result = true;
				}
				break;
			case "schedule_event":
				if (evaledArgs.Count >= 2 && evaledArgs[1].VariantType == Variant.Type.Dictionary)
				{
					GdBridge.Call("EventManager", "schedule_event", evaledArgs[0].AsInt64(), evaledArgs[1]);
					result = true;
				}
				break;
			case "invite":
				if (evaledArgs.Count >= 2)
				{
					var countries = GdBridge.Countries();
					var inviter = countries.ContainsKey(evaledArgs[0]) ? countries[evaledArgs[0]].AsGodotObject() : null;
					var invitee = countries.ContainsKey(evaledArgs[1]) ? countries[evaledArgs[1]].AsGodotObject() : null;
					if (inviter != null && invitee != null)
						GdBridge.Call("FactionManager", "invite_faction", inviter, invitee);
				}
				break;
			case "change_province_types":
				if (evaledArgs.Count >= 3)
				{
					var pids = evaledArgs[0];
					var pidsTyped = new Godot.Collections.Array<int>();
					if (pids.VariantType == Variant.Type.Float || pids.VariantType == Variant.Type.Int)
						pidsTyped.Add(pids.AsInt32());
					else if (pids.VariantType == Variant.Type.Array)
						foreach (var p in pids.AsGodotArray())
							pidsTyped.Add(p.AsInt32());
					GdBridge.Call("MapManager", "ChangeProvinceTypes", pidsTyped, evaledArgs[1].AsInt64(), evaledArgs[2].AsString());
				}
				break;
		}

		if (storeKey != "")
			heap[storeKey] = result;

		return result;
	}

	public void refresh_buttons(Node container)
	{
		foreach (Node child in container.GetChildren())
		{
			if (child is Button button && button.HasMeta("condition"))
			{
				var condition = button.GetMeta("condition");
				var country = button.GetMeta("country_context").AsGodotObject();
				button.Disabled = !get_function(condition, country).AsBool();
			}

			// Recurse
			refresh_buttons(child);
		}
	}

	public string format_functions(Variant expression, string indent = "", bool no_bullet = false)
	{
		Godot.Collections.Array funcs;
		if (expression.VariantType == Variant.Type.Array)
			funcs = expression.AsGodotArray();
		else if (expression.VariantType == Variant.Type.Dictionary)
			funcs = new Godot.Collections.Array { expression };
		else
			return "";

		var formatted = "";
		foreach (var functionVariant in funcs)
		{
			if (functionVariant.VariantType != Variant.Type.Dictionary)
				continue;
			var function = functionVariant.AsGodotDictionary();

			// Handle 'for' loop
			if (function.ContainsKey("for"))
			{
				formatted += indent + (no_bullet ? "" : "• ") + "For each " + GdBridge.Capitalize(Str(function["for"]));
				formatted += " in " + Str(GetOrDefault(function, "in", new Godot.Collections.Array())) + ":\n";
				formatted += format_functions(GetOrDefault(function, "do", new Godot.Collections.Dictionary()), indent + "  ") + "\n";
				continue;
			}

			// Handle 'match' statement
			if (function.ContainsKey("match"))
			{
				var matchHeader = format_functions(function["match"], "", true).Trim();
				formatted += indent + (no_bullet ? "" : "• ") + "Match " + matchHeader + ":\n";
				foreach (var key in function.Keys)
				{
					if (key.VariantType == Variant.Type.String && (key.AsString() == "match" || key.AsString() == "func" || key.AsString() == "args" || key.AsString() == "store"))
						continue;
					formatted += indent + "  - " + GdBridge.Capitalize(Str(key)) + ":\n";
					formatted += format_functions(function[key], indent + "    ") + "\n";
				}
				continue;
			}

			if (function.ContainsKey("func"))
			{
				var funcName = function["func"].AsString();
				var argsVariant = GetOrDefault(function, "args", new Godot.Collections.Array());
				Godot.Collections.Array args;
				if (argsVariant.VariantType == Variant.Type.Array)
					args = argsVariant.AsGodotArray();
				else
					args = new Godot.Collections.Array { argsVariant };

				formatted += indent + (no_bullet ? "" : "• ") + GdBridge.Capitalize(funcName);

				// Check if args contain nested functions (like in 'and', 'or', 'all', 'any')
				var nestedFound = false;
				foreach (var arg in args)
				{
					if ((arg.VariantType == Variant.Type.Dictionary && arg.AsGodotDictionary().ContainsKey("func")) || arg.VariantType == Variant.Type.Array)
					{
						nestedFound = true;
						break;
					}
				}

				if (nestedFound)
				{
					formatted += ":\n";
					formatted += format_functions(args, indent + "  ") + "\n";
				}
				else
				{
					var formattedArgs = new List<string>();
					foreach (var arg in args)
					{
						var isNumber = arg.VariantType == Variant.Type.Int || arg.VariantType == Variant.Type.Float;
						// Only lookup province names for specific functions to avoid constant collisions
						var shouldLookupProv = funcName == "has_pids" || funcName == "annex" || funcName == "release";
						if (shouldLookupProv && isNumber)
						{
							var provinceObjects = GdBridge.Autoload("MapManager").Get("province_objects").AsGodotDictionary();
							var pid = arg.AsInt64();
							if (provinceObjects.ContainsKey(pid))
							{
								var prov = provinceObjects[pid].AsGodotObject();
								var city = prov.Get("city").AsString();
								var name = prov.Get("name").AsString();
								formattedArgs.Add(city != "" ? city : name);
							}
							else
								formattedArgs.Add(Str(arg));
						}
						else
							formattedArgs.Add(Str(arg));
					}

					if (formattedArgs.Count > 0)
						formatted += ": " + string.Join(", ", formattedArgs);
					formatted += "\n";
				}
			}
		}

		return indent == "" ? formatted.Trim() : formatted;
	}

	// NOTE(soi): kept for backward compat; loop bodies now evaluate with heap-bound locals.
	public Variant _substitute(Variant expression, Godot.Collections.Dictionary _heap, Godot.Collections.Array visited = null)
	{
		visited ??= new Godot.Collections.Array();
		if (expression.VariantType == Variant.Type.Dictionary)
		{
			var dict = expression.AsGodotDictionary();
			foreach (var key in dict.Keys)
				dict[key] = _substitute(dict[key], _heap, visited);
			return dict;
		}
		if (expression.VariantType == Variant.Type.Array)
		{
			var array = expression.AsGodotArray();
			for (var i = 0; i < array.Count; i++)
				array[i] = _substitute(array[i], _heap, visited);
			return array;
		}
		if (expression.VariantType == Variant.Type.String)
		{
			var value = expression.AsString();
			if (_heap.ContainsKey(value))
			{
				if (visited.Contains(value))
				{
					GD.PushError($"Interpreter: Circular reference detected for variable '{value}'");
					return expression;
				}
				var newVisited = visited.Duplicate();
				newVisited.Add(value);
				return _substitute(_heap[value], _heap, newVisited);
			}
			return expression;
		}
		return expression;
	}
}
