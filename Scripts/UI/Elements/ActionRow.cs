using System;
using Godot;

namespace Sosr;

[GlobalClass]
public partial class ActionRow : HBoxContainer
{
	[Signal] public delegate void pressedEventHandler();

	private Button button;

	public Godot.Collections.Dictionary data = new();
	public string baseText = "";
	private Action _callback;
	public object? sourceObject = null;

	public override void _Ready()
	{
		button = GetNode<Button>("Button");
		button.Pressed += () => EmitSignal(SignalName.pressed);
		pressed += _on_button_pressed;
		// NOTE: GameUI calls refresh_ui on day_passed for every row in the troop list.
	}

	// 1. Standard Setup
	public async void setup(Godot.Collections.Dictionary itemData, Action onClick)
	{
		data = itemData;
		_callback = onClick;
		sourceObject = null;
		baseText = data.TryGetValue("text", out var text) ? text.AsString() : "Unknown Action";
		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);
		RefreshUi();
	}

	// 2. Training Setup
	public async void SetupTraining(TroopTraining trainingObj)
	{
		sourceObject = trainingObj;
		data = new Godot.Collections.Dictionary { { "is_status", true } };
		_callback = null;
		baseText = $"Training {trainingObj.divisionsCount} Divs";

		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);
		button.Disabled = true;
		button.Modulate = Colors.Gold;
		RefreshUi();
	}

	// 3. Ready Setup
	public async void SetupReady(ReadyTroop readyTroopObj, Action onClick)
	{
		data = new Godot.Collections.Dictionary { { "is_deploy", true } };
		_callback = onClick;
		baseText = $"Deploy {readyTroopObj.count} Divisions";

		if (!IsNodeReady())
			await ToSignal(this, Node.SignalName.Ready);
		button.Disabled = false;
		button.Modulate = Colors.SpringGreen;
		RefreshUi();
	}

	private bool Flag(string key) => data.TryGetValue(key, out var v) && v.AsBool();

	private double Number(string key) => data.TryGetValue(key, out var v) ? v.AsDouble() : 0.0;

	public void RefreshUi()
	{
		var player = CountryManager.Instance.Player;
		if (player == null)
			return;

		// Special Logic: Troop Training
		if (sourceObject != null && Flag("is_status"))
		{
			if (sourceObject is TroopTraining training)
			{
				// CountryData.TrainingFinished rebuilds the list once days_left hits 0.
				if (training.daysLeft <= 0)
					return;
				button.Text = $"{baseText} ({training.daysLeft} Days)";
			}
			return;
		}

		// Special Logic: Ready Troop. The GDScript original never set source_object for
		// ready rows, so this branch was unreachable; kept for parity.
		if (sourceObject != null && Flag("is_deploy"))
		{
			button.Text = baseText;
			return;
		}

		_update_standard_text_and_cost(player);
	}

	private void _update_standard_text_and_cost(CountryData player)
	{
		var costPp = Number("cost");
		var costMp = Number("manpower");
		var suffix = "";

		if (costPp > 0)
			suffix = $" ({(int)costPp} PP)";
		else if (costMp > 0)
			suffix = $" ({costMp * 0.001}k MP)";

		button.Text = baseText + suffix;

		var canAfford = player.politicalPower >= costPp && player.manpower >= costMp;
		button.Disabled = !canAfford;
		button.Modulate = canAfford ? Colors.White : new Color(1, 0.5f, 0.5f);
	}

	private void _on_button_pressed()
	{
		CountryManager.Instance.Player.politicalPower -= Number("cost");
		_callback?.Invoke();
	}
}
