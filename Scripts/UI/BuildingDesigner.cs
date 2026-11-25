using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace Sosr;

public partial class BuildingDesigner : PanelContainer
{
	[Export] public VBoxContainer functions_list;
	[Export] public GridContainer functions_grid;
	[Export] public TextEdit building_name;
	[Export] public Label feasibility_text;
	[Export] public Button add_building;
	[Export] public VBoxContainer existing_design;

	public GDict current_building_template = new();

	public readonly List<string> selected_functionalities = new();

	// Maps functionality names to their specific VBoxContainers containing their OptionButtons/Sliders
	private readonly Dictionary<string, VBoxContainer> func_ui_containers = new();

	private readonly TagMatcher active_tag_matcher = TagMatcher.Create(new GDict());

	private static readonly HashSet<string> RESERVED_KEYS =
		["name", "functionalities", "assigned_inputs", "assigned_outputs", "money_cost"];

	public override void _Ready()
	{
		var functions = EconomyManager.Instance.building_functions;
		foreach (var key in functions.Keys)
		{
			var function_name = key.AsString();
			var func_data = functions[key].AsGodotDictionary();
			var cb = new CheckButton { Text = function_name };
			cb.Toggled += toggled_on =>
			{
				if (toggled_on)
				{
					if (!selected_functionalities.Contains(function_name))
					{
						selected_functionalities.Add(function_name);
						_spawn_func_ui(function_name, func_data);
					}
				}
				else
				{
					selected_functionalities.Remove(function_name);
					_destroy_func_ui(function_name);
				}
				_recalculate();
			};
			inputs_list_or_grid().AddChild(cb);
		}
	}

	public Container inputs_list_or_grid() => functions_list != null ? functions_list : functions_grid;

	private static Range AmountNode(Node holder) => holder.GetMeta("amount_node").As<Range>();

	private void _spawn_func_ui(string func_name, GDict func_data)
	{
		var vbox = new VBoxContainer();

		if (func_data.TryGetValue("input", out var inputs))
		{
			var match_index = 0;
			foreach (var _ in inputs.AsGodotArray())
				active_tag_matcher.slotted_resources.Add(new GDict());

			foreach (var inpVariant in inputs.AsGodotArray())
			{
				var inp = inpVariant.AsGodotDictionary();
				if (inp.ContainsKey("match"))
				{
					_build_match_ui(vbox, inp, "Input", match_index);
					match_index++;
				}
				else if (inp.ContainsKey("resource_tag"))
				{
					_build_tag_ui(vbox, inp, "Input", match_index);
					match_index++;
				}
			}
		}

		if (func_data.TryGetValue("output", out var outputs))
		{
			var match_index = 0;
			foreach (var outVariant in outputs.AsGodotArray())
			{
				var output = outVariant.AsGodotDictionary();
				if (output.ContainsKey("match"))
				{
					// For output, we just build a match card but amount is non-editable
					_build_match_ui(vbox, output, "Output", match_index);
					match_index++;
				}
			}
		}

		func_ui_containers[func_name] = vbox;

		var list = inputs_list_or_grid();
		for (var i = 0; i < list.GetChildCount(); i++)
		{
			if (list.GetChild(i) is CheckButton c && c.Text == func_name)
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
		var card_vbox = new VBoxContainer();
		card.SetMeta("io_type", prefix);

		var hbox_opt = new HBoxContainer();
		var lbl = new Label { Text = $"{prefix} {index}" };
		var opt = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		opt.AddItem("...");
		opt.SetMeta("match_expr", block["match"]);
		opt.SetMeta("last_selected", "");
		opt.ItemSelected += _ => _recalculate();

		hbox_opt.AddChild(lbl);
		hbox_opt.AddChild(opt);
		card_vbox.AddChild(hbox_opt);

		var amount_data = block.TryGetValue("amount", out var amount) ? amount : "0";
		// If it's an output, it's evaluated, so we force it to basically be a spinbox
		_build_amount_ui(card_vbox, amount_data, prefix != "Output");

		card.AddChild(card_vbox);
		vbox.AddChild(card);
	}

	private void _build_tag_ui(Container vbox, GDict block, string prefix, int _index)
	{
		var r_tag = block["resource_tag"].AsString();
		var hbox = new HBoxContainer();
		hbox.SetMeta("io_type", prefix);
		var lbl = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Text = "Pool Req: " + r_tag,
		};
		hbox.AddChild(lbl);

		var amount_data = block.TryGetValue("amount", out var amount) ? amount : "0";
		var amount_cnt = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		amount_cnt.SetMeta("resource_tag", r_tag);

		_build_amount_ui(amount_cnt, amount_data, prefix == "Input");
		hbox.AddChild(amount_cnt);
		vbox.AddChild(hbox);
	}

