using Godot;
using Sosr;

namespace Sosr;

// Port of Scripts/division_item.gd. Scene-attached Control script; no [GlobalClass].
public partial class DivisionItem : Control
{
	[Signal]
	public delegate void ClickedEventHandler(GodotObject cardNode, Variant data);

	[Export] public Button disband;

	private TextureRect texture_rect;
	private Label label_division;
	private Label label_attack;
	private Label label_defense;
	private Label label_experience;
	private ProgressBar progress_bar;

	// Can be DivisionData OR Array[DivisionData].
	public Variant data_payload;
	public bool is_selected = false;

	private static readonly Color ColorNormal = new Color(0.6f, 0.6f, 0.6f, 0.9f);
	private static readonly Color ColorSelected = new Color(0.2f, 0.6f, 0.9f, 1.0f);
	private static readonly Color ColorHover = new Color(0.8f, 0.8f, 0.8f, 0.95f);

	public override void _Ready()
	{
		texture_rect = GetNode<TextureRect>("VBoxContainer/HBoxContainer2/TextureRect");
		label_division = GetNode<Label>("VBoxContainer/HBoxContainer2/label_division");
		label_attack = GetNode<Label>("VBoxContainer/HBoxContainer2/label_attack");
		label_defense = GetNode<Label>("VBoxContainer/HBoxContainer/label_defense");
		label_experience = GetNode<Label>("VBoxContainer/HBoxContainer/label_experience");
		progress_bar = GetNode<ProgressBar>("VBoxContainer/HBoxContainer/ProgressBar");

		// Generated C# signal name is "Clicked"; keep the GDScript name for existing call sites.
		if (!HasSignal("clicked"))
		{
			AddUserSignal("clicked", new Godot.Collections.Array
			{
				new Godot.Collections.Dictionary
				{
					{ "name", "card_node" },
					{ "type", (int)Variant.Type.Object },
				},
				new Godot.Collections.Dictionary
				{
					{ "name", "associated_data" },
					{ "type", (int)Variant.Type.Nil },
				},
			});
		}

		MouseFilter = Control.MouseFilterEnum.Stop;
		foreach (var child in GetChildren())
		{
			if (child is Control control)
				control.MouseFilter = Control.MouseFilterEnum.Ignore;
		}
	}

	private void EmitClicked()
	{
		EmitSignal(SignalName.Clicked, this, data_payload);
		if (HasSignal("clicked"))
			EmitSignal("clicked", this, data_payload);
	}

	// Setup for the new Grouped View.
	public void setup_grouped(TroopData aTroop, string type, Godot.Collections.Array divisions, bool currentlySelected)
	{
		data_payload = divisions;
		is_selected = currentlySelected;

		if (disband != null)
		{
			disband.Pressed += () =>
			{
				TroopManager.Instance.RemoveTroop(aTroop);
				QueueFree();
			};
		}

		var count = divisions.Count;
		label_division.Text = $"{count}x {type.Capitalize()}";

		// Calculate Group Averages.
		var totalAtk = 0.0f;
		var totalDef = 0.0f;
		var totalHp = 0.0f;
		var totalExp = 0.0f;

		foreach (var divisionValue in divisions)
		{
			if (divisionValue.AsGodotObject() is not DivisionData d)
				continue;
			totalAtk += d.get_attack_power();
			totalDef += d.get_defense_power();
			totalHp += d.hp;
			totalExp += d.experience;
		}

		label_attack.Text = ((int)(totalAtk / count)).ToString();
		label_defense.Text = ((int)(totalDef / count)).ToString();
		label_experience.Text = $"{((int)((totalExp / count) * 100))}%";
		progress_bar.Value = totalHp / count;

		var iconPath = $"res://assets/icons/hoi4/{type.ToLowerInvariant()}.png";
		if (ResourceLoader.Exists(iconPath))
			texture_rect.Texture = GD.Load<Texture2D>(iconPath);

		update_visuals();
	}

	public void update_visuals()
	{
		Modulate = is_selected ? ColorSelected : ColorNormal;
	}

	public void _on_mouse_entered()
	{
		if (!is_selected)
			Modulate = ColorHover;
	}

	public void _on_mouse_exited()
	{
		update_visuals();
	}

	public void _on_gui_input(InputEvent @event)
	{
		if (@event is InputEventMouseButton button && button.Pressed && button.ButtonIndex == MouseButton.Left)
		{
			EmitClicked();
			GetViewport().SetInputAsHandled();
		}
	}
}
