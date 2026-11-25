using System.Linq;
using Godot;
using ET = Sosr.InterpreterManager.ExpressionTypes;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace Sosr;

[GlobalClass]
public partial class ExpressionBox : PanelContainer
{
	public enum ElementTypes { Paragraph, Button, Image, Header }

	[Signal] public delegate void changedEventHandler();

	private const string EXPRESSION_SCENE_PATH = "res://Scenes/Expression.tscn";
	private const string CASE_SCENE_PATH = "res://Scenes/Case.tscn";

	private static readonly string[] VARIADIC_FUNCTIONS = ["any", "all", "and", "or", "has_pids"];

	public int expression_type = 0;
	public bool is_child = false;

	[Export] public TabContainer expression_tabs;

	[ExportGroup("Variable")]
	[Export] public TextEdit value_edit;

	[ExportGroup("Array")]
	[Export] public VBoxContainer items_box;

	[ExportGroup("Event")]
	[Export] public VBoxContainer pairs;

	[ExportGroup("Function")]
	[Export] public OptionButton func_options;
	[Export] public VBoxContainer args_box;

	[ExportGroup("Loop")]
	[Export] public TextEdit for_edit;
	[Export] public VBoxContainer iter_element_box;
	[Export] public VBoxContainer do_box;

	[ExportGroup("Match")]
	[Export] public Container match_expression;
	[Export] public VBoxContainer cases;

	[ExportGroup("Element")]
	[Export] public TabContainer element_tabs;

	[ExportSubgroup("Paragraph")]
	[Export] public Container paragraph_text;

	[ExportSubgroup("Button")]
	[Export] public Container button_text;
	[Export] public Container button_condition;
	[Export] public Container button_finished;

	[ExportSubgroup("Image")]
	[Export] public Container image_path;

	[ExportSubgroup("Header")]
	[Export] public Container country1_name;
	[Export] public Container country2_name;

	[ExportGroup("IdeologyDrift")]
	[Export] public SpinBox drift_amount;
	[Export] public SpinBox final_x;
	[Export] public SpinBox final_y;

	private void EmitChanged() => EmitSignal(SignalName.changed);

	public static ExpressionBox Instantiate()
		=> GD.Load<PackedScene>(EXPRESSION_SCENE_PATH).Instantiate<ExpressionBox>();

	private static Case InstantiateCase()
		=> GD.Load<PackedScene>(CASE_SCENE_PATH).Instantiate<Case>();

	private static GArray ChildDicts(Node container)
		=> new(container.GetChildren().Select(c => ((ExpressionBox)c).ToDict()));

	private static Variant FirstChildDict(Node container)
		=> ((ExpressionBox)container.GetChild(0)).ToDict();

	public Variant ToDict()
	{
		switch ((ET)expression_tabs.CurrentTab)
		{
			case ET.Variable:
				return value_edit.Text.IsValidFloat() ? value_edit.Text.ToFloat() : value_edit.Text;

			case ET.List:
				return ChildDicts(items_box);

			case ET.Event:
			{
				var fish = new GDict();
				foreach (var pair in pairs.GetChildren())
					fish.Merge(((Case)pair).ToDict());
				return fish;
			}

			case ET.Function:
				return new GDict
				{
					{ "func", func_options.Text },
					{ "args", ChildDicts(args_box) },
				};

			case ET.Loop:
			{
				Variant in_val = ChildDicts(iter_element_box);
				if (iter_element_box.GetChildCount() == 1)
				{
					var first = (ExpressionBox)iter_element_box.GetChild(0);
					if (first.expression_type == (int)ET.List)
					{
						in_val = first.ToDict();
					}
					else if (first.expression_type == (int)ET.Variable)
					{
						var txt = first.ToDict().ToString();
						if (txt.StartsWith("[") && txt.EndsWith("]"))
						{
							var parsed = Json.ParseString(txt);
							if (parsed.VariantType == Variant.Type.Array)
								in_val = parsed;
						}
						else if (txt.Contains(','))
						{
							in_val = txt.Split(",").Select(s => s.StripEdges()).ToArray();
						}
					}
				}
				return new GDict
				{
					{ "for", for_edit.Text },
					{ "in", in_val },
					{ "do", ChildDicts(do_box) },
				};
			}

			case ET.Match:
			{
				var fish = new GDict();
				if (match_expression.GetChildCount() > 0)
					fish["match"] = FirstChildDict(match_expression);

				foreach (var c in cases.GetChildren())
					fish.Merge(((Case)c).ToDict());
				return fish;
			}

			case ET.Element:
				switch ((ElementTypes)element_tabs.CurrentTab)
				{
					case ElementTypes.Paragraph:
						return new GDict
						{
							{ "type", "paragraph" },
							{ "text", FirstChildDict(paragraph_text) },
						};
					case ElementTypes.Button:
						return new GDict
						{
							{ "type", "button" },
							{ "text", FirstChildDict(button_text) },
							{ "condition", FirstChildDict(button_condition) },
							{ "finished", FirstChildDict(button_finished) },
						};
					case ElementTypes.Image:
						return new GDict
						{
							{ "type", "image" },
							{ "text", FirstChildDict(image_path) },
						};
					case ElementTypes.Header:
						return new GDict
						{
							{ "type", "country_header" },
							{ "country1", FirstChildDict(country1_name) },
							{ "country2", FirstChildDict(country2_name) },
						};
				}
				break;

			case ET.IdeologyDrift:
				return new GDict
				{
					{ "drift_amount", drift_amount.Value },
					{ "final_position", new GArray { final_x.Value, final_y.Value } },
				};
		}
		return new Variant();
	}