	private void _build_amount_ui(Container parent, Variant amount_data, bool editable)
	{
		if (amount_data.VariantType == Variant.Type.Dictionary && editable)
		{
			var range = amount_data.AsGodotDictionary();
			var hsl = new HSlider { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Step = 0.1 };
			hsl.SetMeta("min_expr", range.TryGetValue("min", out var min) ? min : "0");
			hsl.SetMeta("max_expr", range.TryGetValue("max", out var max) ? max : "0");

			var lbl = new Label { Text = "Amt: 0.0" };
			hsl.ValueChanged += v =>
			{
				lbl.Text = $"Amt: {v:F1}";
				_recalculate();
			};

			var amt_hbox = new HBoxContainer();
			amt_hbox.AddChild(hsl);
			amt_hbox.AddChild(lbl);

			parent.AddChild(amt_hbox);
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
			sp.SetMeta("fixed_expr", amount_data.ToString());

			var amt_hbox = new HBoxContainer();
			amt_hbox.AddChild(new Label { Text = "Amt:" });
			amt_hbox.AddChild(sp);

			parent.AddChild(amt_hbox);
			parent.SetMeta("amount_node", sp);
		}
	}

	private void _destroy_func_ui(string func_name)
	{
		if (func_ui_containers.Remove(func_name, out var vbox))
			vbox.QueueFree();
	}

	private static ResourceData ResourceByName(string res_name)
	{
		var resources = MapManager.Instance.resources;
		return resources.TryGetValue(res_name, out var r) ? r.As<ResourceData>() : null;
	}

	// Gathers chosen values and resources mapping for evaluating dynamic data.
	// Each slot is a Dictionary { resource: ResourceData|null, amount: float, resource_tag: String },
	// the shape TagMatcher.set_context expects.
	private Dictionary<string, (GArray inputs, GArray outputs)> _gather_func_io()
	{
		var mapping = new Dictionary<string, (GArray inputs, GArray outputs)>();
		foreach (var func_name in selected_functionalities)
		{
			var inputs = new GArray();
			var outputs = new GArray();
			if (func_ui_containers.TryGetValue(func_name, out var vbox))
			{
				foreach (var ui_element in vbox.GetChildren())
				{
					var slot_data = new GDict { { "resource", new Variant() }, { "amount", 0.0 }, { "resource_tag", "" } };
					GArray target_arr = null;

					if (ui_element is PanelContainer card)
					{
						target_arr = card.GetMeta("io_type").AsString() == "Input" ? inputs : outputs;
						var card_vc = card.GetChild(0);
						var opt = card_vc.GetChild(0).GetChild<OptionButton>(1);
						if (opt.ItemCount > 0 && opt.Selected >= 0)
						{
							var res = ResourceByName(opt.GetItemText(opt.Selected));
							if (res != null)
								slot_data["resource"] = res;
						}
						slot_data["amount"] = AmountNode(card_vc)?.Value ?? 0.0;
					}
					else if (ui_element is HBoxContainer row)
					{
						target_arr = row.GetMeta("io_type").AsString() == "Input" ? inputs : outputs;
						var amt_cnt = row.GetChild(1);
						slot_data["resource_tag"] = amt_cnt.GetMeta("resource_tag");
						slot_data["amount"] = AmountNode(amt_cnt)?.Value ?? 0.0;
					}

					target_arr?.Add(slot_data);
				}
			}
			mapping[func_name] = (inputs, outputs);
		}
		return mapping;
	}

