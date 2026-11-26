using System;
using Godot;

namespace Sosr;

public partial class ManageCountryUI : PanelContainer
{
	#region --- Nodes ---
	[Export] public VBoxContainer militaryLawsGrid;
	[Export] public VBoxContainer economyLawsGrid;
	[Export] public VBoxContainer countryLawsGrid;
	[Export] public VBoxContainer releasableLawsGrid;

	// Header & Stats
	[Export] public Label headerLabel;
	[Export] public TextureRect flagRect;

	// Data
	public enum Category { MILITARY, ECONOMY, COUNTRY, RELEASABLES }
	public Category currentCategory = Category.MILITARY;
	public CountryData currentCountry;

	[Export] public Label economicStatusVal;
	[Export] public Label armyLogisticsVal;
	#endregion

	private const int RELEASE_COST = 50;

	public override void _Ready()
	{
		Visible = false;
		_populate_military();
		_populate_economy();
		TroopManager.Instance.TroopsChanged += _on_troops_changed;
		var clock = GameState.Instance.currentWorld?.clock;
		if (clock != null)
			clock.DayPassed += _on_day_passed;
	}

	public override void _ExitTree()
	{
		TroopManager.Instance.TroopsChanged -= _on_troops_changed;
		var clock = GameState.Instance.currentWorld?.clock;
		if (clock != null && GodotObject.IsInstanceValid(clock))
			clock.DayPassed -= _on_day_passed;
		if (currentCountry != null)
			currentCountry.IdeologyChanged -= _refresh_full_data;
	}

	public void OpenMenu(CountryData country, Category cat = Category.MILITARY)
	{
		if (currentCountry != null)
			currentCountry.IdeologyChanged -= _refresh_full_data;

		currentCountry = country;
		currentCountry.IdeologyChanged += _refresh_full_data;

		_populate_country();
		_populate_releasables(currentCountry.countryName);

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
		if (Visible && currentCountry != null)
			_refresh_army_counts();
	}

	private void _on_troops_changed(string countryName)
	{
		if (!Visible || currentCountry == null)
			return;
		if (countryName == "" || countryName == currentCountry.countryName)
			_refresh_army_counts();
	}

	private void _switch_category(Category cat)
	{
		currentCategory = cat;

		// Attempt to find parent TabContainer to switch tabs correctly if nested
		var tabContainer = countryLawsGrid.GetParent();
		while (tabContainer != null && tabContainer is not TabContainer && tabContainer != this)
			tabContainer = tabContainer.GetParent();

		if (tabContainer is TabContainer tabs)
		{
			tabs.CurrentTab = (int)cat;
			// Ensure all grids are visible so the TabContainer can display the active one
			militaryLawsGrid.Visible = true;
			economyLawsGrid.Visible = true;
			countryLawsGrid.Visible = true;
			releasableLawsGrid.Visible = true;
		}
		else
		{
			// Fallback visibility toggling for non-TabContainer layouts
			militaryLawsGrid.Visible = cat == Category.MILITARY;
			economyLawsGrid.Visible = cat == Category.ECONOMY;
			countryLawsGrid.Visible = cat == Category.COUNTRY;
			releasableLawsGrid.Visible = cat == Category.RELEASABLES;
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
		economyLawsGrid.AddChild(new Label { Text = "Economy laws coming soon..." });
	}

	private void _populate_country()
	{
		foreach (var child in countryLawsGrid.GetChildren())
			child.QueueFree();

		var plans = PlansManager.Instance.plans;
		if (!plans.ContainsKey(currentCountry.countryName))
			return;

		foreach (var element in plans[currentCountry.countryName].AsGodotArray())
			InterpreterManager.Instance.GetElement(element, countryLawsGrid);
	}

	private void _populate_releasables(string playerCountry)
	{
		foreach (var child in releasableLawsGrid.GetChildren())
			child.QueueFree();

		var releasables = MapManager.Instance.GetAllReleasables(playerCountry);

		if (releasables.Count == 0)
		{
			releasableLawsGrid.AddChild(new Label
			{
				Text = "No nations to release.",
				HorizontalAlignment = HorizontalAlignment.Center,
			});
		}
		else
		{
			foreach (var releasableData in releasables)
				_add_releasable_option(releasableData);
		}
	}

	#region --- Logic & Data Refresh ---
	private void _refresh_full_data()
	{
		if (currentCountry == null)
			return;
		headerLabel.Text = currentCountry.countryName.ToUpper();
		flagRect.Texture = TroopManager.Instance.GetFlag(currentCountry.countryName, currentCountry.ideologyName);

		_refresh_army_counts();
		_update_law_buttons_visuals();
	}

	private void _refresh_army_counts()
	{
		// 1. Economic Status
		if (economicStatusVal != null)
		{
			economicStatusVal.Text = string.Format(
				"${0}\n+${1:F1}\n+${2:F1}\n-${3:F1}",
				_format_number(currentCountry.money, false),
				currentCountry.gdp * 0.0000228310502,
				currentCountry.factoriesAmount * currentCountry.factoryIncome,
				currentCountry.armyCost);
		}

		// 2. Army Logistics
		if (armyLogisticsVal != null)
		{
			var counts = new System.Collections.Generic.Dictionary<string, int>
			{
				{ "infantry", 0 }, { "tank", 0 }, { "artillery", 0 },
			};

			foreach (var troopVariant in TroopManager.Instance.GetTroopsForCountry(currentCountry.countryName))
			{
				if (troopVariant is not TroopData troop)
					continue;
				foreach (var div in troop.storedDivisions)
					counts[div.type] = (counts.TryGetValue(div.type, out var n) ? n : 0) + 1;
			}
			armyLogisticsVal.Text = string.Format(
				"{0} / {1}\n{2}\n{3}\n{4}",
				_format_number(currentCountry.manpower),
				_format_number((int)(currentCountry.totalPopulation * currentCountry.militarySizeRatio)),
				counts["infantry"],
				counts["tank"],
				counts["artillery"]);
		}
	}

	private void _update_law_buttons_visuals()
	{
		foreach (var node in militaryLawsGrid.GetChildren())
		{
			if (node is not PanelContainer btn || !btn.HasMeta("ratio"))
				continue;

			var hbox = btn.GetChild(0).GetNode("HBox");
			var statusLbl = hbox.GetNode<Label>("Status");
			var costLbl = hbox.GetNode<Label>("Cost");

			if (Mathf.IsEqualApprox(currentCountry.militarySizeRatio, btn.GetMeta("ratio").AsDouble()))
			{
				btn.Modulate = new Color(0.8f, 1.0f, 0.8f); // Greenish tint
				statusLbl.Text = " ACTIVE";
				costLbl.Visible = false;
			}
			else
			{
				btn.Modulate = new Color(1.0f, 1.0f, 1.0f);
				statusLbl.Text = "";
				costLbl.Visible = true;
				if (currentCountry.politicalPower < btn.GetMeta("cost").AsInt32())
					btn.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.7f);
			}
		}
	}
	#endregion

