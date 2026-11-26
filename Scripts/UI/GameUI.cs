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
	[Signal] public delegate void CloseSidemenuEventHandler();

	// ── Enums ─────────────────────────────────────────────
	public enum Context { PLAYER, ENEMY, NEUTRAL, PUPPET, ALLY, SELECT }
	public enum Category { GENERAL, ECONOMY, MILITARY }

	[Export] public PanelContainer settings;

	// ── Top Bar Nodes ─────────────────────────────────────
	[ExportGroup("Top Bar")]
	[Export] public HBoxContainer topbar;
	[Export] public TextureRect nationFlag;
	[Export] public Label labelDate;
	[Export] public PanelContainer radiosPanel;

	[ExportSubgroup("Stats Labels")]
	[Export] public Label labelPp;
	[Export] public Label labelManpower;
	[Export] public Label labelMoney;
	[Export] public Label labelIndustry;
	[Export] public Label labelStability;
	[Export] public Label labelWarSupport;
	[Export] public Label labelWorldTension;

	[Export] public HBoxContainer notifBox;

	// ── Speed Controls ────────────────────────────────────
	[ExportSubgroup("Speed Controls")]
	[Export] public Button plus;
	[Export] public Button minus;
	[Export] public ProgressBar progressBar;

	// ── Side Menu Nodes ───────────────────────────────────
	[ExportGroup("Side Menu")]
	[Export] public Control sidemenu;
	// NOTE(soi): relations already hv the flag so ehhh
	[Export] public Sprite2D sidemenuPointer;
	[Export] public Button playBtn;
	[Export] public Label sidemenuCountryLabel;
	[Export] public TabContainer sidemenuContext;
	[Export] public VBoxContainer sidemenuTrooplist;
	[Export] public VBoxContainer sidemenuBuildings;
	[Export] public AvatarOfHate sidemenuLeaderPortrait;
	[Export] public OptionButton buildingDropdown;

	[Export] public HBoxContainer relationsHbox;
	[Export] public TextureRect flag1;
	[Export] public TextureRect flag2;
	[Export] public TextureRect owner1;
	[Export] public TextureRect owner2;
	[Export] public Label relation1;
	[Export] public Label relation2;

	[Export] public VBoxContainer factionsBox;

	[Export] public PanelContainer factionPrompt;
	[Export] public Label militaryAccessLabel;
	[Export] public TextureRect puppetBadge;
	[Export] public TextureRect warBadge;
	[Export] public Color FALSE_COLOR = new(0.254f, 0.254f, 0.254f, 1.0f);
	[Export] public Color PUPPET_COLOR = new(1.0f, 1.0f, 1.0f, 1.0f);
	[Export] public Color WAR_COLOR = new(1.0f, 0.0f, 0.0f, 1.0f);

	[Export] public VBoxContainer acceptedCultures;
	[Export] public VBoxContainer unacceptedCultures;
	[Export] public VBoxContainer selectPlayerStat;

	private readonly PackedScene actionScene = GD.Load<PackedScene>("res://Scenes/action.tscn");

	[Export] public VBoxContainer radioList;
	[Export] public Label nowPlaying;
	[Export] public TabBar mapTabs;

	[Export] public Line2D sewageGraph;
	[Export] public Line2D populationGraph;
	[Export] public Line2D waterGraph;
	[Export] public Line2D powerGraph;

	// --- MilitaryExtraPanel ---
	[ExportGroup("Military Extra")]
	[Export] public Control militaryExtraPanel;

	[Export] public SpinBox inputDivision;
	[Export] public Button buttonTrain;
	[Export] public OptionButton divisionType;

	[Export] public Label divType;
	[Export] public Label divStats;
	[Export] public Label costs;
	[Export] public Label manpower;

	[Export] public PanelContainer troopContainer;
	[Export] public VBoxContainer troopListParent;

	// --- BuildingDesigner ---
	[ExportGroup("Building Designer")]
	[Export] public PanelContainer buildingDesigner;

	// --- Logistics ---
	[ExportGroup("Logistics")]
	[Export] public ManageCountryUI logistics;

	[Export] public GovernmentUI governmentUI;

	// --- State ---
	public string divisionTypeSelected = "infantry";
	public CountryData selectedCountry = null;

	// Animation State
	[Export] public float slideDuration = 0.2f;
	public bool isOpen = false;
	private Vector2 posOpen = Vector2.Zero;
	private Vector2 posClosed = Vector2.Zero;

	private bool economyUpdate = false;

	// Navigation State
	public Context currentContext = Context.SELECT;
	public Category currentCategory = Category.GENERAL;

	private readonly record struct ActionCost(int cost, bool canAfford);

	private static ActionCost Free(CountryData _p, CountryData _s) => new(0, true);

	// Keyed by the name of the method a side-menu button's `pressed` signal is connected to.
	private readonly Dictionary<string, Func<CountryData, CountryData, ActionCost>> actionCosts;

	private readonly Dictionary<string, Button> allNotifs = new();

	private readonly FontFile customFont =
		GD.Load<FontFile>("res://font/Google_Sans/GoogleSans-VariableFont_GRAD,opsz,wght.ttf");

	// Read by TroopSelection when moving troops.
	public List<DivisionData> selectedDivisionObjects = new();
	private readonly PackedScene DIVISION_CARD_SCENE = GD.Load<PackedScene>("res://Scenes/DivisionItem.tscn");

	private static CountryData Player => CountryManager.Instance.Player;

	private static bool DebugMode
		=> SettingsManager.Instance.settings.GetValue("Developer Options", "debug_mode").AsBool();

	public GameUI()
	{
		actionCosts = new()
		{
			["_declare_war"] = (player, _) => new(50, player.warSupport > 0.5),
			["_request_access"] = (player, selected) => new(25, player.GetRelationWith(selected.countryName) > 70),
			["_force_puppet"] = (player, selected) => new(150, player.gdp > 1000 * selected.gdp),
			["_release_puppet"] = (_, _) => new(50, true),
			["improve_stability"] = (player, _) => new((int)(50 * (1.0 + player.stability)), true),
			["_improve_relations"] = (player, _) => new(player.ideologyName == "liberal" ? 40 : 50, true),
			["_propose_peace"] = Free,
			["_launch_nuke"] = (_, _) => new(250, true),
			["_form_alliance"] = (player, selected) => new(100, player.ideologyName == selected.ideologyName),
			["_demand_tribute"] = (player, selected) => new(75, player.ideologyName == selected.ideologyName),
			["_trade_deal"] = (player, selected) => new(25, player.ideologyName == selected.ideologyName),
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
		var member = FactionManager.Instance.GetFactionMember(player.countryName);
		return new(0, player.GetRelationWith(selected.countryName) > 120 && member != null && member.status == "Leader");
	}

	public override void _EnterTree()
	{
		GameState.Instance.gameUi = this;
	}

	public override void _Ready()
	{
		SetTooltipsOfTabcontainers();

		sidemenuContext.CurrentTab = (int)Context.SELECT;
		foreach (var template in DivisionTemplates.Instance.TEMPLATES.Keys)
		{
			var name = template.AsString();
			divisionType.AddIconItem(
				GD.Load<Texture2D>($"res://starts/{GameState.Instance.currentStart}/assets/division_icons/{name}.svg"),
				name.Capitalize());
		}

		posOpen = sidemenu.Position;
		posClosed = new Vector2(posOpen.X - sidemenu.CustomMinimumSize.X, posOpen.Y);
		sidemenu.Position = posClosed;

		GameState.Instance.gameUi = this;

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
		if (TroopManager.Instance.troopSelection is TroopSelection selection)
			selection.SelectionChanged += _on_troop_selection_changed;

		var clock = GameState.Instance.currentWorld?.clock;
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
		UpdateDivisionMenu();

		if (clock != null)
		{
			clock.HourPassed += _on_time_passed;
			plus.Pressed += clock.IncreaseSpeed;
			minus.Pressed += clock.DecreaseSpeed;
			labelDate.Text = clock.GetDatetimeString();
		}
		else
		{
			GD.PrintErr("GameUI: Clock not found in current world!");
		}

		// NOTE(soi): its soiladin time
		BuildRadioList();

		var compass = (Control)sidemenuPointer.GetParent();
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
		const string defaultMusicPath = "res://assets/music/";
		const string customMusicPath = "res://radios/";
		var music = MusicManager.Instance;
		foreach (var radioKey in music.musicMap[(int)MusicManager.MUSIC.MAIN_THEME].AsGodotDictionary().Keys)
		{
			var radio = radioKey.AsString();
			var musicPath = ResourceLoader.Exists(defaultMusicPath + radio + "/thumbnail.png")
				? defaultMusicPath
				: customMusicPath;
			var entry = new Button
			{
				Text = "\n\n\n" + radio,
				Icon = GD.Load<Texture2D>(musicPath + radio + "/thumbnail.png"),
				ExpandIcon = true,
			};
			entry.SetMeta("radioName", radio);
			entry.Pressed += () =>
			{
				if (music.radios.Contains(radio))
					music.radios.Remove(radio);
				else
					music.radios.Add(radio);
				_update_radio_visuals();
				music.UpdateInteractivePlaylists();
				// Restart current track so the player picks up the updated playlist
				music.currentTrackType = -1;
				music.PlayMusic(music.lastTrackType);
			};
			radioList.AddChild(entry);
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

	public void SetTooltipsOfTabcontainers()
	{
		var tabs = GetNode<TabContainer>("%PlayerTabs");
		tabs.SetTabTooltip(0, "Diplomacy");
		tabs.SetTabTooltip(1, "Economy");
		tabs.SetTabTooltip(2, "Military");
		tabs.SetTabTooltip(3, "Cultures");
	}

	private void _on_compass_gui_input(InputEvent @event)
	{
		if (!(DebugMode && selectedCountry != null))
			return;

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
			// Export on release
			_update_ideology_from_mouse(button.Position, !button.Pressed);
		else if (@event is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
			_update_ideology_from_mouse(motion.Position, false);
	}

	private void _update_ideology_from_mouse(Vector2 localPos, bool doSave)
	{
		if (selectedCountry == null)
			return;

		var ideologyX = Mathf.Remap(Mathf.Clamp(localPos.X, 3, 97), 3, 97, -100, 100);
		var ideologyY = Mathf.Remap(Mathf.Clamp(localPos.Y, 3, 97), 3, 97, -100, 100);

		selectedCountry.ideology = new Vector2(ideologyX, ideologyY);
		DoUpdateSidemenuVisuals();

		if (doSave && DebugMode)
			MapData.Instance.SaveMapData("user://");
	}

	private void _update_radio_visuals()
	{
		var radios = MusicManager.Instance.radios;
		foreach (var node in radioList.GetChildren())
		{
			if (node is not Control child)
				continue;
			var radioName = child.GetMeta("radioName", "").AsString();
			if (radioName == "")
				continue;
			child.Modulate = radios.Contains(radioName) ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
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
		sidemenuPointer.Position = new Vector2(
			Mathf.Remap(country.ideology.X, -100, 100, 3, 97),
			Mathf.Remap(country.ideology.Y, -100, 100, 3, 97));
	}

	public void DoUpdateSidemenuVisuals()
	{
		sidemenuCountryLabel.Text =
			$"{selectedCountry.ideologyName.Capitalize()} {selectedCountry.displayName.Capitalize()}";

		foreach (var entry in factionsBox.GetChildren())
			entry.QueueFree();

		var factions = FactionManager.Instance.factions;
		foreach (var faction in selectedCountry.factions)
		{
			var factionEntry = new Label
			{
				Text = faction.Capitalize(),
				MouseFilter = Control.MouseFilterEnum.Pass,
				TooltipText = string.Concat(factions[faction].members.Select(x => $"\n{x.polity.Capitalize()}: ({x.status})")),
			};
			factionsBox.AddChild(factionEntry);
		}

		puppetBadge.Modulate = selectedCountry.isPuppet ? PUPPET_COLOR : FALSE_COLOR;
		warBadge.Modulate = selectedCountry.IsAtWar ? WAR_COLOR : FALSE_COLOR;

		if (GameState.Instance.interactionType == InteractionType.SELECT_COUNTRY)
		{
			foreach (var stat in selectPlayerStat.GetChildren())
				stat.QueueFree();
			foreach (var stat in SELECT_STATS)
			{
				var statLabel = new Label
				{
					Text = $"{stat.Capitalize()}: {selectedCountry.Get(stat).ToString().Capitalize()}",
				};
				selectPlayerStat.AddChild(statLabel);
			}

			nationFlag.Texture = TroopManager.Instance.GetFlag(selectedCountry.countryName, selectedCountry.ideologyName);
			playBtn.Text = $"Play as {selectedCountry.countryName.Capitalize()}";

			MapManager.Instance.ShowCountriesMap();
		}

		if (selectedCountry.governmentPositions.TryGetValue("Leader", out var leaders) && leaders.Count > 0)
		{
			var figures = MapManager.Instance.significantFigures;
			sidemenuLeaderPortrait.ussr.Clear();
			foreach (var iLeader in leaders)
				sidemenuLeaderPortrait.ussr.Add(ImportantFigureHost.GetPortrait(figures.TryGetValue(iLeader, out var f) ? f : null));
			sidemenuLeaderPortrait.ForceReset();
			sidemenuLeaderPortrait.TooltipText = figures[leaders[0]].GetDisplayString();
		}

		PlacePointer(selectedCountry);

		_update_relations_visuals();
		sidemenuLatch = false;
	}

	private void _on_selected_country_ideology_changed()
	{
		if (selectedCountry != null)
			_update_sidemenu_visuals();
	}

	private void _on_player_change()
	{
		ConnectPlayerIdeology();
		_connect_player_training();

		_update_flag();
		UpdateTopbarStats();
		UpdateCultures();
	}

	private void SelectCountry(string countryName)
	{
		if (selectedCountry != null)
			selectedCountry.IdeologyChanged -= _on_selected_country_ideology_changed;
		selectedCountry = CountryManager.Instance.GetCountry(countryName);
		selectedCountry.IdeologyChanged += _on_selected_country_ideology_changed;
	}

	public void _on_province_clicked(string countryName)
	{
		SelectCountry(countryName);
		_update_sidemenu_visuals();

		var state = GameState.Instance;
		if (state.interactionType != InteractionType.BUILD_PROVINCE && state.industryBuilding != IndustryType.DEFAULT)
			return;

		var player = Player;
		var newContext = Context.NEUTRAL;
		if (countryName == player.countryName)
			newContext = Context.PLAYER;
		else if (WarManager.Instance.IsAtWar(player, selectedCountry))
			newContext = Context.ENEMY;
		else if (player.puppets.Contains(selectedCountry.countryName))
			newContext = Context.PUPPET;
		else if (FactionManager.Instance.InFaction(player, selectedCountry))
			newContext = Context.ALLY;

		var allowed = player.GetAllAllowedCountries();
		relationsHbox.TooltipText = "Military Access: " + (allowed.Contains(selectedCountry.countryName) ? "Yes" : "No");

		sidemenuContext.CurrentTab = (int)newContext;
		OpenSideMenu();
		_update_context_actions_visuals();
	}

	public void ToggleSideMenu()
	{
		if (isOpen)
			CloseSideMenu();
		else
			OpenSideMenu();
	}

	public void ChangeContextCategory(Context context = Context.PLAYER, Category category = Category.GENERAL)
	{
		currentContext = context;
		currentCategory = category;

		sidemenuContext.CurrentTab = (int)context;
		sidemenuContext.GetChild<TabContainer>((int)context).CurrentTab = (int)category;
	}

	public void OpenSideMenu()
	{
		var state = GameState.Instance;
		if (state.interactionType == InteractionType.DEPLOY_PROVINCE || state.industryBuilding != IndustryType.DEFAULT)
			return;
		militaryExtraPanel.Visible = true;

		if (selectedCountry != null)
			PlacePointer(selectedCountry);

		_update_relations_visuals();

		if (!isOpen)
		{
			MusicManager.Instance.PlaySfx((int)SFX.OPEN_MENU);
			SlideIn();
		}

		_update_context_actions_visuals();
		UpdateEconomyMenu();
	}

	private void _update_relations_visuals()
	{
		if (currentCategory != Category.GENERAL)
			return;

		var player = Player;
		var target = selectedCountry;
		var troops = TroopManager.Instance;

		if (player == null || target == null || player == target)
		{
			relationsHbox.Visible = false;
			return;
		}

		relationsHbox.Visible = true;

		// FAR LEFT: Target Flag
		flag1.Texture = troops.GetFlag(target.countryName, target.ideologyName);
		owner1.Visible = target.isPuppet;
		if (target.isPuppet)
			owner1.Texture = troops.GetFlag(target.owner, target.owner);

		// CENTER: Dual Opinions
		relation1.Text = target.GetRelationWith(player.countryName).ToString();
		relation2.Text = player.GetRelationWith(target.countryName).ToString();

		// FAR RIGHT: Player Flag
		flag2.Texture = troops.GetFlag(player.countryName, player.ideologyName);
		owner2.Visible = player.isPuppet;
		if (player.isPuppet)
			owner2.Texture = troops.GetFlag(player.owner, player.owner);
	}

	private bool contextLatch = false;
	private readonly List<(Button btn, string method, string baseText)> _actions_list_cache = new();
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
			foreach (var contextNode in sidemenuContext.GetChildren())
			{
				var actionsList = contextNode.FindChild("ActionsList", true, false);
				if (actionsList == null)
					continue;
				foreach (var child in actionsList.GetChildren())
				{
					if (child is not Button button)
						continue;
					var method = ConnectedMethod(button);
					if (method != "" && actionCosts.ContainsKey(method))
						_actions_list_cache.Add((button, method, button.Text.Split(" (")[0]));
				}
			}
			_actions_list_cached = true;
		}

		if (selectedCountry == null)
		{
			contextLatch = false;
			return;
		}

		var player = Player;
		foreach (var (child, method, baseText) in _actions_list_cache)
		{
			if (!GodotObject.IsInstanceValid(child))
				continue;

			var costData = actionCosts[method](player, selectedCountry);
			child.Disabled = !costData.canAfford;
			child.Text = baseText + (costData.cost > 0 ? $" ({costData.cost} PP)" : "");

			// Visual feedback for disabled buttons
			child.Modulate = child.Disabled ? new Color(1.0f, 0.5f, 0.5f, 0.7f) : Colors.White;
		}

		contextLatch = false;
	}

	public void _on_tab_changed(int newCategoryIndex)
	{
		currentCategory = (Category)newCategoryIndex;
		MusicManager.Instance.PlaySfx((int)SFX.HOVERED);
	}

	public void _on_menu_button_button_up(int _menu_index)
	{
		currentCategory = (Category)_menu_index;
		var player = Player;
		if (player == null || currentContext != Context.PLAYER)
			return;

		var map = MapManager.Instance;
		if (_menu_index == (int)Category.ECONOMY)
		{
			map.ShowIndustryCountry(player.countryName);
			var designs = EconomyManager.Instance.buildingDesigns;
			if (!designs.ContainsKey(player.countryName))
				designs[player.countryName] = new Godot.Collections.Dictionary();
			buildingDropdown.Clear();
			buildingDropdown.AddItem("Infrastructure");
			foreach (var buildingTemplateName in designs[player.countryName].Keys)
				buildingDropdown.AddItem(buildingTemplateName.AsString());
		}
		else
		{
			map.SetCountryColor(player.countryName, Colors.Transparent);
			GameState.Instance.industryBuilding = IndustryType.DEFAULT;
			GameState.Instance.interactionType = InteractionType.DEFAULT;
			map.ShowCountriesMap();
		}
	}

	// NOTE(Z21): Some of the things here are outdated and not used and overall bad way to do things ngl
	private void _build_trooplist()
	{
		foreach (var child in sidemenuTrooplist.GetChildren())
			child.QueueFree();

		var player = Player;

		foreach (var troop in player.ongoingTraining)
		{
			var btn = actionScene.Instantiate<ActionRow>();
			sidemenuTrooplist.AddChild(btn);
			btn.SetupTraining(troop);
			// CountryData.TrainingFinished rebuilds this list (see _connect_player_training)
		}

		// Ready to Deploy
		foreach (var troop in player.readyTroops)
		{
			var btn = actionScene.Instantiate<ActionRow>();
			sidemenuTrooplist.AddChild(btn);
			btn.SetupReady(troop, () => DeployTroop(troop));
		}
	}

	//NOTE(Sockmit2007): Soilad is literally Hitler for this one.

	private static void ShiftGraph(Line2D graph, float newestY)
	{
		var points = graph.Points;
		if (points.Length == 0)
			return;
		for (var i = 0; i < points.Length - 1; i++)
			points[i] = points[i + 1] with { X = points[i + 1].X - 79 };
		points[^1] = new Vector2(316, 236 - newestY);
		graph.Points = points;
	}

	public void UpdateTopbarStats()
	{
		var player = Player;
		if (player == null)
			return;
		labelPp.Text = ((int)Math.Floor(player.politicalPower)).ToString();
		labelStability.Text = $"{Math.Round(player.stability * 100)}%";
		labelManpower.Text = FormatNumber(player.manpower);
		labelMoney.Text = FormatNumber(player.money);
		labelIndustry.Text = player.factoriesAmount.ToString();
		labelWarSupport.Text = $"{player.warSupport * 100}%";
		labelWorldTension.Text = $"{Math.Round(MapManager.Instance.worldTension * 100)}%";

		ShiftGraph(populationGraph, player.totalPopulation * 0.00001f);
		ShiftGraph(sewageGraph, player.totalSewage);
		ShiftGraph(powerGraph, player.totalPower);
		ShiftGraph(waterGraph, player.totalWater);

		_update_notifications();
	}

	private void _on_hour_passed()
	{
		UpdateTopbarStats();
		UpdateDivisionMenu();
		if (isOpen)
			_update_context_actions_visuals();
	}

	// Construction progress and training countdowns only change once per day.
	private void _on_day_passed()
	{
		if (isOpen)
			UpdateEconomyMenu();
		foreach (var row in sidemenuTrooplist.GetChildren())
		{
			if (row is ActionRow action && !action.IsQueuedForDeletion())
				action.RefreshUi();
		}
	}

	private void _on_construction_changed(int _pid, GodotObject country)
	{
		if (country == Player)
			UpdateEconomyMenu();
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
		if (mapTabs.CurrentTab != view)
			mapTabs.CurrentTab = view;
	}

	private void _on_country_selected(string countryName)
	{
		SelectCountry(countryName);
		DoUpdateSidemenuVisuals();
	}

	private void _on_sidemenu_close_requested()
	{
		if (isOpen)
			CloseSideMenu();
	}

	private void _on_sidemenu_toggle_requested()
	{
		if (GameState.Instance.interactionType != InteractionType.SELECT_COUNTRY)
		{
			// NOTE(soi): This swtiches the sidemenu while its closing instead of swithcing after its out of view
			ChangeContextCategory(Context.PLAYER, Category.GENERAL);
			selectedCountry = Player;
			DoUpdateSidemenuVisuals();
		}
		ToggleSideMenu();
	}

	private void _on_track_changed(string trackName)
	{
		if (nowPlaying != null)
			nowPlaying.Text = trackName;
	}

	private void _on_troop_selection_changed()
	{
		var troops = (TroopManager.Instance.troopSelection as TroopSelection)?.selectedTroops ?? new List<TroopData>();
		if (troops.Count == 0)
		{
			selectedDivisionObjects.Clear();
			CloseTroopContainer();
		}
		else
		{
			MakeTroopContainer(troops);
		}
	}

	public static string FormatNumber(double value)
	{
		var absVal = Math.Abs(value);
		var signStr = value < 0 ? "-" : "";
		if (absVal >= 1_000_000_000)
			return signStr + $"{absVal * 0.000000001:F2}B";
		if (absVal >= 1_000_000)
			return signStr + $"{absVal * 0.000001:F2}M";
		if (absVal >= 1_000)
			return signStr + $"{absVal * 0.001:F1}K";
		return signStr + ((long)Math.Floor(absVal)).ToString();
	}

	private void _on_time_passed()
	{
		labelDate.Text = GameState.Instance.currentWorld.clock.GetDatetimeString();
	}

	public void updateProgressBar()
	{
		var clock = GameState.Instance.currentWorld.clock;
		progressBar.Value = clock.timeScale / clock.maxSpeed * 100.0;
		if (progressBar.GetThemeStylebox("background") is StyleBoxFlat bgStyle)
			bgStyle.BorderColor = clock.paused ? Colors.DarkRed : Colors.DarkCyan;
		labelDate.AddThemeColorOverride("font_color", clock.paused ? Colors.Gray : Colors.White);
	}

	private void _update_flag()
	{
		var player = Player;
		if (player == null)
			return;
		nationFlag.Texture = TroopManager.Instance.GetFlag(player.countryName, player.ideologyName);
	}

	public void CloseSideMenu()
	{
		if (isOpen)
			MusicManager.Instance.PlaySfx((int)SFX.CLOSE_MENU);
		GameState.Instance.ResetIndustryBuilding();
		SlideOut();
		militaryExtraPanel.Visible = false;
	}

	public void SlideIn()
	{
		if (isOpen)
			return;
		isOpen = true;
		CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out)
			.TweenProperty(sidemenu, "position", posOpen, slideDuration);
	}

	public void SlideOut()
	{
		if (!isOpen)
			return;
		isOpen = false;
		CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In)
			.TweenProperty(sidemenu, "position", posClosed, slideDuration);
	}

	public void _choose_deploy_city()
	{
		GameState.Instance.interactionType = InteractionType.DEPLOY_PROVINCE;
	}

	// Deducts the action's political power cost; false when the player can't pay.
	private bool TryPay(string action)
	{
		var player = Player;
		var cost = actionCosts[action](player, selectedCountry).cost;
		if (player.politicalPower < cost)
			return false;
		player.politicalPower -= cost;
		return true;
	}

	public void _declare_war()
	{
		if (!TryPay("_declare_war"))
			return;

		var player = Player;
		var dispatcher = ActionDispatcher.Instance;
		var issuer = player.countryName;
		var target = selectedCountry.countryName;
		dispatcher.Submit(new DeclareWarAction { Issuer = issuer, Warer = issuer, Waree = target });

		if (!string.IsNullOrEmpty(selectedCountry.owner))
			dispatcher.Submit(new DeclareWarAction { Issuer = issuer, Warer = issuer, Waree = selectedCountry.owner });
		foreach (var puppet in selectedCountry.puppets)
			dispatcher.Submit(new DeclareWarAction { Issuer = issuer, Warer = issuer, Waree = puppet });

		OpenSideMenu();
	}

	public void _conscript(Godot.Collections.Dictionary _data)
	{
		Player.TrainTroops(1, "infantry");
		UpdateTopbarStats();
	}

	public void DeployTroop(ReadyTroop troop)
	{
		Player.DeployReadyTroop(troop, Player.deployPid);
		_build_trooplist();
	}

	public void ImproveStability()
	{
		if (!TryPay("improve_stability"))
			return;
		Player.stability += 0.02;
		UpdateTopbarStats();
		_update_context_actions_visuals();
	}

	public void _on_building_selected(int index)
	{
		GameState.Instance.industryBuilding = index switch
		{
			0 => IndustryType.FACTORY,
			1 => IndustryType.PORT,
			2 => IndustryType.INFRASTRUCTURE,
			_ => IndustryType.DEFAULT,
		};
		UpdateEconomyMenu();
	}

	public void UpdateEconomyMenu()
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
			var playerProvinces = MapManager.Instance.countryToOwnedProvinces[player.countryName];
			foreach (var child in sidemenuBuildings.GetChildren())
				child.QueueFree();
			foreach (var (pid, construction) in EconomyManager.Instance.constructionQueue)
			{
				if (!playerProvinces.Contains(pid.AsInt32()))
					continue;
				var data = construction.AsGodotDictionary();
				var entry = new ProgressBar
				{
					MaxValue = 10,
					Value = 10 - data["days"].AsDouble(),
					UseParentMaterial = true,
				};
				entry.AddChild(new Label { Text = data["type"].AsString(), UseParentMaterial = true });
				sidemenuBuildings.AddChild(entry);
			}
		}
		economyUpdate = false;
	}

	public void _request_access()
	{
		if (!TryPay("_request_access"))
			return;
		Player.allowedCountries.Add(selectedCountry.countryName);
		MapManager.Instance.AllowPids(Player, selectedCountry);
		_update_context_actions_visuals();
	}

	public void _force_puppet()
	{
		if (!TryPay("_force_puppet"))
			return;
		CountryManager.Instance.MakePuppet(Player, selectedCountry);
		_update_context_actions_visuals();
		CloseSideMenu();
	}

	public void _on_release_puppet_pressed()
	{
		if (!TryPay("_release_puppet"))
			return;
		CountryManager.Instance.ReleasePuppet(Player, selectedCountry);
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

	public void OpenResearchTree() => GD.Print("Opening Research");

	public void OpenDecisionsTree()
	{
		(GetTree().Root.FindChild("DecisionTreeUI", true, false) as DecisionTreeUI)?.OpenMenu();
	}

	public void _open_faction()
	{
		factionPrompt.Visible = !factionPrompt.Visible;
	}

	public void OpenManageCountry()
	{
		if (logistics.Visible)
			logistics.CloseMenu();
		else
			logistics.OpenMenu(Player);
	}

	// NOTE(Z21): Everything below is made by a Clanker. I am way too lazy for UI stuff
	// NOTE(soi): DAMM YOU Z21

	public void MakeTroopContainer(List<TroopData> selectedTroops)
	{
		troopContainer.Visible = true;
		foreach (var child in troopListParent.GetChildren())
			child.QueueFree();

		foreach (var troop in selectedTroops)
		{
			// Group divisions by type, keeping first-seen order: {"infantry": [div1, div2], "tank": [div3]}
			var groups = new Dictionary<string, Godot.Collections.Array>();
			var order = new List<string>();
			foreach (var div in troop.storedDivisions)
			{
				if (!groups.TryGetValue(div.type, out var list))
				{
					list = new Godot.Collections.Array();
					groups[div.type] = list;
					order.Add(div.type);
				}
				list.Add(VariantState.ToVariant(div.ToDict()));
			}

			// Draw one card per type
			foreach (var type in order)
			{
				var divisionsOfType = groups[type];
				var card = DIVISION_CARD_SCENE.Instantiate<DivisionItem>();
				troopListParent.AddChild(card);

				var groupDivisions = divisionsOfType
					.Select(d => DivisionData.FromDict(d.AsGodotDictionary().AsState())).ToList();
				card.SetupGrouped(troop, type, groupDivisions, selectedDivisionObjects.Contains(groupDivisions[0]));
				card.Clicked += _on_group_clicked;
			}
		}
	}

	private void _on_group_clicked(GodotObject cardObject)
	{
		var cardNode = (DivisionItem)cardObject;
		var divsInGroup = cardNode.groupDivisions ?? new List<DivisionData>();

		// Check the first div to see if we are selecting or deselecting the group
		var isAlreadySelected = selectedDivisionObjects.Contains(divsInGroup[0]);

		foreach (var div in divsInGroup)
		{
			if (isAlreadySelected)
				selectedDivisionObjects.Remove(div);
			else if (!selectedDivisionObjects.Contains(div))
				selectedDivisionObjects.Add(div);
		}

		// Toggle the card's visual state
		cardNode.isSelected = !isAlreadySelected;
		cardNode.UpdateVisuals();
	}

	public void CloseTroopContainer()
	{
		troopContainer.Visible = false;
	}

	// --- Main Update Logic ---
	public void UpdateDivisionMenu()
	{
		var templates = DivisionTemplates.Instance.TEMPLATES;
		if (!templates.TryGetValue(divisionTypeSelected, out var statsVariant))
			return;
		var stats = statsVariant.AsGodotDictionary();
		var statManpower = stats["manpower"].AsInt32();
		var statCost = stats["cost"].AsDouble();

		var player = Player;
		if (player != null)
		{
			var maxManpowerDivs = statManpower > 0 ? (double)player.manpower / statManpower : 999;
			var unitCost = statCost * player.divCostMod;
			var maxMoneyDivs = unitCost > 0 ? player.money / unitCost : 999;

			inputDivision.MaxValue = Math.Max(1, Math.Floor(Math.Min(maxManpowerDivs, maxMoneyDivs)));
		}

		var count = (int)Math.Ceiling(inputDivision.Value);

		divType.Text = divisionTypeSelected.Capitalize();
		divStats.Text = $"{stats["attack"]}\n{stats["defense"]}\n{stats["hp"]}";

		var totalManpower = statManpower * count;
		manpower.Text = FormatNumber(totalManpower);

		if (player != null)
		{
			costs.Text = FormatNumber(statCost * count * player.divCostMod);

			// Check Affordability & Update Button State & Visuals
			buttonTrain.Disabled = player.manpower < totalManpower;
		}
	}

	public void _on_button_train_troops()
	{
		if (Player.TrainTroops(Math.Max(1, (int)Math.Ceiling(inputDivision.Value)), divisionTypeSelected))
			_build_trooplist();
		UpdateDivisionMenu();
	}

	public void _on_division_type_button(string type)
	{
		divisionTypeSelected = type;
		UpdateDivisionMenu();
	}

	public void _on_division_type_selected(int index)
	{
		divisionTypeSelected = DivisionTemplates.Instance.TEMPLATES.Keys.ElementAt(index).AsString();
		UpdateDivisionMenu();
	}

	public void _on_input_division_text_changed(float _value)
	{
		UpdateDivisionMenu();
	}

	public void _on_music_pressed()
	{
		radiosPanel.Visible = !radiosPanel.Visible;
		SettingsManager.Instance.settings.Save(SettingsManager.Instance.SETTINGS_FILE);
	}

	public void _on_create_faction_pressed()
	{
		var nameEdit = factionPrompt.GetNode<TextEdit>("VBoxContainer/HBoxContainer/TextEdit");
		var colorPicker = factionPrompt.GetNode<ColorPicker>("VBoxContainer/ColorPicker");
		FactionManager.Instance.CreateFaction(Player.countryName, nameEdit.Text, colorPicker.Color);
		nameEdit.Text = "";
		colorPicker.Color = Colors.White;
		factionPrompt.Visible = !factionPrompt.Visible;
	}

	public void _on_invite_faction_pressed()
	{
		FactionManager.Instance.InviteFaction(Player, selectedCountry);
	}

	public void _on_kick_faction_pressed()
	{
		FactionManager.Instance.KickFaction(Player, selectedCountry);
	}

	public void _on_steal_manpower_pressed()
	{
		if (selectedCountry.totalPopulation <= 0)
			return;
		Player.totalPopulation += 1_000;
		selectedCountry.totalPopulation -= 1_000;
		Player.UpdateManpowerPool();
		selectedCountry.UpdateManpowerPool();
	}

	public void _on_steal_money_pressed()
	{
		if (selectedCountry.money <= 0)
			return;
		Player.money += 1_000;
		selectedCountry.money -= 1_000;
	}

	public void _on_annex_country_pressed()
	{
		ActionDispatcher.Instance.Submit(new AnnexAction
		{
			Issuer = Player.countryName,
			Annexer = Player.countryName,
			Annexee = selectedCountry.countryName,
		});
	}

	public void _on_call_to_arms_pressed()
	{
		ActionDispatcher.Instance.Submit(new CallToArmsAction
		{
			Issuer = Player.countryName,
			Caller = Player.countryName,
			Target = selectedCountry.countryName,
		});
	}

	public void UpdateCultures()
	{
		foreach (var child in acceptedCultures.GetChildren())
			child.QueueFree();

		foreach (var culture in Player.acceptedCultures)
		{
			acceptedCultures.AddChild(new Button { Text = culture.ToString(), UseParentMaterial = true });
		}
	}

	public void _on_next_song_pressed()
	{
		MusicManager.Instance.SkipTrack();
	}

	public void _on_pause_pressed()
	{
		MusicManager.Instance._toggle_pause();
	}

	public void AddNotif(string type, string tooltip)
	{
		if (allNotifs.ContainsKey(type))
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
					OpenDecisionsTree();
					break;
				case "plan":
					OpenManageCountry();
					break;
			}
		};
		notifBox.AddChild(notif);
		allNotifs[type] = notif;
	}

	public void RemoveNotif(string type)
	{
		if (!allNotifs.Remove(type, out var notif))
			return;
		if (GodotObject.IsInstanceValid(notif))
			notif.QueueFree();
	}

	private void SetNotif(bool active, string type, string tooltip)
	{
		if (active)
			AddNotif(type, tooltip);
		else
			RemoveNotif(type);
	}

	private void _update_notifications()
	{
		var player = Player;
		if (player == null)
			return;

		SetNotif(player.stability < 0.5, "stability", "Low Stability");
		SetNotif(player.warSupport < 0.5, "war_support", "Low War Support");
		SetNotif(player.politicalPower < 0, "political_power", "Political Crisis");
		SetNotif(player.money < 0, "money_icon", "Financial Crisis");
		SetNotif(DecisionManager.Instance.HasAvailableDecisions(player), "decision", "Decision Available");
		SetNotif(PlansManager.Instance.HasAvailablePlans(player), "plan", "Plan Available");
	}

	public void _on_deploy_all_pressed()
	{
		foreach (var troop in sidemenuTrooplist.GetChildren())
			troop.EmitSignal(ActionRow.SignalName.pressed);
	}

	public void _on_map_changed(int tab)
	{
		MapManager.Instance.SetMapMode((MapManager.MapView)tab);
	}

	public void _on_building_designer_pressed()
	{
		buildingDesigner.Visible = !buildingDesigner.Visible;
	}

	public void MOnGovernmentPressed()
	{
		governmentUI.OpenMenu(Player);
	}

	public void _on_play_as_pressed()
	{
		CountryManager.Instance.SetPlayerCountry(selectedCountry.countryName);
		GameState.Instance.interactionType = InteractionType.DEFAULT;
		ChangeContextCategory(Context.PLAYER, Category.GENERAL);
		_on_province_clicked(Player.countryName);
	}
}