	public static ExpressionBox FromDict(Variant data, bool _is_child = false)
	{
		var box = Instantiate();
		if (box == null)
			return null;
		box.is_child = _is_child;
		FromDictInPlace(box, data);
		return box;
	}

	private void AddChildExpression(Node container, Variant data)
	{
		var child = FromDict(data, true);
		if (child == null)
			return;
		container.AddChild(child);
		child.changed += EmitChanged;
	}

	private static GArray AsList(Variant value)
		=> value.VariantType == Variant.Type.Array ? value.AsGodotArray() : new GArray { value };

	public static void FromDictInPlace(ExpressionBox box, Variant data)
	{
		if (box == null)
			return;
		box.setup_ui();

		if (data.VariantType == Variant.Type.Array)
		{
			box.expression_tabs.CurrentTab = (int)ET.List;
			foreach (var item in data.AsGodotArray())
				box.AddChildExpression(box.items_box, item);
			return;
		}

		if (data.VariantType != Variant.Type.Dictionary)
		{
			box.expression_tabs.CurrentTab = (int)ET.Variable;
			if (box.value_edit != null)
				box.value_edit.Text = data.ToString();
			return;
		}

		var dict = data.AsGodotDictionary();
		var dummy = InterpreterManager.Instance.DUMMY_FUNC;

		if (dict.ContainsKey("func"))
		{
			box.expression_tabs.CurrentTab = (int)ET.Function;
			var func_name = dict["func"].AsString();
			for (var i = 0; i < box.func_options.ItemCount; i++)
			{
				if (box.func_options.GetItemText(i) == func_name)
				{
					box.func_options.Select(i);
					break;
				}
			}

			foreach (var arg in AsList(dict["args"]))
				box.AddChildExpression(box.args_box, arg);
			box._update_add_args_button();
			box._update_arg_placeholders();
		}
		else if (dict.ContainsKey("for"))
		{
			box.expression_tabs.CurrentTab = (int)ET.Loop;
			if (box.for_edit != null)
				box.for_edit.Text = dict["for"].AsString();
			box.AddChildExpression(box.iter_element_box, dict["in"]);
			foreach (var item in AsList(dict["do"]))
				box.AddChildExpression(box.do_box, item);
		}
		else if (dict.ContainsKey("match"))
		{
			box.expression_tabs.CurrentTab = (int)ET.Match;
			box.AddChildExpression(box.match_expression, dict["match"]);

			foreach (var (key, value) in dict)
			{
				if (key.AsString() == "match")
					continue;
				box._on_add_case_pressed().FromDict(new GDict { { key, value } });
			}
		}
		else if (dict.ContainsKey("event"))
		{
			box.expression_tabs.CurrentTab = (int)ET.Event;
			foreach (var (key, value) in dict)
				box._on_add_pair_pressed().FromDict(new GDict { { key, value } });
		}
		else if (dict.ContainsKey("type"))
		{
			box.expression_tabs.CurrentTab = (int)ET.Element;
			box._on_expression_type_tab_selected((int)ET.Element);
			switch (dict["type"].AsString())
			{
				case "paragraph":
					box.element_tabs.CurrentTab = (int)ElementTypes.Paragraph;
					FromDictInPlace(box.paragraph_text.GetChild<ExpressionBox>(0), dict["text"]);
					break;
				case "button":
					box.element_tabs.CurrentTab = (int)ElementTypes.Button;
					FromDictInPlace(box.button_text.GetChild<ExpressionBox>(0), dict["text"]);
					FromDictInPlace(box.button_condition.GetChild<ExpressionBox>(0),
						dict.TryGetValue("condition", out var condition) ? condition : dummy);
					FromDictInPlace(box.button_finished.GetChild<ExpressionBox>(0),
						dict.TryGetValue("finished", out var finished) ? finished : dummy);
					break;
				case "image":
					box.element_tabs.CurrentTab = (int)ElementTypes.Image;
					FromDictInPlace(box.image_path.GetChild<ExpressionBox>(0), dict["text"]);
					break;
				case "country_header":
					box.element_tabs.CurrentTab = (int)ElementTypes.Header;
					FromDictInPlace(box.country1_name.GetChild<ExpressionBox>(0), dict["country1"]);
					FromDictInPlace(box.country2_name.GetChild<ExpressionBox>(0), dict["country2"]);
					break;
			}
		}
		else if (dict.ContainsKey("drift_amount"))
		{
			box.expression_tabs.CurrentTab = (int)ET.IdeologyDrift;
			box._on_expression_type_tab_selected((int)ET.IdeologyDrift);
			box.drift_amount.Value = dict["drift_amount"].AsDouble();
			var final_position = dict["final_position"].AsGodotArray();
			box.final_x.Value = final_position[0].AsDouble();
			box.final_y.Value = final_position[1].AsDouble();
		}
	}

