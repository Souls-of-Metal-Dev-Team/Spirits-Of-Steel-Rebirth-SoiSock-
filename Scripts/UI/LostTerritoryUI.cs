using System.Linq;
using Godot;

namespace Sosr;

public partial class LostTerritoryUI : CanvasLayer
{
	[Export] public VBoxContainer hosts;
	[Export] public PanelContainer template;

	public override void _Ready()
	{
		Hide();
		CountryManager.Instance.PlayerEliminated += country => OpenMenu((CountryData)country);
	}

	private void SetGameUiVisible(bool aVisible)
	{
		if (GetTree().Root.FindChild("ui_game", true, false) is CanvasLayer gameUi)
			gameUi.Visible = aVisible;
	}

	public void MExiledHost(CountryData aHost, CountryData aHosted)
	{
		CountryManager.Instance.MakeHost(aHost, aHosted);

		SetGameUiVisible(true);
		GameState.Instance.lostTerritory = false;
		GameState.Instance.currentWorld.clock.resume();

		Hide();
	}

	private PanelContainer MCreateHostNationButton(CountryData aNation, CountryData aPlayer)
	{
		var btn = (PanelContainer)template.Duplicate();
		var row = btn.GetChild(0);
		var button = row.GetChild<Button>(1);
		row.GetChild<TextureRect>(0).Texture = TroopManager.Instance.GetFlag(aNation.countryName, aNation.ideologyName);
		button.Text = aNation.countryName.Capitalize();
		button.Pressed += () => MExiledHost(aNation, aPlayer);
		row.GetChild(2).GetChild<Label>(0).Text = aPlayer.GetRelationWith(aNation.countryName).ToString();
		btn.Visible = true;
		return btn;
	}

	// --- Logic & Integration ---

	public void OpenMenu(CountryData player)
	{
		foreach (var child in hosts.GetChildren())
			hosts.RemoveChild(child);
		foreach (var country in player.relations.Keys)
		{
			if (player.GetRelationWith(country) >= 175)
				hosts.AddChild(MCreateHostNationButton(CountryManager.Instance.GetCountry(country), player));
		}
		Show();
		SetGameUiVisible(false);
		GameState.Instance.currentWorld.clock.pause();
		GameState.Instance.lostTerritory = true;
	}

	public void MOnDissolvePressed()
	{
		var cm = CountryManager.Instance;
		var player = cm.Player;
		if (player.isPuppet)
			cm.ReleasePuppet(cm.GetCountry(player.owner), player);

		foreach (var puppet in player.puppets.ToArray())
		{
			GD.Print($"Deleting Puppet: {puppet}");
			cm.ReleasePuppet(player, cm.GetCountry(puppet));
		}

		cm.countryNames = cm.countryNames.Where(n => n != player.countryName).ToArray();
		cm.countries.Remove(player.countryName);

		SetGameUiVisible(true);
		GameState.Instance.lostTerritory = false;
		GameState.Instance.currentWorld.clock.resume();

		var keys = cm.countries.Keys.ToArray();
		cm.SetPlayerCountry(keys[GD.RandRange(0, keys.Length - 1)].AsString());

		Hide();
	}
}
