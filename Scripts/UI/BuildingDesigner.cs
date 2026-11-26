using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace Sosr;

public partial class BuildingDesigner : PanelContainer
{
	[Export] public VBoxContainer functionsList;
	[Export] public GridContainer functionsGrid;
	[Export] public TextEdit buildingName;
	[Export] public Label feasibilityText;
	[Export] public Button addBuilding;
	[Export] public VBoxContainer existingDesign;

	public GDict currentBuildingTemplate = new();

	public readonly List<string> selectedFunctionalities = new();

	// Maps functionality names to their specific VBoxContainers containing their OptionButtons/Sliders
	private readonly Dictionary<string, VBoxContainer> funcUiContainers = new();

	private readonly TagMatcher activeTagMatcher = TagMatcher.Create(new GDict());

	private static readonly HashSet<string> RESERVED_KEYS =
		["name", "functionalities", "assigned_inputs", "assigned_outputs", "money_cost"];

	public override void _Ready()
	{
		var functions = BuildingManager.Instance.buildingFunctions;
		foreach (var key in functions.Keys)
		{
			var functionName = key.AsString();
			var funcData = functions[key].AsGodotDictionary();
			var cb = new CheckButton { Text = functionName };
			cb.Toggled += toggledOn =>
			{
				if (toggledOn)
				{
					if (!selectedFunctionalities.Contains(functionName))
					{
						selectedFunctionalities.Add(functionName);
						_spawn_func_ui(functionName, funcData);
					}
				}
				else
				{
					selectedFunctionalities.Remove(functionName);
					_destroy_func_ui(functionName);
				}
				_recalculate();
			};
			InputsListOrGrid().AddChild(cb);
		}
	}

	public Container InputsListOrGrid() => functionsList != null ? functionsList : functionsGrid;

	private static Range AmountNode(Node holder) => holder.GetMeta("amount_node").As<Range>();

	private void _spawn_func_ui(string funcName, GDict funcData)
	{
		var vbox = new VBoxContainer();

		if (funcData.TryGetValue("input", out var inputs))
		{
			var matchIndex = 0;
			foreach (var _ in inputs.AsGodotArray())
				activeTagMatcher.slottedResources.Add(new GDict());

			foreach (var inpVariant in inputs.AsGodotArray())
			{
				var inp = inpVariant.AsGodotDictionary();
				if (inp.ContainsKey("match"))
				{
					_build_match_ui(vbox, inp, "Input", matchIndex);
					matchIndex++;
				}
				else if (inp.ContainsKey("resource_tag"))
				{
					_build_tag_ui(vbox, inp, "Input", matchIndex);
					matchIndex++;
				}
			}
		}

		if (funcData.TryGetValue("output", out var outputs))
		{
			var matchIndex = 0;
			foreach (var outVariant in outputs.AsGodotArray())
			{
				var output = outVariant.AsGodotDictionary();
				if (output.ContainsKey("match"))
				{
					// For output, we just build a match card but amount is non-editable
					_build_match_ui(vbox, output, "Output", matchIndex);
					matchIndex++;
				}
			}
		}

		funcUiContainers[funcName] = vbox;

		var list = InputsListOrGrid();
		for (var i = 0; i < list.GetChildCount(); i++)
		{
			if (list.GetChild(i) is CheckButton c && c.Text == funcName)
			{
				list.AddChild(vbox);
				list.MoveChild(vbox, i + 1);
				break;
			}
		}
	}

	private void _build_match_ui(Container vbox, GDict block, string prefix, int index)
	{
		var card = new PanelContainer();
		var cardVbox = new VBoxContainer();
		card.SetMeta("io_type", prefix);

		var hboxOpt = new HBoxContainer();
		var lbl = new Label { Text = $"{prefix} {index}" };
		var opt = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		opt.AddItem("...");
		opt.SetMeta("match_expr", block["match"]);
		opt.SetMeta("last_selected", "");
		opt.ItemSelected += _ => _recalculate();

		hboxOpt.AddChild(lbl);
		hboxOpt.AddChild(opt);
		cardVbox.AddChild(hboxOpt);

		var amountData = block.TryGetValue("amount", out var amount) ? amount : "0";
		// If it's an output, it's evaluated, so we force it to basically be a spinbox
		_build_amount_ui(cardVbox, amountData, prefix != "Output");

		card.AddChild(cardVbox);
		vbox.AddChild(card);
	}

