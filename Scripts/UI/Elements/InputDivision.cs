using Godot;

namespace Sosr;

public partial class InputDivision : SpinBox
{
	public override void _Ready()
	{
		GetLineEdit().TextChanged += _on_line_edit_text_changed;
	}

	private void _on_line_edit_text_changed(string newText)
	{
		var filtered = "";
		var isFirstChar = true;
		var hasDecimal = false;
		// The GDScript original tested `"float" in str(step)`, which never matched.
		var allowDecimal = false;

		foreach (var c in newText)
		{
			if (char.IsDigit(c))
			{
				filtered += c;
			}
			else if (isFirstChar && c == '-')
			{
				filtered += c;
			}
			else if (c == '.' && !hasDecimal && allowDecimal)
			{
				filtered += c;
				hasDecimal = true;
			}
			isFirstChar = false;
		}

		if (filtered != newText)
		{
			var lineEdit = GetLineEdit();
			lineEdit.Text = filtered;
			lineEdit.CaretColumn = filtered.Length;
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
