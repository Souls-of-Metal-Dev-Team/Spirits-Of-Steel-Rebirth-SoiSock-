using System;
using Godot;

namespace Sosr;

public partial class ManageCountryUI : PanelContainer
{
	#region --- Nodes ---
	[Export] public VBoxContainer military_laws_grid;
	[Export] public VBoxContainer economy_laws_grid;
	[Export] public VBoxContainer country_laws_grid;
	[Export] public VBoxContainer releasable_laws_grid;

	// Header & Stats
	[Export] public Label header_label;
	[Export] public TextureRect flag_rect;

	// Data
	public enum Category { MILITARY, ECONOMY, COUNTRY, RELEASABLES }
	public Category current_category = Category.MILITARY;
	public CountryData current_country;

	[Export] public Label economic_status_val;
	[Export] public Label army_logistics_val;
	#endregion

	private const int RELEASE_COST = 50;

	public override void _Ready()
	{
		Visible = false;
		_populate_military();
		_populate_economy();
		TroopManager.Instance.TroopsChanged += _on_troops_changed;
		var clock = GameState.Instance.current_world?.clock;
		if (clock != null)
			clock.DayPassed += _on_day_passed;
	}

	public override void _ExitTree()
	{
		TroopManager.Instance.TroopsChanged -= _on_troops_changed;
		var clock = GameState.Instance.current_world?.clock;
		if (clock != null && GodotObject.IsInstanceValid(clock))
			clock.DayPassed -= _on_day_passed;
		if (current_country != null)
			current_country.IdeologyChanged -= _refresh_full_data;
	}

	public void OpenMenu(CountryData country, Category cat = Category.MILITARY)
	{
		if (current_country != null)
			current_country.IdeologyChanged -= _refresh_full_data;

		current_country = country;
		current_country.IdeologyChanged += _refresh_full_data;

		_populate_country();
		_populate_releasables(current_country.country_name);

		_switch_category(cat);
		_refresh_full_data();
		Show();
	}

	public void CloseMenu()
	{
		Hide();
	}

	private void _on_day_passed()
	{
		if (Visible && current_country != null)
			_refresh_army_counts();
	}

	private void _on_troops_changed(string country_name)
	{
		if (!Visible || current_country == null)
			return;
		if (country_name == "" || country_name == current_country.country_name)
			_refresh_army_counts();
	}

	private void _switch_category(Category cat)
	{
		current_category = cat;

		// Attempt to find parent TabContainer to switch tabs correctly if nested
		var tab_container = country_laws_grid.GetParent();
		while (tab_container != null && tab_container is not TabContainer && tab_container != this)
			tab_container = tab_container.GetParent();

		if (tab_container is TabContainer tabs)
		{
			tabs.CurrentTab = (int)cat;
			// Ensure all grids are visible so the TabContainer can display the active one
			military_laws_grid.Visible = true;
			economy_laws_grid.Visible = true;
			country_laws_grid.Visible = true;
			releasable_laws_grid.Visible = true;
		}
		else
		{
			// Fallback visibility toggling for non-TabContainer layouts
			military_laws_grid.Visible = cat == Category.MILITARY;
			economy_laws_grid.Visible = cat == Category.ECONOMY;
			country_laws_grid.Visible = cat == Category.COUNTRY;
			releasable_laws_grid.Visible = cat == Category.RELEASABLES;
		}
	}

	private void _populate_military()
	{
		_add_law_option("Volunteer Only", 0.005, 0.0, 0, "Professional army.");
		_add_law_option("Limited Conscription", 0.01, 0.05, 150, "Drafting young men.");
		_add_law_option("Extensive Conscription", 0.015, 0.15, 150, "Wide-scale mobilization.");
		_add_law_option("Service by Requirement", 0.02, 0.30, 150, "All eligible adults.");
		_add_law_option("All Adult Serve", 0.4, 0.50, 150, "Scraping the barrel.");
	}

	private void _populate_economy()
	{
		economy_laws_grid.AddChild(new Label { Text = "Economy laws coming soon..." });
	}

	private void _populate_country()
	{
		foreach (var child in country_laws_grid.GetChildren())
			child.QueueFree();

		var plans = PlansManager.Instance.plans;
		if (!plans.ContainsKey(current_country.country_name))
			return;

		foreach (var element in plans[current_country.country_name].AsGodotArray())
			InterpreterManager.Instance.get_element(element, country_laws_grid);
	}

	private void _populate_releasables(string player_country)
	{
		foreach (var child in releasable_laws_grid.GetChildren())
			child.QueueFree();

		var releasables = MapManager.Instance.GetAllReleasables(player_country);

		if (releasables.Count == 0)
		{
			releasable_laws_grid.AddChild(new Label
			{
				Text = "No nations to release.",
				HorizontalAlignment = HorizontalAlignment.Center,
			});
		}
		else
		{
			foreach (var releasable_data in releasables)
				_add_releasable_option(releasable_data);
		}
	}