	private void _build_tag_ui(Container vbox, GDict block, string prefix, int _index)
	{
		var rTag = block["resource_tag"].AsString();
		var hbox = new HBoxContainer();
		hbox.SetMeta("io_type", prefix);
		var lbl = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Text = "Pool Req: " + rTag,
		};
		hbox.AddChild(lbl);

		var amountData = block.TryGetValue("amount", out var amount) ? amount : "0";
		var amountCnt = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		amountCnt.SetMeta("resource_tag", rTag);

		_build_amount_ui(amountCnt, amountData, prefix == "Input");
		hbox.AddChild(amountCnt);
		vbox.AddChild(hbox);
	}

	private void _build_amount_ui(Container parent, Variant amountData, bool editable)
	{
		if (amountData.VariantType == Variant.Type.Dictionary && editable)
		{
			var range = amountData.AsGodotDictionary();
			var hsl = new HSlider { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Step = 0.1 };
			hsl.SetMeta("min_expr", range.TryGetValue("min", out var min) ? min : "0");
			hsl.SetMeta("max_expr", range.TryGetValue("max", out var max) ? max : "0");

			var lbl = new Label { Text = "Amt: 0.0" };
			hsl.ValueChanged += v =>
			{
				lbl.Text = $"Amt: {v:F1}";
				_recalculate();
			};

			var amtHbox = new HBoxContainer();
			amtHbox.AddChild(hsl);
			amtHbox.AddChild(lbl);

			parent.AddChild(amtHbox);
			parent.SetMeta("amount_node", hsl);
		}
		else
		{
			var sp = new SpinBox
			{
				Editable = false,
				Step = 0.1,
				MinValue = -999999.0,
				MaxValue = 999999.0,
			};
			sp.SetMeta("fixed_expr", amountData.ToString());

			var amtHbox = new HBoxContainer();
			amtHbox.AddChild(new Label { Text = "Amt:" });
			amtHbox.AddChild(sp);

			parent.AddChild(amtHbox);
			parent.SetMeta("amount_node", sp);
		}
	}

	private void _destroy_func_ui(string funcName)
	{
		if (funcUiContainers.Remove(funcName, out var vbox))
			vbox.QueueFree();
	}

	private static ResourceData ResourceByName(string resName)
	{
		var resources = MapManager.Instance.resources;
		return resources.TryGetValue(resName, out var r) ? r : null;
	}

	// Gathers chosen values and resources mapping for evaluating dynamic data.
	// Each slot is a Dictionary { resource: ResourceData|null, amount: float, resource_tag: String },
	// the shape TagMatcher.set_context expects.
	private Dictionary<string, (GArray inputs, GArray outputs)> _gather_func_io()
	{
		var mapping = new Dictionary<string, (GArray inputs, GArray outputs)>();
		foreach (var funcName in selectedFunctionalities)
		{
			var inputs = new GArray();
			var outputs = new GArray();
			if (funcUiContainers.TryGetValue(funcName, out var vbox))
			{
				foreach (var uiElement in vbox.GetChildren())
				{
					var slotData = new GDict { { "resource", new Variant() }, { "amount", 0.0 }, { "resource_tag", "" } };
					GArray targetArr = null;

					if (uiElement is PanelContainer card)
					{
						targetArr = card.GetMeta("io_type").AsString() == "Input" ? inputs : outputs;
						var cardVc = card.GetChild(0);
						var opt = cardVc.GetChild(0).GetChild<OptionButton>(1);
						if (opt.ItemCount > 0 && opt.Selected >= 0)
						{
							var res = ResourceByName(opt.GetItemText(opt.Selected));
							if (res != null)
								slotData["resource"] = VariantState.ToVariant(res.ToDict());
						}
						slotData["amount"] = AmountNode(cardVc)?.Value ?? 0.0;
					}
					else if (uiElement is HBoxContainer row)
					{
						targetArr = row.GetMeta("io_type").AsString() == "Input" ? inputs : outputs;
						var amtCnt = row.GetChild(1);
						slotData["resource_tag"] = amtCnt.GetMeta("resource_tag");
						slotData["amount"] = AmountNode(amtCnt)?.Value ?? 0.0;
					}

					targetArr?.Add(slotData);
				}
			}
			mapping[funcName] = (inputs, outputs);
		}
		return mapping;
	}

	private static void AddTo(GDict dict, string key, double amount)
	{
		dict[key] = (dict.TryGetValue(key, out var v) ? v.AsDouble() : 0.0) + amount;
	}

	private static GDict SlotSummary(GDict slot, bool includeTag)
	{
		var dStr = new GDict { { "amount", slot["amount"] } };
		if (ResourceData.FromDict(slot["resource"].AsGodotDictionary().AsState()) is { } res)
			dStr["resource"] = res.name;
		if (includeTag && slot["resource_tag"].AsString() != "")
			dStr["resource_tag"] = slot["resource_tag"];
		return dStr;
	}

	private void _recalculate()
	{
		var functions = BuildingManager.Instance.buildingFunctions;
		var funcIo = _gather_func_io();
		var localPool = new GDict();
		var resourceNet = new GDict();

		var assignedInputs = new GDict();
		var assignedOutputs = new GDict();
		var templateResult = new GDict
		{
			{ "name", buildingName.Text },
			{ "functionalities", new GArray(selectedFunctionalities.Select(f => (Variant)f)) },
			{ "assigned_inputs", assignedInputs },
			{ "assigned_outputs", assignedOutputs },
			{ "money_cost", 0.0 },
		};

		foreach (var (fn, io) in funcIo)
		{
			var ins = new GArray();
			var outs = new GArray();
			foreach (var slot in io.inputs)
				ins.Add(SlotSummary(slot.AsGodotDictionary(), true));
			foreach (var slot in io.outputs)
				outs.Add(SlotSummary(slot.AsGodotDictionary(), false));
			assignedInputs[fn] = ins;
			assignedOutputs[fn] = outs;
		}

		var allFeasible = true;
		var tagMatcher = TagMatcher.Create(localPool);

		// Pass 1: Setup Input Sliders, Dropdowns
		foreach (var funcName in selectedFunctionalities)
		{
			var (slottedInputs, slottedOutputs) = funcIo[funcName];
			tagMatcher.SetContext(slottedInputs);

			if (!funcUiContainers.TryGetValue(funcName, out var vbox))
				continue;

			var matchIdxIn = 0;
			var matchIdxOut = 0;
			foreach (var uiElement in vbox.GetChildren())
			{
				if (uiElement is PanelContainer card)
				{
					var prefix = card.GetMeta("io_type").AsString();
					var cardVc = card.GetChild(0);
					var opt = cardVc.GetChild(0).GetChild<OptionButton>(1);
					var targetMatch = opt.GetMeta("match_expr").AsString();
					var lastSel = opt.Selected >= 0 ? opt.GetItemText(opt.Selected) : opt.GetMeta("last_selected").AsString();
					opt.Clear();
					opt.AddItem("...");

					foreach (var resource in MapManager.Instance.resources.Values)
					{
						var res = resource;
						tagMatcher.SetEvalResource(res);
						if (tagMatcher.evaluate(targetMatch).AsBool())
							opt.AddItem(res.name);
					}

					var targetSlots = prefix == "Input" ? slottedInputs : slottedOutputs;
					var targetIndex = prefix == "Input" ? matchIdxIn : matchIdxOut;

					var didReselect = false;
					for (var i = 0; i < opt.ItemCount; i++)
					{
						if (opt.GetItemText(i) != lastSel)
							continue;
						opt.Select(i);
						didReselect = true;
						if (lastSel != "...")
							VariantState.ToVariant(ResourceByName(lastSel).ToDict());
						break;
					}
					if (!didReselect && opt.ItemCount > 1)
					{
						opt.Select(1);
						VariantState.ToVariant(ResourceByName(opt.GetItemText(1)).ToDict());
					}
					opt.SetMeta("last_selected", opt.GetItemText(opt.Selected));

					// Build limit setting for INPUT
					if (prefix == "Input")
					{
						var amtNode = AmountNode(cardVc);
						ApplyLimits(amtNode, tagMatcher);
						slottedInputs[matchIdxIn].AsGodotDictionary()["amount"] = amtNode.Value;
						matchIdxIn++;
					}
					else
					{
						// Output nodes evaluated in pass 2, but we update index
						matchIdxOut++;
					}
				}
				else if (uiElement is HBoxContainer row)
				{
					var amtNode = AmountNode(row.GetChild(1));
					ApplyLimits(amtNode, tagMatcher);
					slottedInputs[matchIdxIn].AsGodotDictionary()["amount"] = amtNode.Value;
					matchIdxIn++;
				}
			}
		}

		// Pass 2: Calculate outputs (Passive and Resource Net)
		foreach (var funcName in selectedFunctionalities)
		{
			var funcData = functions[funcName].AsGodotDictionary();
			var (slottedInputs, slottedOutputs) = funcIo[funcName];
			tagMatcher.SetContext(slottedInputs);

			// Evaluate Output amounts onto UI
			if (funcData.TryGetValue("output", out var outputs))
			{
				funcUiContainers.TryGetValue(funcName, out var vbox);
				var matchIdxOut = 0;
				var cIndex = 0;
				foreach (var outVariant in outputs.AsGodotArray())
				{
					var outCfg = outVariant.AsGodotDictionary();
					var amt = _eval_or_float(outCfg.TryGetValue("amount", out var a) ? a : "0", tagMatcher);

					// Update UI Element
					if (vbox != null)
					{
						foreach (var uiElement in vbox.GetChildren())
						{
							if (uiElement is not PanelContainer card || card.GetMeta("io_type").AsString() != "Output")
								continue;
							if (cIndex == matchIdxOut)
							{
								if (AmountNode(card.GetChild(0)) is SpinBox sp)
									sp.Value = amt;
								slottedOutputs[matchIdxOut].AsGodotDictionary()["amount"] = amt;
								break;
							}
							cIndex++;
						}
					}

					// Track Yield Natively
					if (outCfg.ContainsKey("match"))
					{
						var res = matchIdxOut < slottedOutputs.Count
							? ResourceData.FromDict(slottedOutputs[matchIdxOut].AsGodotDictionary()["resource"].AsGodotDictionary().AsState())
							: null;
						if (res != null)
							AddTo(resourceNet, res.name, amt);
						matchIdxOut++;
					}
					else if (outCfg.TryGetValue("tag", out var tagVariant))
					{
						var tag = tagVariant.AsString();
						if (!templateResult.ContainsKey(tag))
							templateResult[tag] = 0.0;
						if (templateResult[tag].VariantType == Variant.Type.Bool)
							templateResult[tag] = amt > 0.0;
						else
							AddTo(templateResult, tag, amt);
						AddTo(localPool, tag, amt);
					}
				}
			}

			// Cost processing
			if (funcData.TryGetValue("build_cost", out var bcVariant))
			{
				var bc = bcVariant.AsGodotDictionary();
				if (bc.TryGetValue("money", out var money))
					AddTo(templateResult, "money_cost", _eval_or_float(money, tagMatcher));
			}
		}

		// Pass 3: Process inputs footprint globally
		foreach (var funcName in selectedFunctionalities)
		{
			var funcData = functions[funcName].AsGodotDictionary();
			var slotted = funcIo[funcName].inputs;
			tagMatcher.SetContext(slotted);

			if (!funcData.TryGetValue("input", out var inputs))
				continue;

			var inputList = inputs.AsGodotArray();
			for (var matchIndex = 0; matchIndex < inputList.Count; matchIndex++)
			{
				var inp = inputList[matchIndex].AsGodotDictionary();

				if (matchIndex >= slotted.Count)
				{
					allFeasible = false;
					continue;
				}
				var slot = slotted[matchIndex].AsGodotDictionary();
				var userAllocatedAmt = slot["amount"].AsDouble();
				var res = ResourceData.FromDict(slot["resource"].AsGodotDictionary().AsState());

				if (inp.ContainsKey("match"))
				{
					if (res == null)
					{
						allFeasible = false;
						continue;
					}
					tagMatcher.SetEvalResource(res);
					if (!tagMatcher.evaluate(inp["match"].AsString()).AsBool())
						allFeasible = false;
					else
						AddTo(resourceNet, res.name, -userAllocatedAmt);
				}
				else if (inp.ContainsKey("resource_tag"))
				{
					var cTag = inp["resource_tag"].AsString();
					AddTo(localPool, cTag, -userAllocatedAmt);

					if (!templateResult.ContainsKey(cTag))
						templateResult[cTag] = 0.0;
					if (templateResult[cTag].VariantType != Variant.Type.Bool)
						AddTo(templateResult, cTag, -userAllocatedAmt);
				}
			}
		}

		if (selectedFunctionalities.Count == 0 || buildingName.Text.Length == 0)
			allFeasible = false;

		var summaryText = $"Feasible: {(allFeasible ? "true" : "false")}\nCost: {templateResult["moneyCost"].AsDouble()}\n";

		// Dynamically populate passive states that have changed
		foreach (var keyVariant in templateResult.Keys)
		{
			var key = keyVariant.AsString();
			if (RESERVED_KEYS.Contains(key))
				continue;
			var val = templateResult[keyVariant];
			if (val.VariantType is Variant.Type.Float or Variant.Type.Int)
			{
				if (val.AsDouble() != 0.0)
					summaryText += $"{key.Capitalize()}: {val.AsDouble():F1}\n";
			}
			else if (val.VariantType == Variant.Type.Bool && val.AsBool())
			{
				summaryText += $"{key.Capitalize()}: True\n";
			}
		}

		if (resourceNet.Count > 0)
		{
			summaryText += "\nResources Net:\n";
			foreach (var (rName, net) in resourceNet)
			{
				var value = net.AsDouble();
				summaryText += $"- {rName.AsString()}: {(value >= 0 ? "+" : "")}{value:F1}\n";
			}
		}

		templateResult["resource_net"] = resourceNet;
		feasibilityText.Text = summaryText;
		addBuilding.Disabled = !allFeasible;
		currentBuildingTemplate = templateResult;
	}

	private static void ApplyLimits(Range amtNode, TagMatcher tagMatcher)
	{
		if (amtNode is HSlider slider)
		{
			slider.MinValue = _eval_or_float(slider.GetMeta("min_expr"), tagMatcher);
			slider.MaxValue = _eval_or_float(slider.GetMeta("max_expr"), tagMatcher);
		}
		else if (amtNode is SpinBox sp)
		{
			sp.Value = _eval_or_float(sp.GetMeta("fixed_expr"), tagMatcher);
		}
	}

	private static double _eval_or_float(Variant val, TagMatcher tagMatcher)
	{
		switch (val.VariantType)
		{
			case Variant.Type.String:
			{
				var result = tagMatcher.evaluate(val.AsString());
				return result.VariantType is Variant.Type.Float or Variant.Type.Int ? result.AsDouble() : 0.0;
			}
			case Variant.Type.Float:
			case Variant.Type.Int:
				return val.AsDouble();
			default:
				return 0.0;
		}
	}

	public void _on_add_building_pressed()
	{
		var designs = BuildingManager.Instance.buildingDesigns;
		var playerName = CountryManager.Instance.Player.countryName;
		if (!designs.ContainsKey(playerName))
			designs[playerName] = new GDict();

		var bt = BuildingTemplate.FromDict(currentBuildingTemplate.AsState());
		designs[playerName][bt.name] = VariantState.ToVariant(bt.ToDict());

		foreach (var child in existingDesign.GetChildren())
			child.Free();

		foreach (var design in designs[playerName].Values)
		{
			var btn = new Button { Text = BuildingTemplate.FromDict(design.AsGodotDictionary().AsState()).name };
			existingDesign.AddChild(btn);
		}
	}

	public void _on_building_name_changed()
	{
		_recalculate();
	}
}
