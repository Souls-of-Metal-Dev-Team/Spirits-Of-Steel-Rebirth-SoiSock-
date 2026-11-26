using System.Linq;
using Godot;

namespace Sosr;

public partial class LostTerritoryUI : CanvasLayer
{
	private PanelContainer sidebarPanel;
	private Label summaryLabel;
	private Label statsLabel;
	private Label loserLabel;
	[Export] public VBoxContainer hosts;
	[Export] public PanelContainer template;

	// Color Palette
	private static readonly Color COLOR_BG = new(0.1f, 0.1f, 0.12f, 0.98f);
	private static readonly Color COLOR_GOLD = new(0.85f, 0.65f, 0.2f);
	private static readonly Color COLOR_SELECT = new(0.0f, 1.0f, 0.8f); // Cyan/Teal for treaty selection
	private static readonly Color COLOR_DANGER = new(0.7f, 0.2f, 0.2f);

	public override void _Ready()
	{
		Hide();
		CountryManager.Instance.PlayerEliminated += country => OpenMenu((CountryData)country);
	}

	// Unused builder for a code-generated layout; the scene now provides the layout.
	private void _setup_ui_elements()
	{
		sidebarPanel = new PanelContainer();
		sidebarPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		sidebarPanel.CustomMinimumSize = sidebarPanel.CustomMinimumSize with
		{
			X = GetViewport().GetVisibleRect().Size.X * 0.22f,
		};

		var style = new StyleBoxFlat
		{
			BgColor = COLOR_BG,
			BorderWidthRight = 4,
			BorderColor = COLOR_GOLD,
			ShadowSize = 10,
		};
		sidebarPanel.AddThemeStyleboxOverride("panel", style);
		AddChild(sidebarPanel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 25);
		margin.AddThemeConstantOverride("margin_right", 25);
		margin.AddThemeConstantOverride("margin_top", 50);
		margin.AddThemeConstantOverride("margin_bottom", 50);
		sidebarPanel.AddChild(margin);

		var vBox = new VBoxContainer();
		vBox.AddThemeConstantOverride("separation", 25);
		margin.AddChild(vBox);

		var title = new Label
		{
			Text = "END OF A NATION",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", COLOR_GOLD);
		vBox.AddChild(title);

		var hSep = new ColorRect { CustomMinimumSize = new Vector2(0, 2), Color = COLOR_GOLD };
		vBox.AddChild(hSep);

		loserLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		loserLabel.AddThemeFontSizeOverride("font_size", 18);
		vBox.AddChild(loserLabel);

		vBox.AddSpacer(false); // Pushes buttons to bottom

		var governmentInExileBtn = _create_styled_button("GOVERNMENT IN EXILE", new Color(0.3f, 0.5f, 0.3f));
		vBox.AddChild(governmentInExileBtn);

		var dissolutionBtn = _create_styled_button("DISSOLVE THE GOVERNMENT", COLOR_GOLD);
		dissolutionBtn.Pressed += MOnDissolvePressed;
		vBox.AddChild(dissolutionBtn);
	}

	private static Button _create_styled_button(string btnText, Color accentColor)
	{
		var btn = new Button
		{
			Text = btnText,
			CustomMinimumSize = new Vector2(0, 45),
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};

		var styleNormal = new StyleBoxFlat
		{
			BgColor = accentColor * 0.6f,
			BorderWidthBottom = 4,
			BorderColor = accentColor * 0.4f,
		};
		styleNormal.SetCornerRadiusAll(3);

		var styleHover = (StyleBoxFlat)styleNormal.Duplicate();
		styleHover.BgColor = accentColor * 0.8f;

		btn.AddThemeStyleboxOverride("normal", styleNormal);
		btn.AddThemeStyleboxOverride("hover", styleHover);
		btn.AddThemeColorOverride("font_color", Colors.White);
		return btn;
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
