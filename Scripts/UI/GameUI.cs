using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using InteractionType = Sosr.GameState.InteractionType;
using IndustryType = Sosr.GameState.IndustryType;
using SFX = Sosr.MusicManager.SFX;

namespace Sosr;

[GlobalClass]
public partial class GameUI : CanvasLayer
{
	// Emitted when a click couldn't be processed (so likely sea or border)
	[Signal] public delegate void close_sidemenuEventHandler();

	// ── Enums ─────────────────────────────────────────────
	public enum Context { PLAYER, ENEMY, NEUTRAL, PUPPET, ALLY, SELECT }
	public enum Category { GENERAL, ECONOMY, MILITARY }

	[Export] public PanelContainer settings;

	// ── Top Bar Nodes ─────────────────────────────────────
	[ExportGroup("Top Bar")]
	[Export] public HBoxContainer topbar;
	[Export] public TextureRect nation_flag;
	[Export] public Label label_date;
	[Export] public PanelContainer radios_panel;

	[ExportSubgroup("Stats Labels")]
	[Export] public Label label_pp;
	[Export] public Label label_manpower;
	[Export] public Label label_money;
	[Export] public Label label_industry;
	[Export] public Label label_stability;
	[Export] public Label label_war_support;
	[Export] public Label label_world_tension;

	[Export] public HBoxContainer notif_box;

	// ── Speed Controls ────────────────────────────────────
	[ExportSubgroup("Speed Controls")]
	[Export] public Button plus;
	[Export] public Button minus;
	[Export] public ProgressBar progress_bar;

	// ── Side Menu Nodes ───────────────────────────────────
	[ExportGroup("Side Menu")]
	[Export] public Control sidemenu;
	// NOTE(soi): relations already hv the flag so ehhh
	[Export] public Sprite2D sidemenu_pointer;
	[Export] public Button play_btn;
	[Export] public Label sidemenu_country_label;
	[Export] public TabContainer sidemenu_context;
	[Export] public VBoxContainer sidemenu_trooplist;
	[Export] public VBoxContainer sidemenu_buildings;
	[Export] public AvatarOfHate sidemenu_leader_portrait;
	[Export] public OptionButton building_dropdown;

	[Export] public HBoxContainer relations_hbox;
	[Export] public TextureRect flag1;
	[Export] public TextureRect flag2;
	[Export] public TextureRect owner1;
	[Export] public TextureRect owner2;
	[Export] public Label relation1;
	[Export] public Label relation2;

	[Export] public VBoxContainer factions_box;

	[Export] public PanelContainer faction_prompt;
	[Export] public Label military_access_label;
	[Export] public TextureRect puppetBadge;
	[Export] public TextureRect warBadge;
	[Export] public Color FALSE_COLOR = new(0.254f, 0.254f, 0.254f, 1.0f);
	[Export] public Color PUPPET_COLOR = new(1.0f, 1.0f, 1.0f, 1.0f);
	[Export] public Color WAR_COLOR = new(1.0f, 0.0f, 0.0f, 1.0f);

	[Export] public VBoxContainer accepted_cultures;
	[Export] public VBoxContainer unaccepted_cultures;
	[Export] public VBoxContainer select_player_stat;

	private readonly PackedScene action_scene = GD.Load<PackedScene>("res://Scenes/action.tscn");

	[Export] public VBoxContainer radio_list;
	[Export] public Label now_playing;
	[Export] public TabBar map_tabs;

	[Export] public Line2D sewage_graph;
	[Export] public Line2D population_graph;
	[Export] public Line2D water_graph;
	[Export] public Line2D power_graph;

	// --- MilitaryExtraPanel ---
	[ExportGroup("Military Extra")]
	[Export] public Control military_extra_panel;

	[Export] public SpinBox input_division;
	[Export] public Button button_train;
	[Export] public OptionButton division_type;

	[Export] public Label div_type;
	[Export] public Label div_stats;
	[Export] public Label costs;
	[Export] public Label manpower;

	[Export] public PanelContainer troop_container;
	[Export] public VBoxContainer troop_list_parent;

	// --- BuildingDesigner ---
	[ExportGroup("Building Designer")]
	[Export] public PanelContainer building_designer;

	// --- Logistics ---
	[ExportGroup("Logistics")]
	[Export] public ManageCountryUI logistics;

	[Export] public GovernmentUI governmentUI;

	// --- State ---
	public string division_type_selected = "infantry";
	public CountryData selected_country = null;

	// Animation State
	[Export] public float slide_duration = 0.2f;
	public bool is_open = false;
	private Vector2 pos_open = Vector2.Zero;
	private Vector2 pos_closed = Vector2.Zero;

	private bool economyUpdate = false;

	// Navigation State
	public Context current_context = Context.SELECT;
	public Category current_category = Category.GENERAL;

	private readonly record struct ActionCost(int cost, bool can_afford);

	private static ActionCost Free(CountryData _p, CountryData _s) => new(0, true);

	// Keyed by the name of the method a side-menu button's `pressed` signal is connected to.
	private readonly Dictionary<string, Func<CountryData, CountryData, ActionCost>> action_costs;

	private readonly Dictionary<string, Button> all_notifs = new();

	private readonly FontFile custom_font =
		GD.Load<FontFile>("res://font/Google_Sans/GoogleSans-VariableFont_GRAD,opsz,wght.ttf");

