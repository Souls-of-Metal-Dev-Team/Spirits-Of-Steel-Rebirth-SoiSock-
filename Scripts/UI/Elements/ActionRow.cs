using System;
using Godot;

namespace Sosr;

[GlobalClass]
public partial class ActionRow : HBoxContainer
{
	[Signal] public delegate void pressedEventHandler();

	private Button button;

	public Godot.Collections.Dictionary data = new();
	public string base_text = "";
	private Action _callback;
	public GodotObject source_object = null;

	public override void _Ready()
	{
		button = GetNode<Button>("Button");
		button.Pressed += () => EmitSignal(SignalName.pressed);
		pressed += _on_button_pressed;
		// NOTE: GameUI calls refresh_ui on day_passed for every row in the troop list.
	}

	// 1. Standard Setup
	public async void setup(Godot.Collections.Dictionary item_data, Action on_click)
	{
		data = item_data;
		_callback = on_click;
		source_object = null;
		base_text = data.TryGetValue("text", out var text) ? text.AsString() : "Unknown Action";
		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);
		refresh_ui();
	}

	// 2. Training Setup
	public async void setup_training(TroopTraining training_obj)
	{
		source_object = training_obj;
		data = new Godot.Collections.Dictionary { { "is_status", true } };
		_callback = null;
		base_text = $"Training {training_obj.divisions_count} Divs";

		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);
		button.Disabled = true;
		button.Modulate = Colors.Gold;
		refresh_ui();
	}

	// 3. Ready Setup
	public async void setup_ready(ReadyTroop ready_troop_obj, Action on_click)
	{
		data = new Godot.Collections.Dictionary { { "is_deploy", true } };
		_callback = on_click;
		base_text = $"Deploy {ready_troop_obj.count} Divisions";

		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);
		button.Disabled = false;
		button.Modulate = Colors.SpringGreen;
		refresh_ui();
	}

	private bool Flag(string key) => data.TryGetValue(key, out var v) && v.AsBool();

	private double Number(string key) => data.TryGetValue(key, out var v) ? v.AsDouble() : 0.0;

	public void refresh_ui()
	{
		var player = CountryManager.Instance.Player;
		if (player == null)
			return;

		// Special Logic: Troop Training
		if (source_object != null && Flag("is_status"))
		{
			if (source_object is TroopTraining training)
			{
				// CountryData.TrainingFinished rebuilds the list once days_left hits 0.
				if (training.days_left <= 0)
					return;
				button.Text = $"{base_text} ({training.days_left} Days)";
			}
			return;
		}

		// Special Logic: Ready Troop. The GDScript original never set source_object for
		// ready rows, so this branch was unreachable; kept for parity.
		if (source_object != null && Flag("is_deploy"))
		{
			button.Text = base_text;
			return;
		}

		_update_standard_text_and_cost(player);
	}

	private void _update_standard_text_and_cost(CountryData player)
	{
		var cost_pp = Number("cost");
		var cost_mp = Number("manpower");
		var suffix = "";

		if (cost_pp > 0)
			suffix = $" ({(int)cost_pp} PP)";
		else if (cost_mp > 0)
			suffix = $" ({cost_mp * 0.001}k MP)";

		button.Text = base_text + suffix;

		var can_afford = player.political_power >= cost_pp && player.manpower >= cost_mp;
		button.Disabled = !can_afford;
		button.Modulate = can_afford ? Colors.White : new Color(1, 0.5f, 0.5f);
	}

	private void _on_button_pressed()
	{
		CountryManager.Instance.Player.political_power -= Number("cost");
		_callback?.Invoke();
	}
}
