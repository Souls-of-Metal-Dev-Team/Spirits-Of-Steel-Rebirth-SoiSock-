using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sosr;

public partial class MapEditorUI : CanvasLayer
{
	[Export] public Sprite2D mapSprite;

	public string selectedCountry = "";
	public string selectedFaction = "";
	public string selectedBiome = "";
	public string selectedResource = "";
	public int selectedPid = -1;
	public int hoveredPid = -1;
	private Color originalPidColor;

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
	[Export] public Label statusLabel;
	[Export] public LineEdit provinceName;
	[Export] public LineEdit inputCountry;
	[Export] public LineEdit inputOccupier;
	[Export] public LineEdit inputCity;
	[Export] public SpinBox inputGdp;
	[Export] public TextEdit inputClaims;
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
	private static Dictionary<string, FactionData> Factions => FactionManager.Instance.factions;

	private static Province Prov(int pid) => Map.provinceObjects[pid];

	private static Province ProvOrNull(int pid)
		=> Map.provinceObjects.TryGetValue(pid, out var p) ? p : null;

	private static bool HasProv(int pid) => Map.provinceObjects.ContainsKey(pid);

	private static BiomeData Biome(string name) => Map.biomes[name];

	private static ResourceData Resource(string name) => Map.resources[name];

	public override void _Ready()
	{
		SetupFactionList();
		SetupPolitiesList();
		SetupBiomesList();
		SetupResourcesList();

		inputClaims.FocusExited += MOnProvinceClaimsSubmitted;

		mapSprite ??= GetNodeOrNull<Sprite2D>("../../MapContainer/CultureSprite");
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
				_handle_paint_selection(hoveredPid);
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
						_handle_paint_selection(hoveredPid);
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
			var owned = Map.countryToOwnedProvinces[Prov(multiSelectPID[0]).country];
			foreach (var pid in owned)
				AddToMultiSelect(pid, false);
			UpdateMultiSelectUi();
		}
	}

	private bool AddToMultiSelect(int pid, bool refresh = true)
	{
		if (multiSelectPID.Contains(pid))
			return false;
		multiSelectPID.Add(pid);
		Map.SetProvinceColor(pid, Colors.Cornsilk);
		if (refresh)
			UpdateMultiSelectUi();
		return true;
	}

	private bool RemoveFromMultiSelect(int pid, bool refresh = true)
	{
		if (!multiSelectPID.Remove(pid))
			return false;
		Map.ResetProvinceColor(pid);
		if (refresh)
			UpdateMultiSelectUi();
		return true;
	}

	private void _handle_hover(Vector2 screenPos)
	{
		if (mapSprite == null)
			return;
		var worldPos = GetViewport().GetCanvasTransform().AffineInverse() * screenPos;
		var pid = Map.GetProvinceWithRadius(worldPos, mapSprite, 5);

		if (pid == hoveredPid)
			return;
		_clear_hover();

		if (pid > 1)
		{
			hoveredPid = pid;
			if (!multiSelectPID.Contains(hoveredPid))
			{
				originalPidColor = Map.stateColorImage.GetPixel(pid, 0);
				Map.stateColorImage.SetPixel(pid, 0, originalPidColor.Lightened(0.4f));
				Map.stateColorTexture.Update(Map.stateColorImage);
			}
			Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
		}
	}

	private void _clear_hover()
	{
		if (hoveredPid <= 1)
			return;
		if (!multiSelectPID.Contains(hoveredPid))
			Map.stateColorImage.SetPixel(hoveredPid, 0, originalPidColor);
		Map.stateColorTexture.Update(Map.stateColorImage);
		hoveredPid = -1;
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
				Prov(hoveredPid).biome = selectedBiome;
				Map.SetProvinceColors(hoveredPid, color.ToColor(), color.ToColor());
				originalPidColor = color.ToColor();
				break;
			case Drag.RIGHT:
				Prov(hoveredPid).biome = "";
				Map.SetProvinceColors(hoveredPid, Colors.AntiqueWhite, Colors.AntiqueWhite);
				originalPidColor = Colors.AntiqueWhite;
				break;
		}
	}

	private void PaintOccupation(Drag button)
	{
		var prov = Prov(hoveredPid);
		switch (button)
		{
			case Drag.LEFT:
				if (prov.country != "Sea" && Countries.countries.ContainsKey(selectedCountry))
				{
					if (prov.country != selectedCountry)
						ActionDispatcher.Instance.Submit(new OccupyProvinceAction
						{
							Issuer = selectedCountry, ProvinceId = hoveredPid, Occupier = selectedCountry,
						});
					else
						Map.DeoccupyProvince(hoveredPid);
				}
				break;
			case Drag.RIGHT:
				if (prov.occupier != "")
					Map.DeoccupyProvince(hoveredPid);
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

	private void _handle_click(Vector2 _screenPos, Drag aButton)
	{
		GetViewport().GuiReleaseFocus();

		if (hoveredPid <= 1)
			return;

		// Resource mode has no map interaction.
		if (currentTool == Tool.NONE && currentMode != Mode.RESOURCE)
		{
			SelectProvince(hoveredPid);
			return;
		}

		switch (currentMode)
		{
			case Mode.PROVINCE:
				if (currentTool == Tool.MULTI_SELECT)
					MultiSelectProvince(hoveredPid);
				break;
			case Mode.FACTION:
				if (currentTool == Tool.PAINT_PRIMARY)
					PaintFaction(aButton);
				break;
			case Mode.POLITY:
				switch (currentTool)
				{
					case Tool.PAINT_PRIMARY:
						PaintOccupation(aButton);
						break;
					case Tool.PAINT_SECONDARY:
						if (Prov(hoveredPid).country != "Sea" && Countries.countries.ContainsKey(selectedCountry))
						{
							ActionDispatcher.Instance.Submit(new TransferProvinceAction
							{
								Issuer = selectedCountry, ProvinceId = hoveredPid, NewOwner = selectedCountry,
							});
							originalPidColor = Countries.GetCountry(selectedCountry).countryColor;
						}
						break;
					case Tool.MULTI_SELECT:
						if (aButton == Drag.RIGHT)
							RemoveFromMultiSelect(hoveredPid);
						else if (aButton == Drag.LEFT)
							AddToMultiSelect(hoveredPid);
						break;
				}
				break;
			case Mode.BIOME:
				if (currentTool == Tool.PAINT_PRIMARY)
					PaintBiome(aButton);
				break;
		}
	}

	private void PaintFaction(Drag aButton)
	{
		var country = Prov(hoveredPid).country;
		if (country == "Sea" || !Factions.TryGetValue(selectedFaction, out var faction))
			return;

		switch (aButton)
		{
			case Drag.LEFT:
				switch (faction.GetMemberStatus(country))
				{
					case "Member":
						faction.SetMemberStatus(country, "Leader");
						break;
					case "":
						faction.SetMemberStatus(country, "Member");
						originalPidColor = faction.color.ToColor();
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
						originalPidColor = Colors.Gray;
						break;
				}
				break;
		}
	}

	public static int IndexOfText(ItemList aItemList, string aText)
	{
		for (var i = 0; i < aItemList.ItemCount; i++)
		{
			if (aItemList.GetItemText(i) == aText)
				return i;
		}
		return -1;
	}

	private void SetProvinceFields(Province prov)
	{
		provinceName.Editable = true;
		provinceName.Text = prov.name;
		inputCountry.Editable = true;
		inputCountry.Text = prov.country;
		inputOccupier.Editable = true;
		inputOccupier.Text = prov.occupier;
		inputCity.Editable = true;
		inputCity.Text = prov.city;
		inputGdp.Editable = true;
		inputGdp.Value = prov.gdp;
		inputClaims.Editable = true;
		inputClaims.Text = string.Join(", ", prov.claims);
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
		eth.TextSubmitted += aEthnicity => population.ethnicity = aEthnicity;
		var pop = entry.GetNode<Range>("MarginContainer/HBoxContainer/InputPop");
		pop.Value = population.amount;
		pop.ValueChanged += aAmount => population.amount = (int)aAmount;
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
		type.TextSubmitted += aType => resource.type = aType;
		var amount = entry.GetNode<Range>("MarginContainer/HBoxContainer/InputAmount");
		amount.Value = resource.amount;
		amount.ValueChanged += aAmount => resource.amount = (int)aAmount;
		var quality = entry.GetNode<Range>("MarginContainer/HBoxContainer/InputQuality");
		// The GDScript original showed resource.amount in the quality field.
		quality.Value = resource.quality;
		quality.ValueChanged += aQuality => resource.quality = (float)aQuality;
		provinceResourcesList.AddChild(entry);
	}

	private static void SelectAndEmit(ItemList list, string text)
	{
		var itemIndex = IndexOfText(list, text);
		list.Select(itemIndex);
		list.EmitSignal(ItemList.SignalName.ItemSelected, itemIndex);
	}

	public void SelectProvince(int pid)
	{
		selectedPid = pid;
		var prov = ProvOrNull(pid);
		if (prov == null)
			return;

		statusLabel.Text = $"Selected PID: {pid}";
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

		UpdateMultiSelectUi();
	}

	public void UpdateMultiSelectUi()
	{
		statusLabel.Text = $"Selected: {multiSelectPID.Count}";
		ClearProvinceLists();

		switch (multiSelectPID.Count)
		{
			case 0:
				provinceName.Editable = false;
				provinceName.Text = "";
				inputCountry.Editable = false;
				inputCountry.Text = "";
				inputOccupier.Editable = false;
				inputOccupier.Text = "";
				inputCity.Editable = false;
				inputCity.Text = "";
				inputGdp.Editable = false;
				inputGdp.Value = 0;
				inputClaims.Editable = false;
				inputClaims.Text = "";
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

				inputCountry.Editable = true;
				inputCountry.Text = holdCountry ? prov.country : "...";
				inputOccupier.Editable = true;
				inputOccupier.Text = holdOccupier ? prov.occupier : "...";
				inputCity.Editable = false;
				inputCity.Text = "...";
				inputGdp.Editable = true;
				inputGdp.Value = holdGDP ? prov.gdp : 0;
				inputClaims.Editable = true;
				inputClaims.Text = holdClaims ? string.Join(", ", prov.claims) : "...";
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

	public void MOnTabContainerTabChanged(int tab)
	{
		MNoneTool();
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

	public void SetupBiomesList() => FillSorted(biomesItemList, Map.biomes.Keys);

	public void SetupResourcesList() => FillSorted(resourcesItemList, Map.resources.Keys);

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
			status.ItemSelected += aIndex => Factions[selectedFaction].UpdateMemberStatus(member.polity, (int)aIndex);
		else
			status.ItemSelected += aIndex => member.status = FactionMember.GetString((int)aIndex);
		factionMemberList.AddChild(memberEntry);
	}

	public void MOnFactionSelected(int index)
	{
		selectedFaction = factionsItemList.GetItemText(index);
		var faction = Factions[selectedFaction];
		factionName.Text = faction.name;
		factionColor.Color = faction.color.ToColor();
		foreach (var child in factionMemberList.GetChildren())
			child.QueueFree();
		foreach (var member in faction.members)
			AddFactionMemberEntry(member, true);
	}

	public void MOnPolitySelected(int index)
	{
		selectedCountry = politiesItemList.GetItemText(index);
		var polity = Countries.GetCountry(selectedCountry);
		polityPuppet.SetPressedNoSignal(polity.isPuppet);
		polityOwner.Text = polity.owner;
		polityInExile.SetPressedNoSignal(polity.isExiled);
		polityHost.Text = polity.host;
		polityName.Text = polity.countryName;
		polityColor.Color = polity.countryColor;
		polityMoney.SetValueNoSignal(polity.money);
		polityPoliticalPower.SetValueNoSignal(polity.politicalPower);
		polityWarSupport.SetValueNoSignal(polity.warSupport);
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
			var member = Factions[faction].members.FirstOrDefault(m => m.polity == polity.countryName);
			if (member != null)
				factionEntry.GetNode<OptionButton>("HBoxContainer/OptionButton").Select(FactionMember.GetIndex(member.status));
			polityFactionList.AddChild(factionEntry);
		}
	}

	public void MOnBiomeSelected(int index)
	{
		selectedBiome = biomesItemList.GetItemText(index);
		var biome = Biome(selectedBiome);
		biomeName.Text = biome.name;
		biomeColor.Color = biome.color.ToColor();
	}

	public void MOnBiomeNameSubmitted(string newText)
	{
		var biomes = Map.biomes;
		if (!biomes.ContainsKey(selectedBiome) || biomes.ContainsKey(newText))
			return;
		var biome = Biome(selectedBiome);
		biomes[newText] = biome;
		biome.name = newText;
		biomes.Remove(selectedBiome);
		foreach (var province in Map.provinceObjects.Values)
		{
			if (province.biome == selectedBiome)
				province.biome = newText;
		}
		selectedBiome = newText;
	}

	public void MOnBiomeColorChanged(Color color)
	{
		if (Map.biomes.ContainsKey(selectedBiome))
			Biome(selectedBiome).color = color.ToHex();
	}

	public void MOnResourceSelected(int index)
	{
		selectedResource = resourcesItemList.GetItemText(index);
		var resource = Resource(selectedResource);
		resourceName.Text = resource.name;
		resourceColor.Color = resource.color.ToColor();
		resourceIcon.Text = resource.icon;
		resourceIconTexture.Texture = Map.GetResourceIcon(selectedResource);
	}

	public void MOnResourceNameSubmitted(string newText)
	{
		var resources = Map.resources;
		if (!resources.ContainsKey(selectedResource) || resources.ContainsKey(newText))
			return;
		var resource = Resource(selectedResource);
		resources[newText] = resource;
		resource.name = newText;
		resources.Remove(selectedResource);
		foreach (var province in Map.provinceObjects.Values)
		{
			foreach (var node in province.resources)
			{
				if (node.type == selectedResource)
					node.type = newText;
			}
		}
		selectedResource = newText;
	}

	public void MOnResourceColorChanged(Color color)
	{
		if (Map.resources.ContainsKey(selectedResource))
			Resource(selectedResource).color = color.ToHex();
	}

	public void MOnResourceIconSubmitted(string newText)
	{
		if (!Map.resources.ContainsKey(selectedResource))
			return;
		Resource(selectedResource).icon = newText;
		resourceIconTexture.Texture = Map.GetResourceIcon(selectedResource);
	}

	public void MOnCitySubmitted(string newText)
	{
		if (HasProv(selectedPid))
			Prov(selectedPid).city = newText;
	}

	// Pids the province fields apply to for the current tool.
	private IEnumerable<int> EditedPids()
	{
		switch (currentTool)
		{
			case Tool.NONE:
				if (HasProv(selectedPid))
					yield return selectedPid;
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

	public void MOnProvinceGDPChanged(float value)
	{
		foreach (var pid in EditedPids())
			Prov(pid).gdp = (int)value;
	}

	public void MOnColorChanged(Color color)
	{
		if (!Countries.countries.ContainsKey(selectedCountry))
			return;
		Countries.GetCountry(selectedCountry).countryColor = color;
		// The GDScript original called a nonexistent `setCountryColor`.
		Map.SetCountryColor(selectedCountry, color);
	}

	public void MOnFactionColorChanged(Color color)
	{
		if (Factions.TryGetValue(selectedFaction, out var faction))
			faction.color = color.ToHex();
	}

	public void MOnProvinceNameSubmitted(string newText)
	{
		if (HasProv(selectedPid))
			Prov(selectedPid).name = newText;
	}

	public void MOnProvinceClaimsSubmitted()
	{
		if (inputClaims.Text == "...")
			return;

		var finalClaims = inputClaims.Text.Split(",").Select(c => c.StripEdges()).ToArray();
		foreach (var pid in EditedPids())
			Prov(pid).claims = finalClaims;
	}

	public void MOnProvinceOwnerSubmitted(string newText)
	{
		if (!Countries.countries.ContainsKey(newText))
			return;
		foreach (var pid in EditedPids())
			ActionDispatcher.Instance.Submit(new TransferProvinceAction
			{
				Issuer = newText, ProvinceId = pid, NewOwner = newText,
			});
	}

	public void MOnProvinceOccupierSubmitted(string newText)
	{
		foreach (var pid in EditedPids())
		{
			if (string.IsNullOrEmpty(newText))
				Map.DeoccupyProvince(pid);
			else
				ActionDispatcher.Instance.Submit(new OccupyProvinceAction
				{
					Issuer = newText, ProvinceId = pid, Occupier = newText,
				});
		}
	}

	public void MOnPolityNameSubmitted(string newText)
	{
		if (!Countries.countries.ContainsKey(selectedCountry))
			return;
		Countries.RenameCountry(selectedCountry, newText);
		selectedCountry = newText;
	}

	public void MOnPolityOwnerSubmitted(string newText)
	{
		var polity = Countries.GetCountry(selectedCountry);
		if (polity == null)
			return;
		if (Countries.countries.ContainsKey(polity.owner))
			Countries.ReleasePuppet(Countries.GetCountry(polity.owner), polity);
		if (Countries.countries.ContainsKey(newText))
			Countries.MakePuppet(Countries.GetCountry(newText), polity);
	}

	public void MOnPolityMoneyChanged(float value)
	{
		if (Countries.GetCountry(selectedCountry) is { } polity)
			polity.money = value;
	}

	public void MOnPolityPoliticalPowerChanged(float value)
	{
		if (Countries.GetCountry(selectedCountry) is { } polity)
			polity.politicalPower = value;
	}

	public void MOnPolityWarSupportChanged(float value)
	{
		if (Countries.GetCountry(selectedCountry) is { } polity)
			polity.warSupport = value;
	}

	public void MOnPolityStabilityChanged(float value)
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

	public void MNoneTool()
	{
		EndMultiSelect();
		currentTool = Tool.NONE;
	}

	public void MPaintOwnerTool()
	{
		EndMultiSelect();
		Map.ShowCountriesMap();
		currentTool = Tool.PAINT_SECONDARY;
	}

	public void MPaintOccupationTool()
	{
		EndMultiSelect();
		Map.ShowCountriesMap();
		currentTool = Tool.PAINT_PRIMARY;
	}

	public void MPaintFactionTool()
	{
		EndMultiSelect();
		Map._ShowFactionMap();
		currentTool = Tool.PAINT_PRIMARY;
	}

	public void MPaintBiomeTool()
	{
		EndMultiSelect();
		Map._ShowBiomesMap();
		currentTool = Tool.PAINT_PRIMARY;
	}

	public void MMultiSelectTool()
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
		var prov = Prov(selectedPid);
		var population = PopulationData.FromDict(new Dictionary<string, object?>
		{
			{ "ethnicity", "Unknown" },
			{ "amount", 1 },
		});
		prov.populations.Add(population);
		AddPopulationEntry(prov, population);
	}

	public void AddProvinceResource()
	{
		var prov = Prov(selectedPid);
		var resource = ResourceNode.FromDict(new Dictionary<string, object?> { { "type", "Unknown" } });
		prov.resources.Add(resource);
		AddResourceEntry(prov, resource);
	}
}
