using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sosr;

public partial class MapEditorUI : CanvasLayer
{
	[Export] public Sprite2D map_sprite;

	public string selectedCountry = "";
	public string selectedFaction = "";
	public string selectedBiome = "";
	public string selectedResource = "";
	public int selected_pid = -1;
	public int hovered_pid = -1;
	private Color original_pid_color;

	public List<int> multiSelectPID = new();
	public Drag dragging = Drag.NONE;

	public enum Tool
	{
		NONE = 0,
		PAINT_PRIMARY = 1,
		PAINT_SECONDARY = 2,
		PAINT_TERTIARY = 3,
		MULTI_SELECT = 4,
	}

	public enum Mode
	{
		PROVINCE = 0,
		FACTION = 1,
		POLITY = 2,
		BIOME = 3,
		RESOURCE = 4,
	}

	public enum Drag
	{
		NONE = 0,
		LEFT = 1,
		RIGHT = 2,
	}

	public Tool currentTool = Tool.NONE;
	public Mode currentMode = Mode.PROVINCE;

	[Export] public TabContainer leftSidebar;

	[ExportCategory("Province")]
	[Export] public Label status_label;
	[Export] public LineEdit provinceName;
	[Export] public LineEdit input_country;
	[Export] public LineEdit inputOccupier;
	[Export] public LineEdit input_city;
	[Export] public SpinBox input_gdp;
	[Export] public TextEdit input_claims;
	[Export] public VBoxContainer provincePopulationList;
	[Export] public PanelContainer provincePopulationTemplate;
	[Export] public VBoxContainer provinceResourcesList;
	[Export] public PanelContainer provinceResourcesTemplate;

	[ExportCategory("Faction")]
	[Export] public ItemList factionsItemList;
	[Export] public LineEdit factionName;
	[Export] public ColorPickerButton factionColor;
	[Export] public VBoxContainer factionMemberList;
	[Export] public PanelContainer factionMemberTemplate;

	[ExportCategory("Polity")]
	[Export] public ItemList politiesItemList;
	[Export] public CheckBox polityPuppet;
	[Export] public LineEdit polityOwner;
	[Export] public CheckBox polityInExile;
	[Export] public LineEdit polityHost;
	[Export] public LineEdit polityName;
	[Export] public ColorPickerButton polityColor;
	[Export] public SpinBox polityMoney;
	[Export] public SpinBox polityPoliticalPower;
	[Export] public SpinBox polityWarSupport;
	[Export] public SpinBox polityStability;
	[Export] public VBoxContainer polityPuppetList;
	[Export] public PanelContainer polityPuppetTemplate;
	[Export] public VBoxContainer polityFactionList;
	[Export] public PanelContainer polityFactionTemplate;

	[ExportCategory("Biome")]
	[Export] public ItemList biomesItemList;
	[Export] public LineEdit biomeName;
	[Export] public ColorPickerButton biomeColor;

	[ExportCategory("Resource")]
	[Export] public ItemList resourcesItemList;
	[Export] public LineEdit resourceName;
	[Export] public ColorPickerButton resourceColor;
	[Export] public LineEdit resourceIcon;
	[Export] public TextureRect resourceIconTexture;

	private static MapManager Map => MapManager.Instance;
	private static CountryManager Countries => CountryManager.Instance;
	private static Godot.Collections.Dictionary<string, FactionData> Factions => FactionManager.Instance.factions;

	private static Province Prov(int pid) => Map.province_objects[pid];

	private static Province ProvOrNull(int pid)
		=> Map.province_objects.TryGetValue(pid, out var p) ? p : null;

	private static bool HasProv(int pid) => Map.province_objects.ContainsKey(pid);

	private static BiomeData Biome(string name) => Map.biomes[name].As<BiomeData>();

	private static ResourceData Resource(string name) => Map.resources[name].As<ResourceData>();

	public override void _Ready()
	{
		SetupFactionList();
		SetupPolitiesList();
		SetupBiomesList();
		SetupResourcesList();

		input_claims.FocusExited += m_OnProvinceClaimsSubmitted;

		map_sprite ??= GetNodeOrNull<Sprite2D>("../../MapContainer/CultureSprite");
	}

	private static bool ConsoleVisible()
		=> GameConsole.IsVisible();