	private static Button MakeButton(string text, Action onPressed)
	{
		var btn = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(80, 30),
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};
		btn.Pressed += onPressed;
		return btn;
	}

	private void _add_releasable_option(Godot.Collections.Dictionary releasable)
	{
		var country = releasable["country"].AsString();

		var btnPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 55) };

		var m = new MarginContainer();
		m.AddThemeConstantOverride("margin_left", 10);
		m.AddThemeConstantOverride("margin_right", 10);
		btnPanel.AddChild(m);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 15);
		m.AddChild(hbox);

		// Flag
		hbox.AddChild(new TextureRect
		{
			CustomMinimumSize = new Vector2(40, 26),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Texture = TroopManager.Instance.GetFlag(country),
		});

		// Text Info (Expand to push buttons to the right)
		var vText = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		hbox.AddChild(vText);

		var title = new Label { Text = country.Capitalize().Replace("_", " ") };
		title.AddThemeFontSizeOverride("font_size", 14);
		vText.AddChild(title);

		var costLbl = new Label { Text = $"Cost: {RELEASE_COST} PP" };
		costLbl.AddThemeFontSizeOverride("font_size", 10);
		costLbl.AddThemeColorOverride("font_color", new Color(0.2f, 0.8f, 0.4f));
		vText.AddChild(costLbl);

		// Buttons HBox (The Action Area)
		var hBtns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		hBtns.AddThemeConstantOverride("separation", 8);
		hbox.AddChild(hBtns);

		if (MapManager.Instance._country_exists_on_map(country))
		{
			var pids = releasable["owned_provinces"].AsInt32Array();
			hBtns.AddChild(MakeButton("Return Territory", () => _on_return_territory_pressed(country, pids)));
		}
		else
		{
			hBtns.AddChild(MakeButton("Release", () => _on_release_pressed(country)));
			hBtns.AddChild(MakeButton("Release As Puppet", () => _on_release_puppet_pressed(country)));
			var btnPlay = MakeButton("Play As", () => _on_release_and_play_pressed(country));
			btnPlay.AddThemeColorOverride("font_hover_color", new Color(0.5f, 0.8f, 1.0f));
			hBtns.AddChild(btnPlay);
		}

		releasableLawsGrid.AddChild(btnPanel);
	}

	private bool TrySpendReleaseCost()
	{
		if (currentCountry.politicalPower < RELEASE_COST)
		{
			GameConsole.Error("Not enough Political Power!");
			return false;
		}
		currentCountry.politicalPower -= RELEASE_COST;
		return true;
	}

	private void _on_return_territory_pressed(string countryId, int[] pids)
	{
		if (!TrySpendReleaseCost())
			return;
		foreach (var pid in pids)
			ActionDispatcher.Instance.Submit(new TransferProvinceAction
			{
				Issuer = currentCountry.countryName, ProvinceId = pid, NewOwner = countryId,
			});
		_populate_releasables(currentCountry.countryName);
	}

	private void _on_release_pressed(string countryId)
	{
		if (!TrySpendReleaseCost())
			return;
		MapManager.Instance.ReleaseCountry(countryId);
		_populate_releasables(currentCountry.countryName);
	}

	private void _on_release_puppet_pressed(string countryId)
	{
		if (!TrySpendReleaseCost())
			return;
		MapManager.Instance.ReleasePuppet(currentCountry.countryName, countryId);
		_populate_releasables(currentCountry.countryName);
	}

	private void _on_release_and_play_pressed(string countryId)
	{
		// The GDScript original checked the cost but did not deduct it here.
		if (currentCountry.politicalPower < RELEASE_COST)
		{
			GameConsole.Error("Not enough Political Power!");
			return;
		}
		MapManager.Instance.ReleaseCountry(countryId);
		CountryManager.Instance.SetPlayerCountry(countryId);
		GameConsole.Info("Switched playing as: " + countryId);

		_populate_releasables(countryId);
	}

	private void _add_law_option(string _name, double ratio, double ecoPenalty, int cost, string _tooltip)
	{
		var btnPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) };

		// Metadata storage for logic
		btnPanel.SetMeta("ratio", ratio);
		btnPanel.SetMeta("penalty", ecoPenalty);
		btnPanel.SetMeta("cost", cost);

		var m = new MarginContainer();
		m.AddThemeConstantOverride("margin_left", 5);
		m.AddThemeConstantOverride("margin_right", 5);
		btnPanel.AddChild(m);

		var hbox = new HBoxContainer { Name = "HBox" };
		m.AddChild(hbox);

		var title = new Label
		{
			Name = "Title",
			Text = _name,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		var effectLbl = new Label { Text = $"Pop: {ratio * 100.0:F2}% | Eco: -{ecoPenalty * 100.0:F0}%" };
		effectLbl.AddThemeFontSizeOverride("font_size", 12);

		var costLbl = new Label { Name = "Cost", Text = $"{cost} PP" };

		var statusLbl = new Label { Name = "Status", Text = "" };
		statusLbl.AddThemeFontSizeOverride("font_size", 12);

		hbox.AddChild(title);
		hbox.AddChild(effectLbl);
		hbox.AddChild(new VSeparator());
		hbox.AddChild(costLbl);
		hbox.AddChild(statusLbl);

		// Make it clickable
		btnPanel.GuiInput += e => _on_law_gui_input(e, btnPanel);

		militaryLawsGrid.AddChild(btnPanel);
	}

	#region --- Interactions ---
	private void _on_law_gui_input(InputEvent @event, PanelContainer btn)
	{
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			return;

		var ratio = btn.GetMeta("ratio").AsDouble();

		// 1. Is this already active?
		if (Mathf.IsEqualApprox(currentCountry.militarySizeRatio, ratio))
			return;

		var cost = btn.GetMeta("cost").AsInt32();

		// 2. Can we afford it?
		if (currentCountry.politicalPower >= cost)
		{
			currentCountry.politicalPower -= cost;
			currentCountry.militarySizeRatio = ratio;
			currentCountry.economyLawPenalty = btn.GetMeta("penalty").AsDouble();

			currentCountry.UpdateManpowerPool(); // Recalc based on new ratio

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
	private static string _format_number(double amount, bool asInt = true)
	{
		var absAmount = Math.Abs(amount);
		var suffix = "";
		var result = amount;

		if (absAmount >= 1000000)
		{
			result = amount * 0.000001;
			suffix = "M";
		}
		else if (absAmount >= 1000)
		{
			result = amount * 0.001;
			suffix = "K";
		}

		if (suffix == "")
			return asInt ? ((int)amount).ToString() : $"{amount:F2}";

		return $"{result:F1}{suffix}";
	}

	public void _on_tab_container_tab_changed(int tab)
	{
		if (currentCountry != null)
			_populate_releasables(currentCountry.countryName);
	}
}
