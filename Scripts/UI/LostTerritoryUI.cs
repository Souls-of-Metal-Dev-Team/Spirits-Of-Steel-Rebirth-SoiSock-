using System.Linq;
using Godot;

namespace Sosr;

public partial class LostTerritoryUI : CanvasLayer
{
	private PanelContainer sidebar_panel;
	private Label summary_label;
	private Label stats_label;
	private Label loser_label;
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
		sidebar_panel = new PanelContainer();
		sidebar_panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		sidebar_panel.CustomMinimumSize = sidebar_panel.CustomMinimumSize with
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
		sidebar_panel.AddThemeStyleboxOverride("panel", style);
		AddChild(sidebar_panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 25);
		margin.AddThemeConstantOverride("margin_right", 25);
		margin.AddThemeConstantOverride("margin_top", 50);
		margin.AddThemeConstantOverride("margin_bottom", 50);
		sidebar_panel.AddChild(margin);

		var v_box = new VBoxContainer();
		v_box.AddThemeConstantOverride("separation", 25);
		margin.AddChild(v_box);

		var title = new Label
		{
			Text = "END OF A NATION",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", COLOR_GOLD);
		v_box.AddChild(title);

		var h_sep = new ColorRect { CustomMinimumSize = new Vector2(0, 2), Color = COLOR_GOLD };
		v_box.AddChild(h_sep);

		loser_label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		loser_label.AddThemeFontSizeOverride("font_size", 18);
		v_box.AddChild(loser_label);

		v_box.AddSpacer(false); // Pushes buttons to bottom

		var governmentInExile_btn = _create_styled_button("GOVERNMENT IN EXILE", new Color(0.3f, 0.5f, 0.3f));
		v_box.AddChild(governmentInExile_btn);

		var dissolution_btn = _create_styled_button("DISSOLVE THE GOVERNMENT", COLOR_GOLD);
		dissolution_btn.Pressed += m_OnDissolvePressed;
		v_box.AddChild(dissolution_btn);
	}

	private static Button _create_styled_button(string btn_text, Color accent_color)
	{
		var btn = new Button
		{
			Text = btn_text,
			CustomMinimumSize = new Vector2(0, 45),
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};

		var style_normal = new StyleBoxFlat
		{
			BgColor = accent_color * 0.6f,
			BorderWidthBottom = 4,
			BorderColor = accent_color * 0.4f,
		};
		style_normal.SetCornerRadiusAll(3);

		var style_hover = (StyleBoxFlat)style_normal.Duplicate();
		style_hover.BgColor = accent_color * 0.8f;

		btn.AddThemeStyleboxOverride("normal", style_normal);
		btn.AddThemeStyleboxOverride("hover", style_hover);
		btn.AddThemeColorOverride("font_color", Colors.White);
		return btn;
	}

	private void SetGameUiVisible(bool a_visible)
	{
		if (GetTree().Root.FindChild("ui_game", true, false) is CanvasLayer game_ui)
			game_ui.Visible = a_visible;
	}

	public void m_ExiledHost(CountryData a_host, CountryData a_hosted)
	{
		CountryManager.Instance.MakeHost(a_host, a_hosted);

		SetGameUiVisible(true);
		GameState.Instance.lostTerritory = false;
		GameState.Instance.current_world.clock.resume();

		Hide();
	}

	private PanelContainer m_CreateHostNationButton(CountryData a_nation, CountryData a_player)
	{
		var btn = (PanelContainer)template.Duplicate();
		var row = btn.GetChild(0);
		var button = row.GetChild<Button>(1);
		row.GetChild<TextureRect>(0).Texture = TroopManager.Instance.get_flag(a_nation.country_name, a_nation.ideology_name);
		button.Text = a_nation.country_name.Capitalize();
		button.Pressed += () => m_ExiledHost(a_nation, a_player);
		row.GetChild(2).GetChild<Label>(0).Text = a_player.get_relation_with(a_nation.country_name).ToString();
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
			if (player.get_relation_with(country) >= 175)
				hosts.AddChild(m_CreateHostNationButton(CountryManager.Instance.GetCountry(country), player));
		}
		Show();
		SetGameUiVisible(false);
		GameState.Instance.current_world.clock.pause();
		GameState.Instance.lostTerritory = true;
	}

	public void m_OnDissolvePressed()
	{
		var cm = CountryManager.Instance;
		var player = cm.Player;
		if (player.is_puppet)
			cm.release_puppet(cm.GetCountry(player.owner), player);

		foreach (var puppet in player.puppets.ToArray())
		{
			GD.Print($"Deleting Puppet: {puppet}");
			cm.release_puppet(player, cm.GetCountry(puppet));
		}

		cm.countryNames = cm.countryNames.Where(n => n != player.country_name).ToArray();
		cm.countries.Remove(player.country_name);

		SetGameUiVisible(true);
		GameState.Instance.lostTerritory = false;
		GameState.Instance.current_world.clock.resume();

		var keys = cm.countries.Keys.ToArray();
		cm.set_player_country(keys[GD.RandRange(0, keys.Length - 1)].AsString());

		Hide();
	}
}