	public override void _UnhandledInput(InputEvent @event)
	{
		if (GetViewport().GuiGetHoveredControl() != null || ConsoleVisible())
		{
			_clear_hover();
			return;
		}

		if (@event is InputEventMouseMotion motion)
		{
			_handle_hover(motion.Position);
			if (dragging != Drag.NONE)
				_handle_paint_selection(hovered_pid);
		}

		if (@event is InputEventMouseButton mouse)
		{
			var drag = mouse.ButtonIndex switch
			{
				MouseButton.Left => Drag.LEFT,
				MouseButton.Right => Drag.RIGHT,
				_ => Drag.NONE,
			};
			if (drag != Drag.NONE)
			{
				if (mouse.Pressed)
				{
					if (currentTool == Tool.MULTI_SELECT || (currentMode == Mode.BIOME && currentTool == Tool.PAINT_PRIMARY))
					{
						dragging = drag;
						_handle_paint_selection(hovered_pid);
					}
					_handle_click(mouse.Position, drag);
				}
				else
				{
					dragging = Drag.NONE;
				}
			}
		}

		if (@event is InputEventKey { CtrlPressed: true, Keycode: Key.A, Pressed: true }
			&& currentTool == Tool.MULTI_SELECT && multiSelectPID.Count == 1)
		{
			var owned = Map.country_to_owned_provinces[Prov(multiSelectPID[0]).country];
			foreach (var pid in owned)
				AddToMultiSelect(pid, false);
			update_multi_select_ui();
		}
	}

	private bool AddToMultiSelect(int pid, bool refresh = true)
	{
		if (multiSelectPID.Contains(pid))
			return false;
		multiSelectPID.Add(pid);
		Map.SetProvinceColor(pid, Colors.Cornsilk);
		if (refresh)
			update_multi_select_ui();
		return true;
	}

	private bool RemoveFromMultiSelect(int pid, bool refresh = true)
	{
		if (!multiSelectPID.Remove(pid))
			return false;
		Map.ResetProvinceColor(pid);
		if (refresh)
			update_multi_select_ui();
		return true;
	}

	private void _handle_hover(Vector2 screen_pos)
	{
		if (map_sprite == null)
			return;
		var world_pos = GetViewport().GetCanvasTransform().AffineInverse() * screen_pos;
		var pid = Map.GetProvinceWithRadius(world_pos, map_sprite, 5);

		if (pid == hovered_pid)
			return;
		_clear_hover();

		if (pid > 1)
		{
			hovered_pid = pid;
			if (!multiSelectPID.Contains(hovered_pid))
			{
				original_pid_color = Map.state_color_image.GetPixel(pid, 0);
				Map.state_color_image.SetPixel(pid, 0, original_pid_color.Lightened(0.4f));
				Map.state_color_texture.Update(Map.state_color_image);
			}
			Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
		}
	}

	private void _clear_hover()
	{
		if (hovered_pid <= 1)
			return;
		if (!multiSelectPID.Contains(hovered_pid))
			Map.state_color_image.SetPixel(hovered_pid, 0, original_pid_color);
		Map.state_color_texture.Update(Map.state_color_image);
		hovered_pid = -1;
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
	}

	private void PaintBiome(Drag button)
	{
		switch (button)
		{
			case Drag.LEFT:
				if (!Map.biomes.ContainsKey(selectedBiome))
					return;
				var color = Biome(selectedBiome).color;
				Prov(hovered_pid).biome = selectedBiome;
				Map.SetProvinceColors(hovered_pid, color, color);
				original_pid_color = color;
				break;
			case Drag.RIGHT:
				Prov(hovered_pid).biome = "";
				Map.SetProvinceColors(hovered_pid, Colors.AntiqueWhite, Colors.AntiqueWhite);
				original_pid_color = Colors.AntiqueWhite;
				break;
		}
	}

	private void PaintOccupation(Drag button)
	{
		var prov = Prov(hovered_pid);
		switch (button)
		{
			case Drag.LEFT:
				if (prov.country != "Sea" && Countries.countries.ContainsKey(selectedCountry))
				{
					if (prov.country != selectedCountry)
						Map.OccupyProvince(hovered_pid, selectedCountry);
					else
						Map.DeoccupyProvince(hovered_pid);
				}
				break;
			case Drag.RIGHT:
				if (prov.occupier != "")
					Map.DeoccupyProvince(hovered_pid);
				break;
		}
	}