	// Read by TroopSelection when moving troops.
	public Godot.Collections.Array<DivisionData> selected_division_objects = new();
	private readonly PackedScene DIVISION_CARD_SCENE = GD.Load<PackedScene>("res://Scenes/DivisionItem.tscn");

	private static CountryData Player => CountryManager.Instance.Player;

	private static bool DebugMode
		=> SettingsManager.Instance.settings.GetValue("Developer Options", "debug_mode").AsBool();

	public GameUI()
	{
		action_costs = new()
		{
			["_declare_war"] = (player, _) => new(50, player.war_support > 0.5),
			["_request_access"] = (player, selected) => new(25, player.get_relation_with(selected.country_name) > 70),
			["_force_puppet"] = (player, selected) => new(150, player.gdp > 1000 * selected.gdp),
			["_release_puppet"] = (_, _) => new(50, true),
			["improve_stability"] = (player, _) => new((int)(50 * (1.0 + player.stability)), true),
			["_improve_relations"] = (player, _) => new(player.ideology_name == "liberal" ? 40 : 50, true),
			["_propose_peace"] = Free,
			["_launch_nuke"] = (_, _) => new(250, true),
			["_form_alliance"] = (player, selected) => new(100, player.ideology_name == selected.ideology_name),
			["_demand_tribute"] = (player, selected) => new(75, player.ideology_name == selected.ideology_name),
			["_trade_deal"] = (player, selected) => new(25, player.ideology_name == selected.ideology_name),
			["open_research_tree"] = Free,
			["open_decisions_tree"] = Free,
			["_open_faction"] = Free,
			["open_manage_country"] = Free,
			["_build_factory"] = Free,
			["_build_port"] = Free,
			["_on_invite_faction_pressed"] = _get_invite_faction_cost,
			["m_BuildInfrastructure"] = Free,
		};
	}

	private static ActionCost _get_invite_faction_cost(CountryData player, CountryData selected)
	{
		var member = FactionManager.Instance.get_faction_member(player.country_name);
		return new(0, player.get_relation_with(selected.country_name) > 120 && member != null && member.status == "Leader");
	}

	public override void _EnterTree()
	{
		GameState.Instance.game_ui = this;
	}

	public override void _Ready()
	{
		set_tooltips_of_tabcontainers();

		sidemenu_context.CurrentTab = (int)Context.SELECT;
		foreach (var template in DivisionTemplates.Instance.TEMPLATES.Keys)
		{
			var name = template.AsString();
			division_type.AddIconItem(
				GD.Load<Texture2D>($"res://starts/{GameState.Instance.current_start}/assets/division_icons/{name}.svg"),
				name.Capitalize());
		}

		pos_open = sidemenu.Position;
		pos_closed = new Vector2(pos_open.X - sidemenu.CustomMinimumSize.X, pos_open.Y);
		sidemenu.Position = pos_closed;

		GameState.Instance.game_ui = this;

		var map = MapManager.Instance;
		map.CountryClicked += _on_province_clicked;
		map.CloseSidemenu += CloseSideMenu;
		map.MapViewChanged += _on_map_view_changed;
		map.CountrySelected += _on_country_selected;
		KeyboardManager.Instance.SidemenuCloseRequested += _on_sidemenu_close_requested;
		KeyboardManager.Instance.SidemenuToggleRequested += _on_sidemenu_toggle_requested;
		MusicManager.Instance.TrackChanged += _on_track_changed;
		EconomyManager.Instance.ConstructionStarted += _on_construction_changed;
		EconomyManager.Instance.ConstructionFinished += _on_construction_changed;
		if (TroopManager.Instance.troop_selection is TroopSelection selection)
			selection.SelectionChanged += _on_troop_selection_changed;

		var clock = GameState.Instance.current_world?.clock;
		if (clock != null)
		{
			clock.HourPassed += _on_hour_passed;
			clock.DayPassed += _on_day_passed;
			clock.ClockUpdate += updateProgressBar;
		}

		CountryManager.Instance.PlayerCountryChanged += _on_player_change;
		ConnectPlayerIdeology();
		_connect_player_training();

		_update_flag();
		updateProgressBar();
		update_division_menu();

		if (clock != null)
		{
			clock.HourPassed += _on_time_passed;
			plus.Pressed += clock.increase_speed;
			minus.Pressed += clock.decrease_speed;
			label_date.Text = clock.get_datetime_string();
		}
		else
		{
			GD.PrintErr("GameUI: Clock not found in current world!");
		}

		// NOTE(soi): its soiladin time
		BuildRadioList();

		var compass = (Control)sidemenu_pointer.GetParent();
		compass.MouseFilter = Control.MouseFilterEnum.Pass;
		compass.GuiInput += _on_compass_gui_input;
	}

	public override void _ExitTree()
	{
		// Autoload and cached-world signals outlive this node; drop our handlers.
		var map = MapManager.Instance;
		map.CountryClicked -= _on_province_clicked;
		map.CloseSidemenu -= CloseSideMenu;
		map.MapViewChanged -= _on_map_view_changed;
		map.CountrySelected -= _on_country_selected;
		KeyboardManager.Instance.SidemenuCloseRequested -= _on_sidemenu_close_requested;
		KeyboardManager.Instance.SidemenuToggleRequested -= _on_sidemenu_toggle_requested;
		MusicManager.Instance.TrackChanged -= _on_track_changed;
		EconomyManager.Instance.ConstructionStarted -= _on_construction_changed;
		EconomyManager.Instance.ConstructionFinished -= _on_construction_changed;
		CountryManager.Instance.PlayerCountryChanged -= _on_player_change;
	}

