using Godot;

namespace Sosr;

public partial class Case : FoldableContainer
{
	[Signal] public delegate void changedEventHandler();

	[Export] public VBoxContainer block;
	[Export] public TextEdit case_edit;

	private bool _text_hooked = false;

	private void EmitChanged() => EmitSignal(SignalName.changed);

	public void FromDict(Godot.Collections.Dictionary data)
	{
		var key = data.Keys.GetEnumerator();
		key.MoveNext();
		var first = key.Current;
		if (case_edit != null)
			case_edit.Text = first.ToString();
		var value = data[first];
		var arr = value.VariantType == Variant.Type.Array
			? value.AsGodotArray()
			: new Godot.Collections.Array { value };
		foreach (var expr_data in arr)
		{
			var expr = ExpressionBox.FromDict(expr_data, true);
			block.AddChild(expr);
			expr.changed += EmitChanged;
		}
		EmitChanged();

		if (!_text_hooked)
		{
			case_edit.TextChanged += EmitChanged;
			_text_hooked = true;
		}
	}

	public Godot.Collections.Dictionary ToDict()
	{
		var list = new Godot.Collections.Array();
		foreach (var child in block.GetChildren())
			list.Add(((ExpressionBox)child).ToDict());
		return new Godot.Collections.Dictionary { { case_edit.Text, list } };
	}

	public ExpressionBox _on_add_function_pressed()
	{
		var expr = ExpressionBox.Instantiate();
		expr.is_child = true;
		block.AddChild(expr);
		expr.changed += EmitChanged;
		EmitChanged();
		return expr;
	}

	public void _on_close_pressed()
	{
		QueueFree();
	}

	public void _on_case_entry_text_changed()
	{
		Title = case_edit.Text;
	}
}