	private void _handle_paint_selection(int pid)
	{
		if (pid <= 1)
			return;
		switch (currentTool)
		{
			case Tool.MULTI_SELECT:
				if (!Input.IsKeyPressed(Key.Ctrl))
					break;
				if (dragging == Drag.LEFT)
					AddToMultiSelect(pid);
				else if (dragging == Drag.RIGHT)
					RemoveFromMultiSelect(pid);
				break;
			case Tool.PAINT_PRIMARY:
				if (Input.IsKeyPressed(Key.Ctrl))
					break;
				if (currentMode == Mode.BIOME)
					PaintBiome(dragging);
				else if (currentMode == Mode.POLITY)
					PaintOccupation(dragging);
				break;
		}
	}

	private void _handle_click(Vector2 _screenPos, Drag a_button)
	{
		GetViewport().GuiReleaseFocus();

		if (hovered_pid <= 1)
			return;

		// Resource mode has no map interaction.
		if (currentTool == Tool.NONE && currentMode != Mode.RESOURCE)
		{
			select_province(hovered_pid);
			return;
		}

		switch (currentMode)
		{
			case Mode.PROVINCE:
				if (currentTool == Tool.MULTI_SELECT)
					MultiSelectProvince(hovered_pid);
				break;
			case Mode.FACTION:
				if (currentTool == Tool.PAINT_PRIMARY)
					PaintFaction(a_button);
				break;
			case Mode.POLITY:
				switch (currentTool)
				{
					case Tool.PAINT_PRIMARY:
						PaintOccupation(a_button);
						break;
					case Tool.PAINT_SECONDARY:
						if (Prov(hovered_pid).country != "Sea" && Countries.countries.ContainsKey(selectedCountry))
						{
							Map.TransferOwnership(hovered_pid, selectedCountry);
							original_pid_color = Countries.GetCountry(selectedCountry).country_color;
						}
						break;
					case Tool.MULTI_SELECT:
						if (a_button == Drag.RIGHT)
							RemoveFromMultiSelect(hovered_pid);
						else if (a_button == Drag.LEFT)
							AddToMultiSelect(hovered_pid);
						break;
				}
				break;
			case Mode.BIOME:
				if (currentTool == Tool.PAINT_PRIMARY)
					PaintBiome(a_button);
				break;
		}
	}

	private void PaintFaction(Drag a_button)
	{
		var country = Prov(hovered_pid).country;
		if (country == "Sea" || !Factions.TryGetValue(selectedFaction, out var faction))
			return;

		switch (a_button)
		{
			case Drag.LEFT:
				switch (faction.GetMemberStatus(country))
				{
					case "Member":
						faction.SetMemberStatus(country, "Leader");
						break;
					case "":
						faction.SetMemberStatus(country, "Member");
						original_pid_color = faction.color;
						break;
				}
				break;
			// The GDScript original matched Drag.LEFT twice, so this demote/kick branch was unreachable.
			case Drag.RIGHT:
				switch (faction.GetMemberStatus(country))
				{
					case "Leader":
						faction.SetMemberStatus(country, "Member");
						break;
					case "Member":
						faction.KickMemberByName(country);
						original_pid_color = Colors.Gray;
						break;
				}
				break;
		}
	}

	public static int IndexOfText(ItemList a_itemList, string a_text)
	{
		for (var i = 0; i < a_itemList.ItemCount; i++)
		{
			if (a_itemList.GetItemText(i) == a_text)
				return i;
		}
		return -1;
	}

	private void SetProvinceFields(Province prov)
	{
		provinceName.Editable = true;
		provinceName.Text = prov.name;
		input_country.Editable = true;
		input_country.Text = prov.country;
		inputOccupier.Editable = true;
		inputOccupier.Text = prov.occupier;
		input_city.Editable = true;
		input_city.Text = prov.city;
		input_gdp.Editable = true;
		input_gdp.Value = prov.gdp;
		input_claims.Editable = true;
		input_claims.Text = string.Join(", ", prov.claims);
	}

	private void ClearProvinceLists()
	{
		foreach (var child in provincePopulationList.GetChildren())
			child.QueueFree();
		foreach (var child in provinceResourcesList.GetChildren())
			child.QueueFree();
	}

