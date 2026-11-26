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

	public int expressionType = 0;
	public bool isChild = false;

	[Export] public TabContainer expressionTabs;

	[ExportGroup("Variable")]
	[Export] public TextEdit valueEdit;

	[ExportGroup("Array")]
	[Export] public VBoxContainer itemsBox;

	[ExportGroup("Event")]
	[Export] public VBoxContainer pairs;

	[ExportGroup("Function")]
	[Export] public OptionButton funcOptions;
	[Export] public VBoxContainer argsBox;

	[ExportGroup("Loop")]
	[Export] public TextEdit forEdit;
	[Export] public VBoxContainer iterElementBox;
	[Export] public VBoxContainer doBox;

	[ExportGroup("Match")]
	[Export] public Container matchExpression;
	[Export] public VBoxContainer cases;

	[ExportGroup("Element")]
	[Export] public TabContainer elementTabs;

	[ExportSubgroup("Paragraph")]
	[Export] public Container paragraphText;

	[ExportSubgroup("Button")]
	[Export] public Container buttonText;
	[Export] public Container buttonCondition;
	[Export] public Container buttonFinished;

	[ExportSubgroup("Image")]
	[Export] public Container imagePath;

	[ExportSubgroup("Header")]
	[Export] public Container country1Name;
	[Export] public Container country2Name;

	[ExportGroup("IdeologyDrift")]
	[Export] public SpinBox driftAmount;
	[Export] public SpinBox finalX;
	[Export] public SpinBox finalY;

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
		switch ((ET)expressionTabs.CurrentTab)
		{
			case ET.Variable:
				return valueEdit.Text.IsValidFloat() ? valueEdit.Text.ToFloat() : valueEdit.Text;

			case ET.List:
				return ChildDicts(itemsBox);

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
					{ "func", funcOptions.Text },
					{ "args", ChildDicts(argsBox) },
				};

			case ET.Loop:
			{
				Variant inVal = ChildDicts(iterElementBox);
				if (iterElementBox.GetChildCount() == 1)
				{
					var first = (ExpressionBox)iterElementBox.GetChild(0);
					if (first.expressionType == (int)ET.List)
					{
						inVal = first.ToDict();
					}
					else if (first.expressionType == (int)ET.Variable)
					{
						var txt = first.ToDict().ToString();
						if (txt.StartsWith("[") && txt.EndsWith("]"))
						{
							var parsed = Json.ParseString(txt);
							if (parsed.VariantType == Variant.Type.Array)
								inVal = parsed;
						}
						else if (txt.Contains(','))
						{
							inVal = txt.Split(",").Select(s => s.StripEdges()).ToArray();
						}
					}
				}
				return new GDict
				{
					{ "for", forEdit.Text },
					{ "in", inVal },
					{ "do", ChildDicts(doBox) },
				};
			}

			case ET.Match:
			{
				var fish = new GDict();
				if (matchExpression.GetChildCount() > 0)
					fish["match"] = FirstChildDict(matchExpression);

				foreach (var c in cases.GetChildren())
					fish.Merge(((Case)c).ToDict());
				return fish;
			}

			case ET.Element:
				switch ((ElementTypes)elementTabs.CurrentTab)
				{
					case ElementTypes.Paragraph:
						return new GDict
						{
							{ "type", "paragraph" },
							{ "text", FirstChildDict(paragraphText) },
						};
					case ElementTypes.Button:
						return new GDict
						{
							{ "type", "button" },
							{ "text", FirstChildDict(buttonText) },
							{ "condition", FirstChildDict(buttonCondition) },
							{ "finished", FirstChildDict(buttonFinished) },
						};
					case ElementTypes.Image:
						return new GDict
						{
							{ "type", "image" },
							{ "text", FirstChildDict(imagePath) },
						};
					case ElementTypes.Header:
						return new GDict
						{
							{ "type", "country_header" },
							{ "country1", FirstChildDict(country1Name) },
							{ "country2", FirstChildDict(country2Name) },
						};
				}
				break;

			case ET.IdeologyDrift:
				return new GDict
				{
					{ "drift_amount", driftAmount.Value },
					{ "final_position", new GArray { finalX.Value, finalY.Value } },
				};
		}
		return new Variant();
	}

	public static ExpressionBox FromDict(Variant data, bool _is_child = false)
	{
		var box = Instantiate();
		if (box == null)
			return null;
		box.isChild = _is_child;
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
		box.SetupUi();

		if (data.VariantType == Variant.Type.Array)
		{
			box.expressionTabs.CurrentTab = (int)ET.List;
			foreach (var item in data.AsGodotArray())
				box.AddChildExpression(box.itemsBox, item);
			return;
		}

		if (data.VariantType != Variant.Type.Dictionary)
		{
			box.expressionTabs.CurrentTab = (int)ET.Variable;
			if (box.valueEdit != null)
				box.valueEdit.Text = data.ToString();
			return;
		}

		var dict = data.AsGodotDictionary();
		var dummy = InterpreterManager.Instance.DUMMY_FUNC;

		if (dict.ContainsKey("func"))
		{
			box.expressionTabs.CurrentTab = (int)ET.Function;
			var funcName = dict["func"].AsString();
			for (var i = 0; i < box.funcOptions.ItemCount; i++)
			{
				if (box.funcOptions.GetItemText(i) == funcName)
				{
					box.funcOptions.Select(i);
					break;
				}
			}

			foreach (var arg in AsList(dict["args"]))
				box.AddChildExpression(box.argsBox, arg);
			box._update_add_args_button();
			box._update_arg_placeholders();
		}
		else if (dict.ContainsKey("for"))
		{
			box.expressionTabs.CurrentTab = (int)ET.Loop;
			if (box.forEdit != null)
				box.forEdit.Text = dict["for"].AsString();
			box.AddChildExpression(box.iterElementBox, dict["in"]);
			foreach (var item in AsList(dict["do"]))
				box.AddChildExpression(box.doBox, item);
		}
		else if (dict.ContainsKey("match"))
		{
			box.expressionTabs.CurrentTab = (int)ET.Match;
			box.AddChildExpression(box.matchExpression, dict["match"]);

			foreach (var (key, value) in dict)
			{
				if (key.AsString() == "match")
					continue;
				box._on_add_case_pressed().FromDict(new GDict { { key, value } });
			}
		}
		else if (dict.ContainsKey("event"))
		{
			box.expressionTabs.CurrentTab = (int)ET.Event;
			foreach (var (key, value) in dict)
				box._on_add_pair_pressed().FromDict(new GDict { { key, value } });
		}
		else if (dict.ContainsKey("type"))
		{
			box.expressionTabs.CurrentTab = (int)ET.Element;
			box._on_expression_type_tab_selected((int)ET.Element);
			switch (dict["type"].AsString())
			{
				case "paragraph":
					box.elementTabs.CurrentTab = (int)ElementTypes.Paragraph;
					FromDictInPlace(box.paragraphText.GetChild<ExpressionBox>(0), dict["text"]);
					break;
				case "button":
					box.elementTabs.CurrentTab = (int)ElementTypes.Button;
					FromDictInPlace(box.buttonText.GetChild<ExpressionBox>(0), dict["text"]);
					FromDictInPlace(box.buttonCondition.GetChild<ExpressionBox>(0),
						dict.TryGetValue("condition", out var condition) ? condition : dummy);
					FromDictInPlace(box.buttonFinished.GetChild<ExpressionBox>(0),
						dict.TryGetValue("finished", out var finished) ? finished : dummy);
					break;
				case "image":
					box.elementTabs.CurrentTab = (int)ElementTypes.Image;
					FromDictInPlace(box.imagePath.GetChild<ExpressionBox>(0), dict["text"]);
					break;
				case "country_header":
					box.elementTabs.CurrentTab = (int)ElementTypes.Header;
					FromDictInPlace(box.country1Name.GetChild<ExpressionBox>(0), dict["country1"]);
					FromDictInPlace(box.country2Name.GetChild<ExpressionBox>(0), dict["country2"]);
					break;
			}
		}
		else if (dict.ContainsKey("drift_amount"))
		{
			box.expressionTabs.CurrentTab = (int)ET.IdeologyDrift;
			box._on_expression_type_tab_selected((int)ET.IdeologyDrift);
			box.driftAmount.Value = dict["drift_amount"].AsDouble();
			var finalPosition = dict["final_position"].AsGodotArray();
			box.finalX.Value = finalPosition[0].AsDouble();
			box.finalY.Value = finalPosition[1].AsDouble();
		}
	}

	public void SetupUi()
	{
		if (funcOptions.ItemCount == 0)
		{
			foreach (var function in InterpreterManager.Instance.functions.Keys)
				funcOptions.AddItem(function.AsString());
		}

		expressionTabs.SetTabHidden((int)ET.Close, !isChild);
	}

	public override void _Ready()
	{
		SetupUi();

		// Connect change signals
		expressionTabs.TabSelected += _ => EmitChanged();
		funcOptions.ItemSelected += index => _on_func_selected((int)index);
		if (valueEdit != null)
			valueEdit.TextChanged += EmitChanged;
		if (forEdit != null)
			forEdit.TextChanged += EmitChanged;
		if (argsBox != null)
		{
			argsBox.ChildEnteredTree += _ => _update_add_args_button();
			argsBox.ChildExitingTree += _ => Callable.From(_update_add_args_button).CallDeferred();
		}
	}

	private string SelectedFunction
		=> funcOptions.Selected != -1 ? funcOptions.GetItemText(funcOptions.Selected) : "";

	public void _update_add_args_button()
	{
		var funcName = SelectedFunction;
		var isVariadic = VARIADIC_FUNCTIONS.Contains(funcName);
		var functions = InterpreterManager.Instance.functions;
		var expectedArgs = functions.TryGetValue(funcName, out var args) ? args.AsGodotArray().Count : 0;

		if (argsBox.GetParent().GetNodeOrNull<Button>("AddArgs/AddArgsButton") is { } addArgsBtn)
			addArgsBtn.Disabled = !isVariadic && argsBox.GetChildCount() >= expectedArgs;
	}

	public void _update_arg_placeholders()
	{
		var funcName = SelectedFunction;
		var index = 0;
		foreach (var child in argsBox.GetChildren())
		{
			if (child.IsQueuedForDeletion())
				continue;
			if (child is ExpressionBox box && box.valueEdit != null)
				box.valueEdit.PlaceholderText = InterpreterManager.Instance.GetParamName(funcName, index);
			index++;
		}
	}

	private static void _add_inital_expression(Container box)
	{
		if (box.GetChildCount() > 0)
			return;
		var expr = Instantiate();
		expr.expressionTabs.CurrentTab = (int)ET.Variable;
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
				_add_inital_expression(matchExpression);
				break;
			case ET.Element:
				_add_inital_expression(paragraphText);
				_add_inital_expression(buttonText);
				_add_inital_expression(buttonCondition);
				_add_inital_expression(buttonFinished);
				_add_inital_expression(country1Name);
				_add_inital_expression(country2Name);
				_add_inital_expression(imagePath);
				break;
			default:
				expressionType = (int)tab;
				break;
		}
		EmitChanged();
	}

	public ExpressionBox AddExpression(Node container)
	{
		var expr = Instantiate();
		if (expr == null)
			return null;

		expr.isChild = true;
		container.AddChild(expr);
		expr.changed += EmitChanged;
		EmitChanged();
		return expr;
	}

	private void _on_func_selected(int index)
	{
		var funcName = funcOptions.GetItemText(index);
		var functions = InterpreterManager.Instance.functions;
		if (functions.TryGetValue(funcName, out var argTypes))
		{
			// Clear existing args
			foreach (var child in argsBox.GetChildren())
				child.QueueFree();

			// Auto-fill
			foreach (var type in argTypes.AsGodotArray())
			{
				var expr = AddExpression(argsBox);
				if (expr == null)
					continue;
				expr.expressionTabs.CurrentTab = type.AsInt32();
				expr._on_expression_type_tab_selected(type.AsInt32());
			}
		}

		_update_add_args_button();
		_update_arg_placeholders();
		EmitChanged();
	}

	public void _on_add_args_button_pressed()
	{
		AddExpression(argsBox);
		_update_arg_placeholders();
	}

	public void _on_add_match_pressed() => AddExpression(matchExpression);

	public void _on_add_iter_element_button_pressed() => AddExpression(iterElementBox);

	public void _on_add_do_button_pressed() => AddExpression(doBox);

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

	public void _on_add_item_button_pressed() => AddExpression(itemsBox);
}