	#region --- Logic & Data Refresh ---
	private void _refresh_full_data()
	{
		if (current_country == null)
			return;
		header_label.Text = current_country.country_name.ToUpper();
		flag_rect.Texture = TroopManager.Instance.get_flag(current_country.country_name, current_country.ideology_name);

		_refresh_army_counts();
		_update_law_buttons_visuals();
	}

	private void _refresh_army_counts()
	{
		// 1. Economic Status
		if (economic_status_val != null)
		{
			economic_status_val.Text = string.Format(
				"${0}\n+${1:F1}\n+${2:F1}\n-${3:F1}",
				_format_number(current_country.money, false),
				current_country.gdp * 0.0000228310502,
				current_country.factories_amount * current_country.factory_income,
				current_country.army_cost);
		}

		// 2. Army Logistics
		if (army_logistics_val != null)
		{
			var counts = new System.Collections.Generic.Dictionary<string, int>
			{
				{ "infantry", 0 }, { "tank", 0 }, { "artillery", 0 },
			};

			foreach (var troopVariant in TroopManager.Instance.get_troops_for_country(current_country.country_name))
			{
				if (troopVariant.AsGodotObject() is not TroopData troop)
					continue;
				foreach (var div in troop.stored_divisions)
					counts[div.type] = (counts.TryGetValue(div.type, out var n) ? n : 0) + 1;
			}
			army_logistics_val.Text = string.Format(
				"{0} / {1}\n{2}\n{3}\n{4}",
				_format_number(current_country.manpower),
				_format_number((int)(current_country.total_population * current_country.military_size_ratio)),
				counts["infantry"],
				counts["tank"],
				counts["artillery"]);
		}
	}