	public void setup_ui()
	{
		if (func_options.ItemCount == 0)
		{
			foreach (var function in InterpreterManager.Instance.functions.Keys)
				func_options.AddItem(function.AsString());
		}

		expression_tabs.SetTabHidden((int)ET.Close, !is_child);
	}

	public override void _Ready()
	{
		setup_ui();

		// Connect change signals
		expression_tabs.TabSelected += _ => EmitChanged();
		func_options.ItemSelected += index => _on_func_selected((int)index);
		if (value_edit != null)
			value_edit.TextChanged += EmitChanged;
		if (for_edit != null)
			for_edit.TextChanged += EmitChanged;
		if (args_box != null)
		{
			args_box.ChildEnteredTree += _ => _update_add_args_button();
			args_box.ChildExitingTree += _ => Callable.From(_update_add_args_button).CallDeferred();
		}
	}

	private string SelectedFunction
		=> func_options.Selected != -1 ? func_options.GetItemText(func_options.Selected) : "";

	public void _update_add_args_button()
	{
		var func_name = SelectedFunction;
		var is_variadic = VARIADIC_FUNCTIONS.Contains(func_name);
		var functions = InterpreterManager.Instance.functions;
		var expected_args = functions.TryGetValue(func_name, out var args) ? args.AsGodotArray().Count : 0;

		if (args_box.GetParent().GetNodeOrNull<Button>("AddArgs/AddArgsButton") is { } add_args_btn)
			add_args_btn.Disabled = !is_variadic && args_box.GetChildCount() >= expected_args;
	}

	public void _update_arg_placeholders()
	{
		var func_name = SelectedFunction;
		var index = 0;
		foreach (var child in args_box.GetChildren())
		{
			if (child.IsQueuedForDeletion())
				continue;
			if (child is ExpressionBox box && box.value_edit != null)
				box.value_edit.PlaceholderText = InterpreterManager.Instance.get_param_name(func_name, index);
			index++;
		}
	}

	private static void _add_inital_expression(Container box)
	{
		if (box.GetChildCount() > 0)
			return;
		var expr = Instantiate();
		expr.expression_tabs.CurrentTab = (int)ET.Variable;
		expr.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		expr.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		box.AddChild(expr);
	}

	public void _on_expression_type_tab_selected(long tab)
	{
		switch ((ET)tab)
		{
			case ET.Close:
				QueueFree();
				break;
			case ET.Match:
				_add_inital_expression(match_expression);
				break;
			case ET.Element:
				_add_inital_expression(paragraph_text);
				_add_inital_expression(button_text);
				_add_inital_expression(button_condition);
				_add_inital_expression(button_finished);
				_add_inital_expression(country1_name);
				_add_inital_expression(country2_name);
				_add_inital_expression(image_path);
				break;
			default:
				expression_type = (int)tab;
				break;
		}
		EmitChanged();
	}

	public ExpressionBox add_expression(Node container)
	{
		var expr = Instantiate();
		if (expr == null)
			return null;

		expr.is_child = true;
		container.AddChild(expr);
		expr.changed += EmitChanged;
		EmitChanged();
		return expr;
	}

	private void _on_func_selected(int index)
	{
		var func_name = func_options.GetItemText(index);
		var functions = InterpreterManager.Instance.functions;
		if (functions.TryGetValue(func_name, out var arg_types))
		{
			// Clear existing args
			foreach (var child in args_box.GetChildren())
				child.QueueFree();

			// Auto-fill
			foreach (var type in arg_types.AsGodotArray())
			{
				var expr = add_expression(args_box);
				if (expr == null)
					continue;
				expr.expression_tabs.CurrentTab = type.AsInt32();
				expr._on_expression_type_tab_selected(type.AsInt32());
			}
		}

		_update_add_args_button();
		_update_arg_placeholders();
		EmitChanged();
	}

	public void _on_add_args_button_pressed()
	{
		add_expression(args_box);
		_update_arg_placeholders();
	}

	public void _on_add_match_pressed() => add_expression(match_expression);

	public void _on_add_iter_element_button_pressed() => add_expression(iter_element_box);

	public void _on_add_do_button_pressed() => add_expression(do_box);

	public Case _on_add_case_pressed()
	{
		var c = InstantiateCase();
		cases.AddChild(c);
		c.changed += EmitChanged;
		EmitChanged();
		return c;
	}

	public Case _on_add_pair_pressed()
	{
		var pair = InstantiateCase();
		pairs.AddChild(pair);
		pair.changed += EmitChanged;
		EmitChanged();
		return pair;
	}

	public void _on_add_item_button_pressed() => add_expression(items_box);
}