	private void BuildRadioList()
	{
		const string default_music_path = "res://assets/music/";
		const string custom_music_path = "res://radios/";
		var music = MusicManager.Instance;
		foreach (var radioKey in music.music_map[(int)MusicManager.MUSIC.MAIN_THEME].AsGodotDictionary().Keys)
		{
			var radio = radioKey.AsString();
			var music_path = ResourceLoader.Exists(default_music_path + radio + "/thumbnail.png")
				? default_music_path
				: custom_music_path;
			var entry = new Button
			{
				Text = "\n\n\n" + radio,
				Icon = GD.Load<Texture2D>(music_path + radio + "/thumbnail.png"),
				ExpandIcon = true,
			};
			entry.SetMeta("radio_name", radio);
			entry.Pressed += () =>
			{
				if (music.radios.Contains(radio))
					music.radios.Remove(radio);
				else
					music.radios.Add(radio);
				_update_radio_visuals();
				music.update_interactive_playlists();
				// Restart current track so the player picks up the updated playlist
				music.current_track_type = -1;
				music.play_music(music.last_track_type);
			};
			radio_list.AddChild(entry);
			entry.UseParentMaterial = true;
		}

		_update_radio_visuals();
	}

	private void ConnectPlayerIdeology()
	{
		var player = Player;
		if (player == null)
			return;
		player.IdeologyChanged -= _update_flag;
		player.IdeologyChanged += _update_flag;
	}

	public void set_tooltips_of_tabcontainers()
	{
		var tabs = GetNode<TabContainer>("%PlayerTabs");
		tabs.SetTabTooltip(0, "Diplomacy");
		tabs.SetTabTooltip(1, "Economy");
		tabs.SetTabTooltip(2, "Military");
		tabs.SetTabTooltip(3, "Cultures");
	}