	private void FillProvinceLists(Province prov)
	{
		foreach (var population in prov.populations)
			AddPopulationEntry(prov, population);
		foreach (var resource in prov.resources)
			AddResourceEntry(prov, resource);
	}

	private void AddPopulationEntry(Province prov, PopulationData population)
	{
		var entry = (PanelContainer)provincePopulationTemplate.Duplicate();
		entry.Visible = true;
		entry.GetNode<Button>("MarginContainer/HBoxContainer/Button").Pressed += () =>
		{
			prov.populations.Remove(population);
			entry.QueueFree();
		};
		var eth = entry.GetNode<LineEdit>("MarginContainer/HBoxContainer/InputEthName");
		eth.Text = population.ethnicity;
		eth.TextSubmitted += a_ethnicity => population.ethnicity = a_ethnicity;
		var pop = entry.GetNode<Range>("MarginContainer/HBoxContainer/InputPop");
		pop.Value = population.amount;
		pop.ValueChanged += a_amount => population.amount = (int)a_amount;
		provincePopulationList.AddChild(entry);
	}

	private void AddResourceEntry(Province prov, ResourceNode resource)
	{
		var entry = (PanelContainer)provinceResourcesTemplate.Duplicate();
		entry.Visible = true;
		entry.GetNode<Button>("MarginContainer/HBoxContainer/Button").Pressed += () =>
		{
			prov.resources.Remove(resource);
			entry.QueueFree();
		};
		var type = entry.GetNode<LineEdit>("MarginContainer/HBoxContainer/InputResource");
		type.Text = resource.type;
		type.TextSubmitted += a_type => resource.type = a_type;
		var amount = entry.GetNode<Range>("MarginContainer/HBoxContainer/InputAmount");
		amount.Value = resource.amount;
		amount.ValueChanged += a_amount => resource.amount = (int)a_amount;
		var quality = entry.GetNode<Range>("MarginContainer/HBoxContainer/InputQuality");
		// The GDScript original showed resource.amount in the quality field.
		quality.Value = resource.quality;
		quality.ValueChanged += a_quality => resource.quality = (float)a_quality;
		provinceResourcesList.AddChild(entry);
	}

	private static void SelectAndEmit(ItemList list, string text)
	{
		var itemIndex = IndexOfText(list, text);
		list.Select(itemIndex);
		list.EmitSignal(ItemList.SignalName.ItemSelected, itemIndex);
	}

	public void select_province(int pid)
	{
		selected_pid = pid;
		var prov = ProvOrNull(pid);
		if (prov == null)
			return;

		status_label.Text = $"Selected PID: {pid}";
		SetProvinceFields(prov);
		ClearProvinceLists();
		FillProvinceLists(prov);

		switch (currentMode)
		{
			case Mode.POLITY:
				if (prov.country != "Sea")
					SelectAndEmit(politiesItemList, prov.country);
				break;
			case Mode.BIOME:
				if (!string.IsNullOrEmpty(prov.biome))
					SelectAndEmit(biomesItemList, prov.biome);
				break;
		}
	}

	public void MultiSelectProvince(int pid)
	{
		if (pid <= 1)
			return;

		if (!RemoveFromMultiSelect(pid, false))
			AddToMultiSelect(pid, false);

		update_multi_select_ui();
	}

	public void update_multi_select_ui()
	{
		status_label.Text = $"Selected: {multiSelectPID.Count}";
		ClearProvinceLists();

		switch (multiSelectPID.Count)
		{
			case 0:
				provinceName.Editable = false;
				provinceName.Text = "";
				input_country.Editable = false;
				input_country.Text = "";
				inputOccupier.Editable = false;
				inputOccupier.Text = "";
				input_city.Editable = false;
				input_city.Text = "";
				input_gdp.Editable = false;
				input_gdp.Value = 0;
				input_claims.Editable = false;
				input_claims.Text = "";
				break;
			case 1:
			{
				var prov = ProvOrNull(multiSelectPID[0]);
				if (prov == null)
					return;
				SetProvinceFields(prov);
				FillProvinceLists(prov);
				break;
			}
			default:
			{
				var prov = ProvOrNull(multiSelectPID[0]);
				if (prov == null)
					return;
				provinceName.Editable = false;
				provinceName.Text = "...";

				var holdCountry = true;
				var holdOccupier = true;
				var holdGDP = true;
				var holdClaims = true;

				foreach (var iPID in multiSelectPID)
				{
					var other = Prov(iPID);
					holdCountry = holdCountry && prov.country == other.country;
					holdOccupier = holdOccupier && prov.occupier == other.occupier;
					holdGDP = holdGDP && prov.gdp == other.gdp;
					holdClaims = holdClaims && prov.claims.SequenceEqual(other.claims);
					if (!holdCountry && !holdOccupier && !holdGDP && !holdClaims)
						break;
				}

				input_country.Editable = true;
				input_country.Text = holdCountry ? prov.country : "...";
				inputOccupier.Editable = true;
				inputOccupier.Text = holdOccupier ? prov.occupier : "...";
				input_city.Editable = false;
				input_city.Text = "...";
				input_gdp.Editable = true;
				input_gdp.Value = holdGDP ? prov.gdp : 0;
				input_claims.Editable = true;
				input_claims.Text = holdClaims ? string.Join(", ", prov.claims) : "...";
				break;
			}
		}
	}

