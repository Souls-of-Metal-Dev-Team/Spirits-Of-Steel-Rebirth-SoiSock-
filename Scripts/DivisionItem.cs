using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

// Port of Scripts/division_item.gd. Scene-attached Control script; no [GlobalClass].
public partial class DivisionItem : Control
{
	[Signal]
	public delegate void ClickedEventHandler(GodotObject cardNode);

	[Export] public Button disband;

	private TextureRect textureRect;
	private Label labelDivision;
	private Label labelAttack;
	private Label labelDefense;
	private Label labelExperience;
	private ProgressBar progressBar;

	// Can be DivisionData OR Array[DivisionData].
	public object? dataPayload;
	public List<DivisionData>? groupDivisions;
	public bool isSelected = false;

	private static readonly Color ColorNormal = new Color(0.6f, 0.6f, 0.6f, 0.9f);
	private static readonly Color ColorSelected = new Color(0.2f, 0.6f, 0.9f, 1.0f);
	private static readonly Color ColorHover = new Color(0.8f, 0.8f, 0.8f, 0.95f);

	public override void _Ready()
	{
		textureRect = GetNode<TextureRect>("VBoxContainer/HBoxContainer2/TextureRect");
		labelDivision = GetNode<Label>("VBoxContainer/HBoxContainer2/label_division");
		labelAttack = GetNode<Label>("VBoxContainer/HBoxContainer2/label_attack");
		labelDefense = GetNode<Label>("VBoxContainer/HBoxContainer/label_defense");
		labelExperience = GetNode<Label>("VBoxContainer/HBoxContainer/label_experience");
		progressBar = GetNode<ProgressBar>("VBoxContainer/HBoxContainer/ProgressBar");

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
		EmitSignal(SignalName.Clicked, this);
	}

	// Setup for the new Grouped View.
	public void SetupGrouped(TroopData aTroop, string type, List<DivisionData> divisions, bool currentlySelected)
	{
		dataPayload = divisions;
		groupDivisions = divisions;
		isSelected = currentlySelected;

		if (disband != null)
		{
			disband.Pressed += () =>
			{
				ActionDispatcher.Instance.Submit(new RemoveTroopAction
				{
					Issuer = aTroop.countryName, Troop = aTroop,
				});
				QueueFree();
			};
		}

		var count = divisions.Count;
		labelDivision.Text = $"{count}x {type.Capitalize()}";

		// Calculate Group Averages.
		var totalAtk = 0.0f;
		var totalDef = 0.0f;
		var totalHp = 0.0f;
		var totalExp = 0.0f;

		foreach (var divisionValue in divisions)
		{
			if (divisionValue is not DivisionData d)
				continue;
			totalAtk += d.GetAttackPower();
			totalDef += d.GetDefensePower();
			totalHp += d.hp;
			totalExp += d.experience;
		}

		labelAttack.Text = ((int)(totalAtk / count)).ToString();
		labelDefense.Text = ((int)(totalDef / count)).ToString();
		labelExperience.Text = $"{((int)((totalExp / count) * 100))}%";
		progressBar.Value = totalHp / count;

		var iconPath = $"res://assets/icons/hoi4/{type.ToLowerInvariant()}.png";
		if (ResourceLoader.Exists(iconPath))
			textureRect.Texture = GD.Load<Texture2D>(iconPath);

		UpdateVisuals();
	}

	public void UpdateVisuals()
	{
		Modulate = isSelected ? ColorSelected : ColorNormal;
	}

	public void _on_mouse_entered()
	{
		if (!isSelected)
			Modulate = ColorHover;
	}

	public void _on_mouse_exited()
	{
		UpdateVisuals();
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