	private void _on_compass_gui_input(InputEvent @event)
	{
		if (!(DebugMode && selected_country != null))
			return;

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
			// Export on release
			_update_ideology_from_mouse(button.Position, !button.Pressed);
		else if (@event is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
			_update_ideology_from_mouse(motion.Position, false);
	}

	private void _update_ideology_from_mouse(Vector2 local_pos, bool do_save)
	{
		if (selected_country == null)
			return;

		var ideology_x = Mathf.Remap(Mathf.Clamp(local_pos.X, 3, 97), 3, 97, -100, 100);
		var ideology_y = Mathf.Remap(Mathf.Clamp(local_pos.Y, 3, 97), 3, 97, -100, 100);

		selected_country.ideology = new Vector2(ideology_x, ideology_y);
		DoUpdateSidemenuVisuals();

		if (do_save && DebugMode)
			MapData.Instance.SaveMapData("user://map_data.json");
	}

	private void _update_radio_visuals()
	{
		var radios = MusicManager.Instance.radios;
		foreach (var node in radio_list.GetChildren())
		{
			if (node is not Control child)
				continue;
			var radio_name = child.GetMeta("radio_name", "").AsString();
			if (radio_name == "")
				continue;
			child.Modulate = radios.Contains(radio_name) ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
		}
	}

	private bool sidemenuLatch = false;

	private void _update_sidemenu_visuals()
	{
		if (!sidemenuLatch)
		{
			sidemenuLatch = true;
			DoUpdateSidemenuVisuals();
		}
	}

	private static readonly string[] SELECT_STATS =
		["money", "gdp", "income", "political_power", "stability", "manpower", "puppets"];

	private void PlacePointer(CountryData country)
	{
		sidemenu_pointer.Position = new Vector2(
			Mathf.Remap(country.ideology.X, -100, 100, 3, 97),
			Mathf.Remap(country.ideology.Y, -100, 100, 3, 97));
	}

	public void DoUpdateSidemenuVisuals()
	{
		sidemenu_country_label.Text =
			$"{selected_country.ideology_name.Capitalize()} {selected_country.display_name.Capitalize()}";

		foreach (var entry in factions_box.GetChildren())
			entry.QueueFree();

		var factions = FactionManager.Instance.factions;
		foreach (var faction in selected_country.factions)
		{
			var faction_entry = new Label
			{
				Text = faction.Capitalize(),
				MouseFilter = Control.MouseFilterEnum.Pass,
				TooltipText = string.Concat(factions[faction].members.Select(x => $"\n{x.polity.Capitalize()}: ({x.status})")),
			};
			factions_box.AddChild(faction_entry);
		}

		puppetBadge.Modulate = selected_country.is_puppet ? PUPPET_COLOR : FALSE_COLOR;
		warBadge.Modulate = selected_country.is_at_war ? WAR_COLOR : FALSE_COLOR;

		if (GameState.Instance.interactionType == InteractionType.SELECT_COUNTRY)
		{
			foreach (var stat in select_player_stat.GetChildren())
				stat.QueueFree();
			foreach (var stat in SELECT_STATS)
			{
				var stat_label = new Label
				{
					Text = $"{stat.Capitalize()}: {selected_country.Get(stat).ToString().Capitalize()}",
				};
				select_player_stat.AddChild(stat_label);
			}

			nation_flag.Texture = TroopManager.Instance.get_flag(selected_country.country_name, selected_country.ideology_name);
			play_btn.Text = $"Play as {selected_country.country_name.Capitalize()}";

			MapManager.Instance.ShowCountriesMap();
		}

		if (selected_country.governmentPositions.TryGetValue("Leader", out var leaders) && leaders.Count > 0)
		{
			var figures = MapManager.Instance.significantFigures;
			sidemenu_leader_portrait.ussr.Clear();
			foreach (var I_leader in leaders)
				sidemenu_leader_portrait.ussr.Add(ImportantFigure.GetPortrait(figures.TryGetValue(I_leader, out var f) ? f : null));
			sidemenu_leader_portrait.ForceReset();
			sidemenu_leader_portrait.TooltipText = figures[leaders[0]].GetDisplayString();
		}

		PlacePointer(selected_country);

		_update_relations_visuals();
		sidemenuLatch = false;
	}

	private void _on_selected_country_ideology_changed()
	{
		if (selected_country != null)
			_update_sidemenu_visuals();
	}

	private void _on_player_change()
	{
		ConnectPlayerIdeology();
		_connect_player_training();

		_update_flag();
		update_topbar_stats();
		update_cultures();
	}

	private void SelectCountry(string country_name)
	{
		if (selected_country != null)
			selected_country.IdeologyChanged -= _on_selected_country_ideology_changed;
		selected_country = CountryManager.Instance.GetCountry(country_name);
		selected_country.IdeologyChanged += _on_selected_country_ideology_changed;
	}

	public void _on_province_clicked(string country_name)
	{
		SelectCountry(country_name);
		_update_sidemenu_visuals();

		var state = GameState.Instance;
		if (state.interactionType != InteractionType.BUILD_PROVINCE && state.industry_building != IndustryType.DEFAULT)
			return;

		var player = Player;
		var new_context = Context.NEUTRAL;
		if (country_name == player.country_name)
			new_context = Context.PLAYER;
		else if (WarManager.Instance.is_at_war(player, selected_country))
			new_context = Context.ENEMY;
		else if (player.puppets.Contains(selected_country.country_name))
			new_context = Context.PUPPET;
		else if (FactionManager.Instance.in_faction(player, selected_country))
			new_context = Context.ALLY;

		var allowed = player.get_all_allowed_countries();
		relations_hbox.TooltipText = "Military Access: " + (allowed.Contains(selected_country.country_name) ? "Yes" : "No");

		sidemenu_context.CurrentTab = (int)new_context;
		OpenSideMenu();
		_update_context_actions_visuals();
	}

	public void ToggleSideMenu()
	{
		if (is_open)
			CloseSideMenu();
		else
			OpenSideMenu();
	}

	public void ChangeContextCategory(Context context = Context.PLAYER, Category category = Category.GENERAL)
	{
		current_context = context;
		current_category = category;

		sidemenu_context.CurrentTab = (int)context;
		sidemenu_context.GetChild<TabContainer>((int)context).CurrentTab = (int)category;
	}

	public void OpenSideMenu()
	{
		var state = GameState.Instance;
		if (state.interactionType == InteractionType.DEPLOY_PROVINCE || state.industry_building != IndustryType.DEFAULT)
			return;
		military_extra_panel.Visible = true;

		if (selected_country != null)
			PlacePointer(selected_country);

		_update_relations_visuals();

		if (!is_open)
		{
			MusicManager.Instance.play_sfx((int)SFX.OPEN_MENU);
			slide_in();
		}

		_update_context_actions_visuals();
		update_economy_menu();
	}

	private void _update_relations_visuals()
	{
		if (current_category != Category.GENERAL)
			return;

		var player = Player;
		var target = selected_country;
		var troops = TroopManager.Instance;

		if (player == null || target == null || player == target)
		{
			relations_hbox.Visible = false;
			return;
		}

		relations_hbox.Visible = true;

		// FAR LEFT: Target Flag
		flag1.Texture = troops.get_flag(target.country_name, target.ideology_name);
		owner1.Visible = target.is_puppet;
		if (target.is_puppet)
			owner1.Texture = troops.get_flag(target.owner, target.owner);

		// CENTER: Dual Opinions
		relation1.Text = target.get_relation_with(player.country_name).ToString();
		relation2.Text = player.get_relation_with(target.country_name).ToString();

		// FAR RIGHT: Player Flag
		flag2.Texture = troops.get_flag(player.country_name, player.ideology_name);
		owner2.Visible = player.is_puppet;
		if (player.is_puppet)
			owner2.Texture = troops.get_flag(player.owner, player.owner);
	}

	private bool contextLatch = false;
	private readonly List<(Button btn, string method, string base_text)> _actions_list_cache = new();
	private bool _actions_list_cached = false;

	private void _update_context_actions_visuals()
	{
		if (!contextLatch)
		{
			contextLatch = true;
			Callable.From(DoUpdateContextActionsVisuals).CallDeferred();
		}
	}

	private string ConnectedMethod(Button button)
	{
		foreach (var connection in button.GetSignalConnectionList(BaseButton.SignalName.Pressed))
		{
			var callable = connection["callable"].AsCallable();
			if (callable.Target == this)
				return callable.Method.ToString();
		}
		return "";
	}

	public void DoUpdateContextActionsVisuals()
	{
		if (!_actions_list_cached)
		{
			_actions_list_cache.Clear();
			foreach (var context_node in sidemenu_context.GetChildren())
			{
				var actions_list = context_node.FindChild("ActionsList", true, false);
				if (actions_list == null)
					continue;
				foreach (var child in actions_list.GetChildren())
				{
					if (child is not Button button)
						continue;
					var method = ConnectedMethod(button);
					if (method != "" && action_costs.ContainsKey(method))
						_actions_list_cache.Add((button, method, button.Text.Split(" (")[0]));
				}
			}
			_actions_list_cached = true;
		}

		if (selected_country == null)
		{
			contextLatch = false;
			return;
		}

		var player = Player;
		foreach (var (child, method, base_text) in _actions_list_cache)
		{
			if (!GodotObject.IsInstanceValid(child))
				continue;

			var cost_data = action_costs[method](player, selected_country);
			child.Disabled = !cost_data.can_afford;
			child.Text = base_text + (cost_data.cost > 0 ? $" ({cost_data.cost} PP)" : "");

			// Visual feedback for disabled buttons
			child.Modulate = child.Disabled ? new Color(1.0f, 0.5f, 0.5f, 0.7f) : Colors.White;
		}

		contextLatch = false;
	}

	public void _on_tab_changed(int new_category_index)
	{
		current_category = (Category)new_category_index;
		MusicManager.Instance.play_sfx((int)SFX.HOVERED);
	}

	public void _on_menu_button_button_up(int _menu_index)
	{
		current_category = (Category)_menu_index;
		var player = Player;
		if (player == null || current_context != Context.PLAYER)
			return;

		var map = MapManager.Instance;
		if (_menu_index == (int)Category.ECONOMY)
		{
			map.ShowIndustryCountry(player.country_name);
			var designs = EconomyManager.Instance.building_designs;
			if (!designs.ContainsKey(player.country_name))
				designs[player.country_name] = new Godot.Collections.Dictionary();
			building_dropdown.Clear();
			building_dropdown.AddItem("Infrastructure");
			foreach (var buildingTemplateName in designs[player.country_name].Keys)
				building_dropdown.AddItem(buildingTemplateName.AsString());
		}
		else
		{
			map.SetCountryColor(player.country_name, Colors.Transparent);
			GameState.Instance.industry_building = IndustryType.DEFAULT;
			GameState.Instance.interactionType = InteractionType.DEFAULT;
			map.ShowCountriesMap();
		}
	}

	// NOTE(Z21): Some of the things here are outdated and not used and overall bad way to do things ngl
	private void _build_trooplist()
	{
		foreach (var child in sidemenu_trooplist.GetChildren())
			child.QueueFree();

		var player = Player;

		foreach (var troop in player.ongoing_training)
		{
			var btn = action_scene.Instantiate<ActionRow>();
			sidemenu_trooplist.AddChild(btn);
			btn.setup_training(troop);
			// CountryData.TrainingFinished rebuilds this list (see _connect_player_training)
		}

		// Ready to Deploy
		foreach (var troop in player.ready_troops)
		{
			var btn = action_scene.Instantiate<ActionRow>();
			sidemenu_trooplist.AddChild(btn);
			btn.setup_ready(troop, () => deploy_troop(troop));
		}
	}

	//NOTE(Sockmit2007): Soilad is literally Hitler for this one.

	private static void ShiftGraph(Line2D graph, float newest_y)
	{
		var points = graph.Points;
		if (points.Length == 0)
			return;
		for (var i = 0; i < points.Length - 1; i++)
			points[i] = points[i + 1] with { X = points[i + 1].X - 79 };
		points[^1] = new Vector2(316, 236 - newest_y);
		graph.Points = points;
	}

	public void update_topbar_stats()
	{
		var player = Player;
		if (player == null)
			return;
		label_pp.Text = ((int)Math.Floor(player.political_power)).ToString();
		label_stability.Text = $"{Math.Round(player.stability * 100)}%";
		label_manpower.Text = format_number(player.manpower);
		label_money.Text = format_number(player.money);
		label_industry.Text = player.factories_amount.ToString();
		label_war_support.Text = $"{player.war_support * 100}%";
		label_world_tension.Text = $"{Math.Round(MapManager.Instance.world_tension * 100)}%";

		ShiftGraph(population_graph, player.total_population * 0.00001f);
		ShiftGraph(sewage_graph, player.total_sewage);
		ShiftGraph(power_graph, player.total_power);
		ShiftGraph(water_graph, player.total_water);

		_update_notifications();
	}

	private void _on_hour_passed()
	{
		update_topbar_stats();
		update_division_menu();
		if (is_open)
			_update_context_actions_visuals();
	}

	// Construction progress and training countdowns only change once per day.
	private void _on_day_passed()
	{
		if (is_open)
			update_economy_menu();
		foreach (var row in sidemenu_trooplist.GetChildren())
		{
			if (row is ActionRow action && !action.IsQueuedForDeletion())
				action.refresh_ui();
		}
	}

	private void _on_construction_changed(int _pid, GodotObject country)
	{
		if (country == Player)
			update_economy_menu();
	}

	// Tracks which country TrainingFinished is connected on, so a player switch moves it.
	private CountryData _training_signal_country;

	private void _connect_player_training()
	{
		if (_training_signal_country != null)
			_training_signal_country.TrainingFinished -= _build_trooplist;
		_training_signal_country = Player;
		if (_training_signal_country != null)
			_training_signal_country.TrainingFinished += _build_trooplist;
	}

	private void _on_map_view_changed(int view)
	{
		// Setting current_tab emits tab_changed, which calls SetMapMode again. Only set it if it differs.
		if (map_tabs.CurrentTab != view)
			map_tabs.CurrentTab = view;
	}

	private void _on_country_selected(string country_name)
	{
		SelectCountry(country_name);
		DoUpdateSidemenuVisuals();
	}

	private void _on_sidemenu_close_requested()
	{
		if (is_open)
			CloseSideMenu();
	}

	private void _on_sidemenu_toggle_requested()
	{
		if (GameState.Instance.interactionType != InteractionType.SELECT_COUNTRY)
		{
			// NOTE(soi): This swtiches the sidemenu while its closing instead of swithcing after its out of view
			ChangeContextCategory(Context.PLAYER, Category.GENERAL);
			selected_country = Player;
			DoUpdateSidemenuVisuals();
		}
		ToggleSideMenu();
	}

	private void _on_track_changed(string track_name)
	{
		if (now_playing != null)
			now_playing.Text = track_name;
	}

	private void _on_troop_selection_changed(Godot.Collections.Array troops)
	{
		if (troops.Count == 0)
		{
			selected_division_objects.Clear();
			close_troop_container();
		}
		else
		{
			make_troop_container(troops.Select(t => t.As<TroopData>()).ToList());
		}
	}

	public static string format_number(double value)
	{
		var abs_val = Math.Abs(value);
		var sign_str = value < 0 ? "-" : "";
		if (abs_val >= 1_000_000_000)
			return sign_str + $"{abs_val * 0.000000001:F2}B";
		if (abs_val >= 1_000_000)
			return sign_str + $"{abs_val * 0.000001:F2}M";
		if (abs_val >= 1_000)
			return sign_str + $"{abs_val * 0.001:F1}K";
		return sign_str + ((long)Math.Floor(abs_val)).ToString();
	}

	private void _on_time_passed()
	{
		label_date.Text = GameState.Instance.current_world.clock.get_datetime_string();
	}

	public void updateProgressBar()
	{
		var clock = GameState.Instance.current_world.clock;
		progress_bar.Value = clock.time_scale / clock.max_speed * 100.0;
		if (progress_bar.GetThemeStylebox("background") is StyleBoxFlat bg_style)
			bg_style.BorderColor = clock.paused ? Colors.DarkRed : Colors.DarkCyan;
		label_date.AddThemeColorOverride("font_color", clock.paused ? Colors.Gray : Colors.White);
	}

	private void _update_flag()
	{
		var player = Player;
		if (player == null)
			return;
		nation_flag.Texture = TroopManager.Instance.get_flag(player.country_name, player.ideology_name);
	}

	public void CloseSideMenu()
	{
		if (is_open)
			MusicManager.Instance.play_sfx((int)SFX.CLOSE_MENU);
		GameState.Instance.reset_industry_building();
		slide_out();
		military_extra_panel.Visible = false;
	}

	public void slide_in()
	{
		if (is_open)
			return;
		is_open = true;
		CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out)
			.TweenProperty(sidemenu, "position", pos_open, slide_duration);
	}