	// NOTE: the GDScript original returned immediately; the dead body was not ported.
	public void _on_apply_pressed()
	{
	}

	public void _on_export_pressed()
	{
		MapData.Instance.SaveMapData("user://");
	}

	public void m_OnTabContainerTabChanged(int tab)
	{
		m_NoneTool();
		currentMode = (Mode)tab;
		leftSidebar.CurrentTab = tab;
	}

	private static void FillSorted(ItemList list, IEnumerable<string> items)
	{
		list.Clear();
		foreach (var item in items)
			list.AddItem(item);
		list.SortItemsByText();
	}

	public void SetupFactionList() => FillSorted(factionsItemList, Factions.Keys);

	public void SetupPolitiesList() => FillSorted(politiesItemList, Countries.countryNames);

	public void SetupBiomesList() => FillSorted(biomesItemList, Map.biomes.Keys.Select(k => k.AsString()));

	public void SetupResourcesList() => FillSorted(resourcesItemList, Map.resources.Keys.Select(k => k.AsString()));

	private void AddFactionMemberEntry(FactionMember member, bool updateThroughFaction)
	{
		var memberEntry = (PanelContainer)factionMemberTemplate.Duplicate();
		memberEntry.Visible = true;
		memberEntry.GetNode<Button>("HBoxContainer/Button").Pressed += () =>
		{
			Factions[selectedFaction].KickMember(member);
			memberEntry.QueueFree();
		};
		memberEntry.GetNode<LineEdit>("HBoxContainer/LineEdit").Text = member.polity;
		var status = memberEntry.GetNode<OptionButton>("HBoxContainer/OptionButton");
		status.Select(FactionMember.GetIndex(member.status));
		if (updateThroughFaction)
			status.ItemSelected += a_index => Factions[selectedFaction].UpdateMemberStatus(member.polity, (int)a_index);
		else
			status.ItemSelected += a_index => member.status = FactionMember.GetString((int)a_index);
		factionMemberList.AddChild(memberEntry);
	}

	public void m_OnFactionSelected(int index)
	{
		selectedFaction = factionsItemList.GetItemText(index);
		var faction = Factions[selectedFaction];
		factionName.Text = faction.name;
		factionColor.Color = faction.color;
		foreach (var child in factionMemberList.GetChildren())
			child.QueueFree();
		foreach (var member in faction.members)
			AddFactionMemberEntry(member, true);
	}

	public void m_OnPolitySelected(int index)
	{
		selectedCountry = politiesItemList.GetItemText(index);
		var polity = Countries.GetCountry(selectedCountry);
		polityPuppet.SetPressedNoSignal(polity.is_puppet);
		polityOwner.Text = polity.owner;
		polityInExile.SetPressedNoSignal(polity.is_exiled);
		polityHost.Text = polity.host;
		polityName.Text = polity.country_name;
		polityColor.Color = polity.country_color;
		polityMoney.SetValueNoSignal(polity.money);
		polityPoliticalPower.SetValueNoSignal(polity.political_power);
		polityWarSupport.SetValueNoSignal(polity.war_support);
		polityStability.SetValueNoSignal(polity.stability);
		foreach (var child in polityPuppetList.GetChildren())
			child.QueueFree();
		foreach (var puppet in polity.puppets)
		{
			var puppetEntry = (PanelContainer)polityPuppetTemplate.Duplicate();
			puppetEntry.Visible = true;
			puppetEntry.GetNode<Label>("HBoxContainer/Label").Text = puppet;
			polityPuppetList.AddChild(puppetEntry);
		}
		foreach (var child in polityFactionList.GetChildren())
			child.QueueFree();
		foreach (var faction in polity.factions)
		{
			var factionEntry = (PanelContainer)polityFactionTemplate.Duplicate();
			factionEntry.Visible = true;
			factionEntry.GetNode<LineEdit>("HBoxContainer/LineEdit").Text = faction;
			var member = Factions[faction].members.FirstOrDefault(m => m.polity == polity.country_name);
			if (member != null)
				factionEntry.GetNode<OptionButton>("HBoxContainer/OptionButton").Select(FactionMember.GetIndex(member.status));
			polityFactionList.AddChild(factionEntry);
		}
	}