	private void _update_law_buttons_visuals()
	{
		foreach (var node in military_laws_grid.GetChildren())
		{
			if (node is not PanelContainer btn || !btn.HasMeta("ratio"))
				continue;

			var hbox = btn.GetChild(0).GetNode("HBox");
			var status_lbl = hbox.GetNode<Label>("Status");
			var cost_lbl = hbox.GetNode<Label>("Cost");

			if (Mathf.IsEqualApprox(current_country.military_size_ratio, btn.GetMeta("ratio").AsDouble()))
			{
				btn.Modulate = new Color(0.8f, 1.0f, 0.8f); // Greenish tint
				status_lbl.Text = " ACTIVE";
				cost_lbl.Visible = false;
			}
			else
			{
				btn.Modulate = new Color(1.0f, 1.0f, 1.0f);
				status_lbl.Text = "";
				cost_lbl.Visible = true;
				if (current_country.political_power < btn.GetMeta("cost").AsInt32())
					btn.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.7f);
			}
		}
	}
	#endregion

	private static Button MakeButton(string text, Action on_pressed)
	{
		var btn = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(80, 30),
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};
		btn.Pressed += on_pressed;
		return btn;
	}

	private void _add_releasable_option(Godot.Collections.Dictionary releasable)
	{
		var country = releasable["country"].AsString();

		var btn_panel = new PanelContainer { CustomMinimumSize = new Vector2(0, 55) };

		var m = new MarginContainer();
		m.AddThemeConstantOverride("margin_left", 10);
		m.AddThemeConstantOverride("margin_right", 10);
		btn_panel.AddChild(m);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 15);
		m.AddChild(hbox);

		// Flag
		hbox.AddChild(new TextureRect
		{
			CustomMinimumSize = new Vector2(40, 26),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Texture = TroopManager.Instance.get_flag(country),
		});

		// Text Info (Expand to push buttons to the right)
		var v_text = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		hbox.AddChild(v_text);

		var title = new Label { Text = country.Capitalize().Replace("_", " ") };
		title.AddThemeFontSizeOverride("font_size", 14);
		v_text.AddChild(title);

		var cost_lbl = new Label { Text = $"Cost: {RELEASE_COST} PP" };
		cost_lbl.AddThemeFontSizeOverride("font_size", 10);
		cost_lbl.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 0.4f));
		v_text.AddChild(cost_lbl);

		// Buttons HBox (The Action Area)
		var h_btns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		h_btns.AddThemeConstantOverride("separation", 8);
		hbox.AddChild(h_btns);

		if (MapManager.Instance._country_exists_on_map(country))
		{
			var pids = releasable["owned_provinces"].AsInt32Array();
			h_btns.AddChild(MakeButton("Return Territory", () => _on_return_territory_pressed(country, pids)));
		}
		else
		{
			h_btns.AddChild(MakeButton("Release", () => _on_release_pressed(country)));
			h_btns.AddChild(MakeButton("Release As Puppet", () => _on_release_puppet_pressed(country)));
			var btn_play = MakeButton("Play As", () => _on_release_and_play_pressed(country));
			btn_play.AddThemeColorOverride("font_hover_color", new Color(0.5f, 0.8f, 1.0f));
			h_btns.AddChild(btn_play);
		}

		releasable_laws_grid.AddChild(btn_panel);
	}

	private bool TrySpendReleaseCost()
	{
		if (current_country.political_power < RELEASE_COST)
		{
			GameConsole.Error("Not enough Political Power!");
			return false;
		}
		current_country.political_power -= RELEASE_COST;
		return true;
	}

	private void _on_return_territory_pressed(string country_id, int[] pids)
	{
		if (!TrySpendReleaseCost())
			return;
		foreach (var pid in pids)
			MapManager.Instance.TransferOwnership(pid, country_id);
		_populate_releasables(current_country.country_name);
	}

	private void _on_release_pressed(string country_id)
	{
		if (!TrySpendReleaseCost())
			return;
		MapManager.Instance.ReleaseCountry(country_id);
		_populate_releasables(current_country.country_name);
	}

	private void _on_release_puppet_pressed(string country_id)
	{
		if (!TrySpendReleaseCost())
			return;
		MapManager.Instance.ReleasePuppet(current_country.country_name, country_id);
		_populate_releasables(current_country.country_name);
	}

	private void _on_release_and_play_pressed(string country_id)
	{
		// The GDScript original checked the cost but did not deduct it here.
		if (current_country.political_power < RELEASE_COST)
		{
			GameConsole.Error("Not enough Political Power!");
			return;
		}
		MapManager.Instance.ReleaseCountry(country_id);
		CountryManager.Instance.set_player_country(country_id);
		GameConsole.Info("Switched playing as: " + country_id);

		_populate_releasables(country_id);
	}

	private void _add_law_option(string _name, double ratio, double eco_penalty, int cost, string _tooltip)
	{
		var btn_panel = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) };

		// Metadata storage for logic
		btn_panel.SetMeta("ratio", ratio);
		btn_panel.SetMeta("penalty", eco_penalty);
		btn_panel.SetMeta("cost", cost);

		var m = new MarginContainer();
		m.AddThemeConstantOverride("margin_left", 5);
		m.AddThemeConstantOverride("margin_right", 5);
		btn_panel.AddChild(m);

		var hbox = new HBoxContainer { Name = "HBox" };
		m.AddChild(hbox);

		var title = new Label
		{
			Name = "Title",
			Text = _name,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		var effect_lbl = new Label { Text = $"Pop: {ratio * 100.0:F2}% | Eco: -{eco_penalty * 100.0:F0}%" };
		effect_lbl.AddThemeFontSizeOverride("font_size", 12);

		var cost_lbl = new Label { Name = "Cost", Text = $"{cost} PP" };

		var status_lbl = new Label { Name = "Status", Text = "" };
		status_lbl.AddThemeFontSizeOverride("font_size", 12);

		hbox.AddChild(title);
		hbox.AddChild(effect_lbl);
		hbox.AddChild(new VSeparator());
		hbox.AddChild(cost_lbl);
		hbox.AddChild(status_lbl);

		// Make it clickable
		btn_panel.GuiInput += e => _on_law_gui_input(e, btn_panel);

		military_laws_grid.AddChild(btn_panel);
	}

	#region --- Interactions ---
	private void _on_law_gui_input(InputEvent @event, PanelContainer btn)
	{
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			return;

		var ratio = btn.GetMeta("ratio").AsDouble();

		// 1. Is this already active?
		if (Mathf.IsEqualApprox(current_country.military_size_ratio, ratio))
			return;

		var cost = btn.GetMeta("cost").AsInt32();

		// 2. Can we afford it?
		if (current_country.political_power >= cost)
		{
			current_country.political_power -= cost;
			current_country.military_size_ratio = ratio;
			current_country.economy_law_penalty = btn.GetMeta("penalty").AsDouble();

			current_country.UpdateManpowerPool(); // Recalc based on new ratio

			_refresh_full_data();
			GD.Print("Law enacted: ", ratio);
		}
		else
		{
			GD.Print("Not enough Political Power!");
		}
	}
	#endregion

	// Utils
	private static string _format_number(double amount, bool as_int = true)
	{
		var abs_amount = Math.Abs(amount);
		var suffix = "";
		var result = amount;

		if (abs_amount >= 1000000)
		{
			result = amount * 0.000001;
			suffix = "M";
		}
		else if (abs_amount >= 1000)
		{
			result = amount * 0.001;
			suffix = "K";
		}

		if (suffix == "")
			return as_int ? ((int)amount).ToString() : $"{amount:F2}";

		return $"{result:F1}{suffix}";
	}

	public void _on_tab_container_tab_changed(int tab)
	{
		if (current_country != null)
			_populate_releasables(current_country.country_name);
	}
}