	public void slide_out()
	{
		if (!is_open)
			return;
		is_open = false;
		CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In)
			.TweenProperty(sidemenu, "position", pos_closed, slide_duration);
	}

	public void _choose_deploy_city()
	{
		GameState.Instance.interactionType = InteractionType.DEPLOY_PROVINCE;
	}

	// Deducts the action's political power cost; false when the player can't pay.
	private bool TryPay(string action)
	{
		var player = Player;
		var cost = action_costs[action](player, selected_country).cost;
		if (player.political_power < cost)
			return false;
		player.political_power -= cost;
		return true;
	}

	public void _declare_war()
	{
		if (!TryPay("_declare_war"))
			return;

		var player = Player;
		var wars = WarManager.Instance;
		wars.declare_war(player, selected_country);

		if (!string.IsNullOrEmpty(selected_country.owner))
			wars.declare_war(player, CountryManager.Instance.GetCountry(selected_country.owner));
		foreach (var puppet in selected_country.puppets)
			wars.declare_war(player, CountryManager.Instance.GetCountry(puppet));

		OpenSideMenu();
	}

	public void _conscript(Godot.Collections.Dictionary _data)
	{
		Player.train_troops(1, "infantry");
		update_topbar_stats();
	}

	public void deploy_troop(ReadyTroop troop)
	{
		Player.deploy_ready_troop(troop, Player.deploy_pid);
		_build_trooplist();
	}

	public void improve_stability()
	{
		if (!TryPay("improve_stability"))
			return;
		Player.stability += 0.02;
		update_topbar_stats();
		_update_context_actions_visuals();
	}

	public void _on_building_selected(int index)
	{
		GameState.Instance.industry_building = index switch
		{
			0 => IndustryType.FACTORY,
			1 => IndustryType.PORT,
			2 => IndustryType.INFRASTRUCTURE,
			_ => IndustryType.DEFAULT,
		};
		update_economy_menu();
	}

	public void update_economy_menu()
	{
		if (!economyUpdate)
		{
			Callable.From(DoEconomyMenuUpdate).CallDeferred();
			economyUpdate = true;
		}
	}

	public void DoEconomyMenuUpdate()
	{
		var player = Player;
		if (player != null)
		{
			var playerProvinces = MapManager.Instance.country_to_owned_provinces[player.country_name].AsGodotArray();
			foreach (var child in sidemenu_buildings.GetChildren())
				child.QueueFree();
			foreach (var (pid, construction) in EconomyManager.Instance.construction_queue)
			{
				if (!playerProvinces.Contains(pid))
					continue;
				var data = construction.AsGodotDictionary();
				var entry = new ProgressBar
				{
					MaxValue = 10,
					Value = 10 - data["days"].AsDouble(),
					UseParentMaterial = true,
				};
				entry.AddChild(new Label { Text = data["type"].AsString(), UseParentMaterial = true });
				sidemenu_buildings.AddChild(entry);
			}
		}
		economyUpdate = false;
	}

	public void _request_access()
	{
		if (!TryPay("_request_access"))
			return;
		Player.allowedCountries.Add(selected_country.country_name);
		MapManager.Instance.AllowPids(Player, selected_country);
		_update_context_actions_visuals();
	}

	public void _force_puppet()
	{
		if (!TryPay("_force_puppet"))
			return;
		CountryManager.Instance.make_puppet(Player, selected_country);
		_update_context_actions_visuals();
		CloseSideMenu();
	}

	public void _on_release_puppet_pressed()
	{
		if (!TryPay("_release_puppet"))
			return;
		CountryManager.Instance.release_puppet(Player, selected_country);
		_update_context_actions_visuals();
		CloseSideMenu();
	}

	public void _improve_relations()
	{
		if (!TryPay("_improve_relations"))
			return;
		GD.Print("Improving relations");
		_update_context_actions_visuals();
	}

	public void _propose_peace() => GD.Print("Proposing peace");

	public void _launch_nuke() => GD.Print("NUKE!");

	public void _demand_tribute() => GD.Print("Pay up!");

	public void _trade_deal() => GD.Print("Trading...");

	public void open_research_tree() => GD.Print("Opening Research");

	public void open_decisions_tree()
	{
		(GetTree().Root.FindChild("DecisionTreeUI", true, false) as DecisionTreeUI)?.OpenMenu();
	}

	public void _open_faction()
	{
		faction_prompt.Visible = !faction_prompt.Visible;
	}

	public void open_manage_country()
	{
		if (logistics.Visible)
			logistics.CloseMenu();
		else
			logistics.OpenMenu(Player);
	}

	// NOTE(Z21): Everything below is made by a Clanker. I am way too lazy for UI stuff
	// NOTE(soi): DAMM YOU Z21

	public void make_troop_container(List<TroopData> selected_troops)
	{
		troop_container.Visible = true;
		foreach (var child in troop_list_parent.GetChildren())
			child.QueueFree();

		foreach (var troop in selected_troops)
		{
			// Group divisions by type, keeping first-seen order: {"infantry": [div1, div2], "tank": [div3]}
			var groups = new Dictionary<string, Godot.Collections.Array>();
			var order = new List<string>();
			foreach (var div in troop.stored_divisions)
			{
				if (!groups.TryGetValue(div.type, out var list))
				{
					list = new Godot.Collections.Array();
					groups[div.type] = list;
					order.Add(div.type);
				}
				list.Add(div);
			}

			// Draw one card per type
			foreach (var type in order)
			{
				var divisions_of_type = groups[type];
				var card = DIVISION_CARD_SCENE.Instantiate<DivisionItem>();
				troop_list_parent.AddChild(card);

				var first = divisions_of_type[0].As<DivisionData>();
				card.setup_grouped(troop, type, divisions_of_type, selected_division_objects.Contains(first));
				card.Clicked += _on_group_clicked;
			}
		}
	}

	private void _on_group_clicked(GodotObject card_object, Variant data)
	{
		var card_node = (DivisionItem)card_object;
		var divs_in_group = data.AsGodotArray().Select(d => d.As<DivisionData>()).ToList();

		// Check the first div to see if we are selecting or deselecting the group
		var is_already_selected = selected_division_objects.Contains(divs_in_group[0]);

		foreach (var div in divs_in_group)
		{
			if (is_already_selected)
				selected_division_objects.Remove(div);
			else if (!selected_division_objects.Contains(div))
				selected_division_objects.Add(div);
		}

		// Toggle the card's visual state
		card_node.is_selected = !is_already_selected;
		card_node.update_visuals();
	}

	public void close_troop_container()
	{
		troop_container.Visible = false;
	}

	// --- Main Update Logic ---
	public void update_division_menu()
	{
		var templates = DivisionTemplates.Instance.TEMPLATES;
		if (!templates.TryGetValue(division_type_selected, out var statsVariant))
			return;
		var stats = statsVariant.AsGodotDictionary();
		var stat_manpower = stats["manpower"].AsInt32();
		var stat_cost = stats["cost"].AsDouble();

		var player = Player;
		if (player != null)
		{
			var max_manpower_divs = stat_manpower > 0 ? (double)player.manpower / stat_manpower : 999;
			var unit_cost = stat_cost * player.divCostMod;
			var max_money_divs = unit_cost > 0 ? player.money / unit_cost : 999;

			input_division.MaxValue = Math.Max(1, Math.Floor(Math.Min(max_manpower_divs, max_money_divs)));
		}

		var count = (int)Math.Ceiling(input_division.Value);

		div_type.Text = division_type_selected.Capitalize();
		div_stats.Text = $"{stats["attack"]}\n{stats["defense"]}\n{stats["hp"]}";

		var total_manpower = stat_manpower * count;
		manpower.Text = format_number(total_manpower);

		if (player != null)
		{
			costs.Text = format_number(stat_cost * count * player.divCostMod);

			// Check Affordability & Update Button State & Visuals
			button_train.Disabled = player.manpower < total_manpower;
		}
	}

	public void _on_button_train_troops()
	{
		if (Player.train_troops(Math.Max(1, (int)Math.Ceiling(input_division.Value)), division_type_selected))
			_build_trooplist();
		update_division_menu();
	}

	public void _on_division_type_button(string type)
	{
		division_type_selected = type;
		update_division_menu();
	}

	public void _on_division_type_selected(int index)
	{
		division_type_selected = DivisionTemplates.Instance.TEMPLATES.Keys.ElementAt(index).AsString();
		update_division_menu();
	}

	public void _on_input_division_text_changed(float _value)
	{
		update_division_menu();
	}

	public void _on_music_pressed()
	{
		radios_panel.Visible = !radios_panel.Visible;
		SettingsManager.Instance.settings.Save(SettingsManager.Instance.SETTINGS_FILE);
	}

	public void _on_create_faction_pressed()
	{
		var name_edit = faction_prompt.GetNode<TextEdit>("VBoxContainer/HBoxContainer/TextEdit");
		var color_picker = faction_prompt.GetNode<ColorPicker>("VBoxContainer/ColorPicker");
		FactionManager.Instance.create_faction(Player.country_name, name_edit.Text, color_picker.Color);
		name_edit.Text = "";
		color_picker.Color = Colors.White;
		faction_prompt.Visible = !faction_prompt.Visible;
	}

	public void _on_invite_faction_pressed()
	{
		FactionManager.Instance.invite_faction(Player, selected_country);
	}

	public void _on_kick_faction_pressed()
	{
		FactionManager.Instance.kick_faction(Player, selected_country);
	}

	public void _on_steal_manpower_pressed()
	{
		if (selected_country.total_population <= 0)
			return;
		Player.total_population += 1_000;
		selected_country.total_population -= 1_000;
		Player.UpdateManpowerPool();
		selected_country.UpdateManpowerPool();
	}

	public void _on_steal_money_pressed()
	{
		if (selected_country.money <= 0)
			return;
		Player.money += 1_000;
		selected_country.money -= 1_000;
	}

	public void _on_annex_country_pressed()
	{
		MapManager.Instance.AnnexCountry(Player.country_name, selected_country.country_name);
	}

	public void _on_call_to_arms_pressed()
	{
		WarManager.Instance.call_to_arms(Player, selected_country);
	}

	public void update_cultures()
	{
		foreach (var child in accepted_cultures.GetChildren())
			child.QueueFree();

		foreach (var culture in Player.accepted_cultures)
		{
			accepted_cultures.AddChild(new Button { Text = culture.ToString(), UseParentMaterial = true });
		}
	}

	public void _on_next_song_pressed()
	{
		MusicManager.Instance.skip_track();
	}

	public void _on_pause_pressed()
	{
		MusicManager.Instance._toggle_pause();
	}

	public void add_notif(string type, string tooltip)
	{
		if (all_notifs.ContainsKey(type))
			return;
		var notif = new Button
		{
			Icon = GD.Load<Texture2D>("res://assets/icons/" + type + ".svg"),
			ExpandIcon = true,
			UseParentMaterial = true,
			CustomMinimumSize = new Vector2(30, 0),
			TooltipText = tooltip,
		};
		notif.Pressed += () =>
		{
			switch (type)
			{
				case "decision":
					open_decisions_tree();
					break;
				case "plan":
					open_manage_country();
					break;
			}
		};
		notif_box.AddChild(notif);
		all_notifs[type] = notif;
	}

	public void remove_notif(string type)
	{
		if (!all_notifs.Remove(type, out var notif))
			return;
		if (GodotObject.IsInstanceValid(notif))
			notif.QueueFree();
	}

	private void SetNotif(bool active, string type, string tooltip)
	{
		if (active)
			add_notif(type, tooltip);
		else
			remove_notif(type);
	}

	private void _update_notifications()
	{
		var player = Player;
		if (player == null)
			return;

		SetNotif(player.stability < 0.5, "stability", "Low Stability");
		SetNotif(player.war_support < 0.5, "war_support", "Low War Support");
		SetNotif(player.political_power < 0, "political_power", "Political Crisis");
		SetNotif(player.money < 0, "money_icon", "Financial Crisis");
		SetNotif(DecisionManager.Instance.has_available_decisions(player), "decision", "Decision Available");
		SetNotif(PlansManager.Instance.has_available_plans(player), "plan", "Plan Available");
	}

	public void _on_deploy_all_pressed()
	{
		foreach (var troop in sidemenu_trooplist.GetChildren())
			troop.EmitSignal(ActionRow.SignalName.pressed);
	}

	public void _on_map_changed(int tab)
	{
		MapManager.Instance.SetMapMode((MapManager.MapView)tab);
	}

	public void _on_building_designer_pressed()
	{
		building_designer.Visible = !building_designer.Visible;
	}

	public void m_OnGovernmentPressed()
	{
		governmentUI.OpenMenu(Player);
	}

	public void _on_play_as_pressed()
	{
		CountryManager.Instance.set_player_country(selected_country.country_name);
		GameState.Instance.interactionType = InteractionType.DEFAULT;
		ChangeContextCategory(Context.PLAYER, Category.GENERAL);
		_on_province_clicked(Player.country_name);
	}
}
