using Godot;

namespace Sosr;

public partial class InputDivision : SpinBox
{
	public override void _Ready()
	{
		GetLineEdit().TextChanged += _on_line_edit_text_changed;
	}

	private void _on_line_edit_text_changed(string new_text)
	{
		var filtered = "";
		var is_first_char = true;
		var has_decimal = false;
		// The GDScript original tested `"float" in str(step)`, which never matched.
		var allow_decimal = false;

		foreach (var c in new_text)
		{
			if (char.IsDigit(c))
			{
				filtered += c;
			}
			else if (is_first_char && c == '-')
			{
				filtered += c;
			}
			else if (c == '.' && !has_decimal && allow_decimal)
			{
				filtered += c;
				has_decimal = true;
			}
			is_first_char = false;
		}

		if (filtered != new_text)
		{
			var line_edit = GetLineEdit();
			line_edit.Text = filtered;
			line_edit.CaretColumn = filtered.Length;
		}

		if (filtered != "" && filtered != "-")
		{
			var val = filtered.ToFloat();
			if (val > MaxValue)
			{
				val = (float)MaxValue;
				filtered = ((int)val).ToString();
				GetLineEdit().Text = filtered;
			}

			if (val != Value)
			{
				Value = val;
				GetLineEdit().CaretColumn = filtered.Length;
			}
		}
	}
}