	private static void AddTo(GDict dict, string key, double amount)
	{
		dict[key] = (dict.TryGetValue(key, out var v) ? v.AsDouble() : 0.0) + amount;
	}

	private static GDict SlotSummary(GDict slot, bool include_tag)
	{
		var d_str = new GDict { { "amount", slot["amount"] } };
		if (slot["resource"].As<ResourceData>() is { } res)
			d_str["resource"] = res.name;
		if (include_tag && slot["resource_tag"].AsString() != "")
			d_str["resource_tag"] = slot["resource_tag"];
		return d_str;
	}

	private void _recalculate()
	{
		var functions = EconomyManager.Instance.building_functions;
		var func_io = _gather_func_io();
		var local_pool = new GDict();
		var resource_net = new GDict();

		var assigned_inputs = new GDict();
		var assigned_outputs = new GDict();
		var template_result = new GDict
		{
			{ "name", building_name.Text },
			{ "functionalities", new GArray(selected_functionalities.Select(f => (Variant)f)) },
			{ "assigned_inputs", assigned_inputs },
			{ "assigned_outputs", assigned_outputs },
			{ "money_cost", 0.0 },
		};

		foreach (var (fn, io) in func_io)
		{
			var ins = new GArray();
			var outs = new GArray();
			foreach (var slot in io.inputs)
				ins.Add(SlotSummary(slot.AsGodotDictionary(), true));
			foreach (var slot in io.outputs)
				outs.Add(SlotSummary(slot.AsGodotDictionary(), false));
			assigned_inputs[fn] = ins;
			assigned_outputs[fn] = outs;
		}

		var all_feasible = true;
		var tag_matcher = TagMatcher.Create(local_pool);

		// Pass 1: Setup Input Sliders, Dropdowns
		foreach (var func_name in selected_functionalities)
		{
			var (slotted_inputs, slotted_outputs) = func_io[func_name];
			tag_matcher.set_context(slotted_inputs);

			if (!func_ui_containers.TryGetValue(func_name, out var vbox))
				continue;

			var match_idx_in = 0;
			var match_idx_out = 0;
			foreach (var ui_element in vbox.GetChildren())
			{
				if (ui_element is PanelContainer card)
				{
					var prefix = card.GetMeta("io_type").AsString();
					var card_vc = card.GetChild(0);
					var opt = card_vc.GetChild(0).GetChild<OptionButton>(1);
					var target_match = opt.GetMeta("match_expr").AsString();
					var last_sel = opt.Selected >= 0 ? opt.GetItemText(opt.Selected) : opt.GetMeta("last_selected").AsString();
					opt.Clear();
					opt.AddItem("...");

					foreach (var resource in MapManager.Instance.resources.Values)
					{
						var res = resource.As<ResourceData>();
						tag_matcher.set_eval_resource(res);
						if (tag_matcher.evaluate(target_match).AsBool())
							opt.AddItem(res.name);
					}

					var target_slots = prefix == "Input" ? slotted_inputs : slotted_outputs;
					var target_index = prefix == "Input" ? match_idx_in : match_idx_out;

					var did_reselect = false;
					for (var i = 0; i < opt.ItemCount; i++)
					{
						if (opt.GetItemText(i) != last_sel)
							continue;
						opt.Select(i);
						did_reselect = true;
						if (last_sel != "...")
							target_slots[target_index].AsGodotDictionary()["resource"] = ResourceByName(last_sel);
						break;
					}
					if (!did_reselect && opt.ItemCount > 1)
					{
						opt.Select(1);
						target_slots[target_index].AsGodotDictionary()["resource"] = ResourceByName(opt.GetItemText(1));
					}
					opt.SetMeta("last_selected", opt.GetItemText(opt.Selected));

					// Build limit setting for INPUT
					if (prefix == "Input")
					{
						var amt_node = AmountNode(card_vc);
						ApplyLimits(amt_node, tag_matcher);
						slotted_inputs[match_idx_in].AsGodotDictionary()["amount"] = amt_node.Value;
						match_idx_in++;
					}
					else
					{
						// Output nodes evaluated in pass 2, but we update index
						match_idx_out++;
					}
				}
				else if (ui_element is HBoxContainer row)
				{
					var amt_node = AmountNode(row.GetChild(1));
					ApplyLimits(amt_node, tag_matcher);
					slotted_inputs[match_idx_in].AsGodotDictionary()["amount"] = amt_node.Value;
					match_idx_in++;
				}
			}
		}

		// Pass 2: Calculate outputs (Passive and Resource Net)
		foreach (var func_name in selected_functionalities)
		{
			var func_data = functions[func_name].AsGodotDictionary();
			var (slotted_inputs, slotted_outputs) = func_io[func_name];
			tag_matcher.set_context(slotted_inputs);

			// Evaluate Output amounts onto UI
			if (func_data.TryGetValue("output", out var outputs))
			{
				func_ui_containers.TryGetValue(func_name, out var vbox);
				var match_idx_out = 0;
				var c_index = 0;
				foreach (var outVariant in outputs.AsGodotArray())
				{
					var out_cfg = outVariant.AsGodotDictionary();
					var amt = _eval_or_float(out_cfg.TryGetValue("amount", out var a) ? a : "0", tag_matcher);

					// Update UI Element
					if (vbox != null)
					{
						foreach (var ui_element in vbox.GetChildren())
						{
							if (ui_element is not PanelContainer card || card.GetMeta("io_type").AsString() != "Output")
								continue;
							if (c_index == match_idx_out)
							{
								if (AmountNode(card.GetChild(0)) is SpinBox sp)
									sp.Value = amt;
								slotted_outputs[match_idx_out].AsGodotDictionary()["amount"] = amt;
								break;
							}
							c_index++;
						}
					}

					// Track Yield Natively
					if (out_cfg.ContainsKey("match"))
					{
						var res = match_idx_out < slotted_outputs.Count
							? slotted_outputs[match_idx_out].AsGodotDictionary()["resource"].As<ResourceData>()
							: null;
						if (res != null)
							AddTo(resource_net, res.name, amt);
						match_idx_out++;
					}
					else if (out_cfg.TryGetValue("tag", out var tagVariant))
					{
						var tag = tagVariant.AsString();
						if (!template_result.ContainsKey(tag))
							template_result[tag] = 0.0;
						if (template_result[tag].VariantType == Variant.Type.Bool)
							template_result[tag] = amt > 0.0;
						else
							AddTo(template_result, tag, amt);
						AddTo(local_pool, tag, amt);
					}
				}
			}

			// Cost processing
			if (func_data.TryGetValue("build_cost", out var bcVariant))
			{
				var bc = bcVariant.AsGodotDictionary();
				if (bc.TryGetValue("money", out var money))
					AddTo(template_result, "money_cost", _eval_or_float(money, tag_matcher));
			}
		}

		// Pass 3: Process inputs footprint globally
		foreach (var func_name in selected_functionalities)
		{
			var func_data = functions[func_name].AsGodotDictionary();
			var slotted = func_io[func_name].inputs;
			tag_matcher.set_context(slotted);

			if (!func_data.TryGetValue("input", out var inputs))
				continue;

			var input_list = inputs.AsGodotArray();
			for (var match_index = 0; match_index < input_list.Count; match_index++)
			{
				var inp = input_list[match_index].AsGodotDictionary();

				if (match_index >= slotted.Count)
				{
					all_feasible = false;
					continue;
				}
				var slot = slotted[match_index].AsGodotDictionary();
				var user_allocated_amt = slot["amount"].AsDouble();
				var res = slot["resource"].As<ResourceData>();

				if (inp.ContainsKey("match"))
				{
					if (res == null)
					{
						all_feasible = false;
						continue;
					}
					tag_matcher.set_eval_resource(res);
					if (!tag_matcher.evaluate(inp["match"].AsString()).AsBool())
						all_feasible = false;
					else
						AddTo(resource_net, res.name, -user_allocated_amt);
				}
				else if (inp.ContainsKey("resource_tag"))
				{
					var c_tag = inp["resource_tag"].AsString();
					AddTo(local_pool, c_tag, -user_allocated_amt);

					if (!template_result.ContainsKey(c_tag))
						template_result[c_tag] = 0.0;
					if (template_result[c_tag].VariantType != Variant.Type.Bool)
						AddTo(template_result, c_tag, -user_allocated_amt);
				}
			}
		}

		if (selected_functionalities.Count == 0 || building_name.Text.Length == 0)
			all_feasible = false;

		var summary_text = $"Feasible: {(all_feasible ? "true" : "false")}\nCost: {template_result["money_cost"].AsDouble()}\n";

		// Dynamically populate passive states that have changed
		foreach (var keyVariant in template_result.Keys)
		{
			var key = keyVariant.AsString();
			if (RESERVED_KEYS.Contains(key))
				continue;
			var val = template_result[keyVariant];
			if (val.VariantType is Variant.Type.Float or Variant.Type.Int)
			{
				if (val.AsDouble() != 0.0)
					summary_text += $"{key.Capitalize()}: {val.AsDouble():F1}\n";
			}
			else if (val.VariantType == Variant.Type.Bool && val.AsBool())
			{
				summary_text += $"{key.Capitalize()}: True\n";
			}
		}

		if (resource_net.Count > 0)
		{
			summary_text += "\nResources Net:\n";
			foreach (var (r_name, net) in resource_net)
			{
				var value = net.AsDouble();
				summary_text += $"- {r_name.AsString()}: {(value >= 0 ? "+" : "")}{value:F1}\n";
			}
		}

		template_result["resource_net"] = resource_net;
		feasibility_text.Text = summary_text;
		add_building.Disabled = !all_feasible;
		current_building_template = template_result;
	}