	public void m_OnBiomeSelected(int index)
	{
		selectedBiome = biomesItemList.GetItemText(index);
		var biome = Biome(selectedBiome);
		biomeName.Text = biome.name;
		biomeColor.Color = biome.color;
	}

	public void m_OnBiomeNameSubmitted(string new_text)
	{
		var biomes = Map.biomes;
		if (!biomes.ContainsKey(selectedBiome) || biomes.ContainsKey(new_text))
			return;
		var biome = Biome(selectedBiome);
		biomes[new_text] = biome;
		biome.name = new_text;
		biomes.Remove(selectedBiome);
		foreach (var province in Map.province_objects.Values)
		{
			if (province.biome == selectedBiome)
				province.biome = new_text;
		}
		selectedBiome = new_text;
	}

	public void m_OnBiomeColorChanged(Color color)
	{
		if (Map.biomes.ContainsKey(selectedBiome))
			Biome(selectedBiome).color = color;
	}

	public void m_OnResourceSelected(int index)
	{
		selectedResource = resourcesItemList.GetItemText(index);
		var resource = Resource(selectedResource);
		resourceName.Text = resource.name;
		resourceColor.Color = resource.color;
		resourceIcon.Text = resource.icon;
		resourceIconTexture.Texture = Map.GetResourceIcon(selectedResource);
	}

	public void m_OnResourceNameSubmitted(string new_text)
	{
		var resources = Map.resources;
		if (!resources.ContainsKey(selectedResource) || resources.ContainsKey(new_text))
			return;
		var resource = Resource(selectedResource);
		resources[new_text] = resource;
		resource.name = new_text;
		resources.Remove(selectedResource);
		foreach (var province in Map.province_objects.Values)
		{
			foreach (var node in province.resources)
			{
				if (node.type == selectedResource)
					node.type = new_text;
			}
		}
		selectedResource = new_text;
	}

	public void m_OnResourceColorChanged(Color color)
	{
		if (Map.resources.ContainsKey(selectedResource))
			Resource(selectedResource).color = color;
	}

	public void m_OnResourceIconSubmitted(string new_text)
	{
		if (!Map.resources.ContainsKey(selectedResource))
			return;
		Resource(selectedResource).icon = new_text;
		resourceIconTexture.Texture = Map.GetResourceIcon(selectedResource);
	}

	public void m_OnCitySubmitted(string new_text)
	{
		if (HasProv(selected_pid))
			Prov(selected_pid).city = new_text;
	}

	// Pids the province fields apply to for the current tool.
	private IEnumerable<int> EditedPids()
	{
		switch (currentTool)
		{
			case Tool.NONE:
				if (HasProv(selected_pid))
					yield return selected_pid;
				break;
			case Tool.MULTI_SELECT:
				foreach (var pid in multiSelectPID.ToList())
				{
					if (HasProv(pid))
						yield return pid;
				}
				break;
		}
	}

	public void m_OnProvinceGDPChanged(float value)
	{
		foreach (var pid in EditedPids())
			Prov(pid).gdp = (int)value;
	}

	public void m_OnColorChanged(Color color)
	{
		if (!Countries.countries.ContainsKey(selectedCountry))
			return;
		Countries.GetCountry(selectedCountry).country_color = color;
		// The GDScript original called a nonexistent `setCountryColor`.
		Map.SetCountryColor(selectedCountry, color);
	}

	public void m_OnFactionColorChanged(Color color)
	{
		if (Factions.TryGetValue(selectedFaction, out var faction))
			faction.color = color;
	}

