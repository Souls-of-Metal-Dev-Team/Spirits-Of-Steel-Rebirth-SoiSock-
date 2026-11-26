using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sosr;

public partial class CreationPopupUI : PopupPanel
{
	public enum CreationMode
	{
		CREATE_FACTION = 0,
		CREATE_POLITY = 1,
		CREATE_MEMBER = 2,
		CREATE_BIOME = 3,
		CREATE_RESOURCE = 4,
	}

	[Export] public MapEditorUI mapEditor;
	[Export] public TabContainer tabs;

	[Export] public LineEdit factionName;
	[Export] public ColorPickerButton factionColor;
	[Export] public OptionButton factionFounder;

	[Export] public LineEdit polityName;
	[Export] public OptionButton polityOwner;
	[Export] public ColorPickerButton polityColor;
	[Export] public SpinBox polityMoney;
	[Export] public SpinBox polityPoliticalPower;
	[Export] public SpinBox polityWarSupport;
	[Export] public SpinBox polityStability;
	[Export] public TabContainer polityType;
	[Export] public OptionButton polityHost;
	[Export] public TextEdit polityClaims;

	[Export] public OptionButton memberPolity;
	[Export] public OptionButton memberStatus;

	[Export] public LineEdit biomeName;
	[Export] public ColorPickerButton biomeColor;

	[Export] public LineEdit resourceName;
	[Export] public ColorPickerButton resourceColor;
	[Export] public LineEdit resourceIcon;

	public void MOpenScreen(CreationMode aMode)
	{
		var countries = CountryManager.Instance.countries;
		tabs.CurrentTab = (int)aMode;
		switch (aMode)
		{
			case CreationMode.CREATE_FACTION:
				factionFounder.Clear();
				foreach (var country in countries.Keys)
					factionFounder.AddItem(country.AsString());
				break;
			case CreationMode.CREATE_POLITY:
				polityOwner.Clear();
				polityOwner.AddItem("None");
				polityHost.Clear();
				foreach (var country in countries.Keys)
				{
					polityOwner.AddItem(country.AsString());
					polityHost.AddItem(country.AsString());
				}
				break;
			case CreationMode.CREATE_MEMBER:
			{
				memberPolity.Clear();
				var faction = FactionManager.Instance.factions[mapEditor.selectedFaction];
				foreach (var country in countries.Keys)
				{
					var name = country.AsString();
					if (!faction.members.Any(m => m.polity == name))
						memberPolity.AddItem(name);
				}
				break;
			}
		}
		Visible = true;
	}

	public void MCreateFaction()
	{
		if (FactionManager.Instance.factions.ContainsKey(factionName.Text))
			return;
		FactionManager.Instance.CreateFaction(
			factionFounder.GetItemText(factionFounder.Selected), factionName.Text, factionColor.Color);
		mapEditor.SetupFactionList();
		Visible = false;
	}

	public void MCreatePolity()
	{
		var cm = CountryManager.Instance;
		if (cm.countries.ContainsKey(polityName.Text))
			return;

		var countryData = new Godot.Collections.Dictionary
		{
			{ "name", polityName.Text },
			{ "color", polityColor.Color.ToHtml() },
			{ "money", polityMoney.Value },
			{ "political_power", polityPoliticalPower.Value },
			{ "stability", polityStability.Value },
			{ "war_support", polityWarSupport.Value },
		};
		switch (polityType.CurrentTab)
		{
			case 0:
				cm.AddCountry(countryData);
				cm.MakeHost(cm.GetCountry(polityHost.Text), cm.GetCountry(polityName.Text));
				break;
			case 1:
				if (mapEditor.currentTool != MapEditorUI.Tool.MULTI_SELECT || mapEditor.multiSelectPID.Count <= 0)
					return;
				MapManager.Instance.InstantiateCountryFromProvinces(countryData, mapEditor.multiSelectPID.ToArray());
				mapEditor.multiSelectPID.Clear();
				break;
			case 2:
			{
				var tempClaims = polityClaims.Text.Split(",", false).Select(e => e.ToInt()).ToArray();
				MapManager.Instance.InstantiateCountryFromProvinces(countryData, tempClaims);
				break;
			}
		}
		if (polityOwner.Text != "None")
			cm.MakePuppet(cm.GetCountry(polityOwner.Text), cm.GetCountry(polityName.Text));
		mapEditor.SetupPolitiesList();
		Visible = false;
	}

	public void MCreateMember()
	{
		FactionManager.Instance.factions[mapEditor.selectedFaction].members
			.Add(FactionMember.FromValues(memberPolity.Text, memberStatus.Text));
		mapEditor.AddedFactionMember();
		Visible = false;
	}

	public void MCreateBiome()
	{
		var biomes = MapManager.Instance.biomes;
		if (biomes.ContainsKey(biomeName.Text))
			return;
		biomes[biomeName.Text] = BiomeData.FromValues(biomeName.Text, biomeColor.Color.ToHex());
		mapEditor.SetupBiomesList();
		Visible = false;
	}

	public void MCreateResource()
	{
		var resources = MapManager.Instance.resources;
		if (resources.ContainsKey(resourceName.Text))
			return;
		resources[resourceName.Text] = ResourceData.FromValues(
			resourceName.Text, resourceColor.Color.ToHex(), resourceIcon.Text, new Dictionary<string, object?>());
		mapEditor.SetupResourcesList();
		Visible = false;
	}
}