	private static void ApplyLimits(Range amt_node, TagMatcher tag_matcher)
	{
		if (amt_node is HSlider slider)
		{
			slider.MinValue = _eval_or_float(slider.GetMeta("min_expr"), tag_matcher);
			slider.MaxValue = _eval_or_float(slider.GetMeta("max_expr"), tag_matcher);
		}
		else if (amt_node is SpinBox sp)
		{
			sp.Value = _eval_or_float(sp.GetMeta("fixed_expr"), tag_matcher);
		}
	}

	private static double _eval_or_float(Variant val, TagMatcher tag_matcher)
	{
		switch (val.VariantType)
		{
			case Variant.Type.String:
			{
				var result = tag_matcher.evaluate(val.AsString());
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
		var designs = EconomyManager.Instance.building_designs;
		var player_name = CountryManager.Instance.Player.country_name;
		if (!designs.ContainsKey(player_name))
			designs[player_name] = new GDict();

		var bt = BuildingTemplate.FromDict(current_building_template);
		designs[player_name][bt.name] = bt;

		foreach (var child in existing_design.GetChildren())
			child.Free();

		foreach (var design in designs[player_name].Values)
		{
			var btn = new Button { Text = design.As<BuildingTemplate>().name };
			existing_design.AddChild(btn);
		}
	}

	public void _on_building_name_changed()
	{
		_recalculate();
	}
}