	public void m_OnProvinceNameSubmitted(string new_text)
	{
		if (HasProv(selected_pid))
			Prov(selected_pid).name = new_text;
	}

	public void m_OnProvinceClaimsSubmitted()
	{
		if (input_claims.Text == "...")
			return;

		var final_claims = input_claims.Text.Split(",").Select(c => c.StripEdges()).ToArray();
		foreach (var pid in EditedPids())
			Prov(pid).claims = final_claims;
	}

	public void m_OnProvinceOwnerSubmitted(string new_text)
	{
		if (!Countries.countries.ContainsKey(new_text))
			return;
		foreach (var pid in EditedPids())
			Map.TransferOwnership(pid, new_text);
	}

	public void m_OnProvinceOccupierSubmitted(string new_text)
	{
		foreach (var pid in EditedPids())
		{
			if (string.IsNullOrEmpty(new_text))
				Map.DeoccupyProvince(pid);
			else
				Map.OccupyProvince(pid, new_text);
		}
	}

	public void m_OnPolityNameSubmitted(string new_text)
	{
		if (!Countries.countries.ContainsKey(selectedCountry))
			return;
		Countries.RenameCountry(selectedCountry, new_text);
		selectedCountry = new_text;
	}

	public void m_OnPolityOwnerSubmitted(string new_text)
	{
		var polity = Countries.GetCountry(selectedCountry);
		if (polity == null)
			return;
		if (Countries.countries.ContainsKey(polity.owner))
			Countries.release_puppet(Countries.GetCountry(polity.owner), polity);
		if (Countries.countries.ContainsKey(new_text))
			Countries.make_puppet(Countries.GetCountry(new_text), polity);
	}

	public void m_OnPolityMoneyChanged(float value)
	{
		if (Countries.GetCountry(selectedCountry) is { } polity)
			polity.money = value;
	}

	public void m_OnPolityPoliticalPowerChanged(float value)
	{
		if (Countries.GetCountry(selectedCountry) is { } polity)
			polity.political_power = value;
	}

	public void m_OnPolityWarSupportChanged(float value)
	{
		if (Countries.GetCountry(selectedCountry) is { } polity)
			polity.war_support = value;
	}

	public void m_OnPolityStabilityChanged(float value)
	{
		if (Countries.GetCountry(selectedCountry) is { } polity)
			polity.stability = value;
	}

	private void EndMultiSelect()
	{
		if (currentTool != Tool.MULTI_SELECT)
			return;
		foreach (var pid in multiSelectPID)
			Map.ResetProvinceColor(pid);
		multiSelectPID.Clear();
	}

	public void m_NoneTool()
	{
		EndMultiSelect();
		currentTool = Tool.NONE;
	}

	public void m_PaintOwnerTool()
	{
		EndMultiSelect();
		Map.ShowCountriesMap();
		currentTool = Tool.PAINT_SECONDARY;
	}

	public void m_PaintOccupationTool()
	{
		EndMultiSelect();
		Map.ShowCountriesMap();
		currentTool = Tool.PAINT_PRIMARY;
	}

	public void m_PaintFactionTool()
	{
		EndMultiSelect();
		Map._ShowFactionMap();
		currentTool = Tool.PAINT_PRIMARY;
	}

	public void m_PaintBiomeTool()
	{
		EndMultiSelect();
		Map._ShowBiomesMap();
		currentTool = Tool.PAINT_PRIMARY;
	}

	public void m_MultiSelectTool()
	{
		currentTool = Tool.MULTI_SELECT;
	}

	public void AddedFactionMember()
	{
		var members = Factions[selectedFaction].members;
		AddFactionMemberEntry(members[members.Count - 1], false);
	}

	public void AddProvincePopulation()
	{
		var prov = Prov(selected_pid);
		var population = PopulationData.FromDict(new Godot.Collections.Dictionary
		{
			{ "ethnicity", "Unknown" },
			{ "amount", 1 },
		});
		prov.populations.Add(population);
		AddPopulationEntry(prov, population);
	}

	public void AddProvinceResource()
	{
		var prov = Prov(selected_pid);
		var resource = ResourceNode.FromDict(new Godot.Collections.Dictionary { { "type", "Unknown" } });
		prov.resources.Add(resource);
		AddResourceEntry(prov, resource);
	}
}
