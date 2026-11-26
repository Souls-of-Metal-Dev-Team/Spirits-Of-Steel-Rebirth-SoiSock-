using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sosr;

public partial class PeaceProcessUI : CanvasLayer
{
	public enum MouseMode
	{
		ANNEX,
		PUPPET,
		ANNEX_NEW,
	}

	[Export] public PanelContainer sidebarPanel;
	[Export] public Label summaryLabel;
	[Export] public Label statsLabel;
	[Export] public Label loserLabel;

	[Export] public OptionButton newDropdown;
	[Export] public LineEdit newName;
	[Export] public OptionButton newOwner;
	[Export] public ColorPickerButton newColor;

	[Export] public Button subsumeButton;

	public MouseMode mouseMode = MouseMode.ANNEX;

	// Country dictionaries handed to MapManager.InstantiateCountryFromProvinces.
	private readonly List<Godot.Collections.Dictionary> creating = new();
	private readonly List<CountryData> winners = new();
	private int creatingSelected = 0;
	private CountryData currentSelected;
	private CountryData currentLoser;
	private readonly Dictionary<int, CountryData> provincesToTake = new();
	private readonly Dictionary<int, string> provincesCreating = new(); // PID -> name of the new country
	private readonly Dictionary<string, List<int>> creatingProvinces = new();
	private readonly Dictionary<string, CountryData> ownersCreating = new();
	private int hoveredPid = -1;
	private readonly Dictionary<CountryData, CountryData> puppeting = new();
	[Export] public OptionButton participantSelector;
	private List<int> provincesDiscussing = new();

	// Color Palette
	private static readonly Color COLOR_BG = new(0.1f, 0.1f, 0.12f, 0.98f);
	private static readonly Color COLOR_GOLD = new(0.85f, 0.65f, 0.2f);
	private static readonly Color COLOR_SELECT = new(0.0f, 1.0f, 0.8f); // Cyan/Teal for treaty selection
	private static readonly Color COLOR_DANGER = new(0.7f, 0.2f, 0.2f);

	public override void _Ready()
	{
		Hide();
		WarManager.Instance.PeaceConferenceRequested += (pWinners, pLoser) =>
			OpenMenu(pWinners.Select(w => w.As<CountryData>()).ToList(), (CountryData)pLoser);
	}

	private static Province Prov(int pid) => MapManager.Instance.provinceObjects[pid];

	private static Color CountryColor(string country) => CountryManager.Instance.GetCountryColor(country);

	private string SelectedName => creating[creatingSelected]["name"].AsString();

	private Color SelectedColor => Color.FromString(creating[creatingSelected]["color"].AsString(), Colors.DeepSkyBlue);

	private List<int> CreatingList(string name)
	{
		if (!creatingProvinces.TryGetValue(name, out var list))
		{
			list = new List<int>();
			creatingProvinces[name] = list;
		}
		return list;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible)
			return;

		// 1. Ignore input if clicking on the sidebar itself
		if (GetViewport().GetMousePosition().X < sidebarPanel.Size.X)
			return;

		// 2. Convert Screen position to Map/World position
		var world = GameState.Instance.currentWorld;
		if (world == null)
			return;

		var mapPos = world.GetGlobalMousePosition();

		// 3. Handle Hover (Motion) and Click (Button) separately
		if (@event is InputEventMouseMotion)
			_process_hover(mapPos);
		else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			_process_click(mapPos);
	}

	private void _process_hover(Vector2 mapPos)
	{
		var pid = GetProvinceWithRadius(mapPos, GameState.Instance.currentWorld.mapSprite, 5);
		if (pid == hoveredPid)
			return;

		// Reset the visual of the PREVIOUS hovered province
		if (hoveredPid > 1)
			_reset_province_visual(hoveredPid);

		hoveredPid = pid;

		// Apply NEW hover visual (if it's a valid land province belonging to the loser)
		if (hoveredPid > 1)
		{
			var objects = MapManager.Instance.provinceObjects;
			var province = objects.TryGetValue(hoveredPid, out var p) ? p : null;
			if (province != null && province.country == currentLoser.countryName)
			{
				_update_map_visual(hoveredPid, new Color(1.5f, 1.5f, 1.5f), CountryColor(province.country));
				Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
			}
			else
			{
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			}
		}
		else
		{
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
		}
	}

	private void ShadeUntakenForPuppet(CountryData puppet)
	{
		foreach (var province in provincesDiscussing)
		{
			if (provincesToTake.ContainsKey(province) || provincesCreating.ContainsKey(province))
				continue;
			var baseColor = CountryColor(Prov(province).country);
			_update_map_visual(province, baseColor * 0.8f + puppeting[puppet].countryColor * 0.2f, baseColor);
		}
	}

	private void _process_click(Vector2 mapPos)
	{
		var pid = GetProvinceWithRadius(mapPos, GameState.Instance.currentWorld.mapSprite, 5);
		if (pid <= 1)
			return;

		if (Input.IsKeyPressed(Key.Ctrl))
		{
			currentSelected = CountryManager.Instance.GetCountry(Prov(pid).country);
		}
		else
		{
			if (!provincesDiscussing.Contains(pid))
				return;

			switch (mouseMode)
			{
				case MouseMode.ANNEX:
					if (provincesCreating.TryGetValue(pid, out var creatingName))
					{
						creatingProvinces[creatingName].Remove(pid);
						provincesCreating.Remove(pid);
					}

					if (provincesToTake.TryGetValue(pid, out var taker) && taker == currentSelected)
					{
						provincesToTake.Remove(pid);
						_reset_province_visual(pid);
					}
					else
					{
						provincesToTake[pid] = currentSelected;
						_update_map_visual(pid, currentSelected.countryColor.Lightened(0.2f), CountryColor(Prov(pid).country));
					}
					break;
				case MouseMode.PUPPET:
				{
					var count = CountryManager.Instance.GetCountry(Prov(pid).country);
					if (count == currentSelected)
						break;
					if (puppeting.TryGetValue(count, out var puppeter) && puppeter == currentSelected)
					{
						puppeting.Remove(count);
						foreach (var province in provincesDiscussing)
						{
							if (!provincesToTake.ContainsKey(province) && !provincesCreating.ContainsKey(province))
								_reset_province_visual(province);
						}
					}
					else
					{
						puppeting[count] = currentSelected;
						ShadeUntakenForPuppet(count);
					}
					break;
				}
				case MouseMode.ANNEX_NEW:
				{
					provincesToTake.Remove(pid);

					var name = SelectedName;
					if (provincesCreating.TryGetValue(pid, out var existing) && existing == name)
					{
						// NOTE: the GDScript original re-added the pid to the list here instead of removing it.
						provincesCreating.Remove(pid);
						CreatingList(name).Remove(pid);
						_reset_province_visual(pid);
					}
					else
					{
						if (provincesCreating.TryGetValue(pid, out var previous))
							creatingProvinces[previous].Remove(pid);
						provincesCreating[pid] = name;
						CreatingList(name).Add(pid);
						_update_map_visual(pid, SelectedColor.Lightened(0.2f), CountryColor(Prov(pid).country));
					}
					break;
				}
			}
		}

		_update_summary();
	}

	public void MOnDropdownSelected(int aSelected)
	{
		if (aSelected == 0)
		{
			creatingSelected = newDropdown.ItemCount - 1;
			newDropdown.AddItem("...");
			newName.Text = "...";
			newOwner.Select(0);
			newColor.Color = new Color(GD.Randf(), GD.Randf(), GD.Randf());
			creating.Add(new Godot.Collections.Dictionary
			{
				{ "name", "..." },
				{ "color", "#" + newColor.Color.ToHtml(false).ToUpper() },
				{ "money", 0 },
				{ "ideology", new Godot.Collections.Array { 0, 0 } },
				{ "political_power", 100 },
				{ "stability", 0.5 },
				{ "war_support", 0.5 },
				{ "puppets", new Godot.Collections.Array() },
				{ "accepted_cultures", new Godot.Collections.Array() },
				{ "hostedGovernments", new Godot.Collections.Array() },
				{ "figures", new Godot.Collections.Array() },
			});
		}
		else
		{
			creatingSelected = aSelected - 1;
			newName.Text = SelectedName;
			if (!ownersCreating.TryGetValue(SelectedName, out var owner))
			{
				newOwner.Select(0);
			}
			else
			{
				for (var i = 0; i < newOwner.ItemCount; i++)
				{
					if (newOwner.GetItemText(i) == owner.countryName)
					{
						newOwner.Select(i);
						break;
					}
				}
			}
			newColor.Color = SelectedColor;
		}
	}

	public void MOnNameNewSubmitted(string aValue)
	{
		if (creatingSelected < 0)
			return;
		var oldName = SelectedName;
		if (creatingProvinces.TryGetValue(oldName, out var pids))
		{
			creatingProvinces[aValue] = new List<int>(pids);
			foreach (var pid in pids)
				provincesCreating[pid] = aValue;
		}
		if (oldName != aValue)
			creatingProvinces.Remove(oldName);
		newDropdown.SetItemText(creatingSelected + 1, aValue);
		if (ownersCreating.TryGetValue(oldName, out var owner))
		{
			ownersCreating.Remove(oldName);
			ownersCreating[aValue] = owner;
		}
		creating[creatingSelected]["name"] = aValue;
	}

	public void MOnColorNewSubmitted(Color aValue)
	{
		creating[creatingSelected]["color"] = "#" + aValue.ToHtml(false).ToUpper();
		if (!creatingProvinces.TryGetValue(SelectedName, out var pids))
			return;
		foreach (var pid in pids)
			_update_map_visual(pid, aValue.Lightened(0.2f), CountryColor(Prov(pid).country));
	}

	public void MOnOwnerNewSelected(int aIndex)
	{
		if (aIndex == 0)
			ownersCreating.Remove(SelectedName);
		else
			// The last entry is the loser; the GDScript compared against item_count, which never matched.
			ownersCreating[SelectedName] = aIndex == newOwner.ItemCount - 1 ? currentLoser : winners[aIndex - 1];
	}

	public void MOnAnnexNewPressed()
	{
		mouseMode = MouseMode.ANNEX_NEW;
		subsumeButton.Disabled = false;
	}

	public void MOnDeleteNewPressed()
	{
		if (creatingSelected < 0)
			return;
		if (creatingProvinces.TryGetValue(SelectedName, out var pids))
		{
			foreach (var pid in pids)
				provincesCreating.Remove(pid);
		}
		creatingProvinces.Remove(SelectedName);
		newDropdown.RemoveItem(creatingSelected + 1);

		creating.RemoveAt(creatingSelected);
		creatingSelected = -1;
	}

	public void _on_annex_all_pressed()
	{
		mouseMode = MouseMode.ANNEX;
		subsumeButton.Disabled = false;
	}

	public void _on_winner_selected(int index)
	{
		currentSelected = index < winners.Count ? winners[index] : currentLoser;
		mouseMode = MouseMode.ANNEX;
	}

	public void _on_puppet_pressed()
	{
		mouseMode = MouseMode.PUPPET;
		subsumeButton.Disabled = true;
	}

	public void _on_clear_selection_pressed()
	{
		var pids = provincesToTake.Keys.ToList();
		provincesToTake.Clear();
		foreach (var pid in pids)
			_reset_province_visual_immediate(pid);
		_update_summary();
	}

	private void _reset_province_visual_immediate(int pid)
	{
		var prov = Prov(pid);
		var cm = CountryManager.Instance;
		_update_map_visual(pid, cm.GetCountryColor(prov.GetFunctionalOwner(), Colors.White), cm.GetCountryColor(prov.country, Colors.White));
	}

	public void MAnnexAll()
	{
		switch (mouseMode)
		{
			case MouseMode.ANNEX:
				foreach (var province in provincesDiscussing)
				{
					if (provincesToTake.ContainsKey(province) || provincesCreating.ContainsKey(province))
						continue;
					provincesToTake[province] = currentSelected;
					_update_map_visual(province, currentSelected.countryColor.Lightened(0.2f), CountryColor(Prov(province).country));
				}
				break;
			case MouseMode.ANNEX_NEW:
				foreach (var province in provincesDiscussing)
				{
					if (provincesToTake.ContainsKey(province) || provincesCreating.ContainsKey(province))
						continue;
					provincesCreating[province] = SelectedName;
					CreatingList(SelectedName).Add(province);
					_update_map_visual(province, SelectedColor.Lightened(0.2f), CountryColor(Prov(province).country));
				}
				break;
		}

		_update_summary();
	}

	// --- Logic & Integration ---

	public void OpenMenu(List<CountryData> pWinners, CountryData pLoser)
	{
		mouseMode = MouseMode.ANNEX;
		subsumeButton.Disabled = false;
		Show();
		winners.Clear();
		winners.AddRange(pWinners);
		currentLoser = pLoser;
		provincesToTake.Clear();
		provincesDiscussing = MapManager.Instance.countryToOwnedProvinces[pLoser.countryName].ToList();
		puppeting.Clear();
		creating.Clear();
		creatingProvinces.Clear();
		provincesCreating.Clear();
		creatingSelected = -1;
		ownersCreating.Clear();

		newName.Text = "";

		// Setup Winner Selector
		newDropdown.Clear();
		newDropdown.AddItem("New");
		newOwner.Clear();
		newOwner.AddItem("None");
		participantSelector.Clear();
		var playerIndex = -1;
		for (var i = 0; i < winners.Count; i++)
		{
			newOwner.AddItem(winners[i].countryName);
			participantSelector.AddItem(winners[i].countryName);
			if (winners[i].isPlayer)
				playerIndex = i;
		}
		newOwner.AddItem(pLoser.countryName);
		participantSelector.AddItem(pLoser.countryName);

		if (playerIndex != -1)
		{
			participantSelector.Selected = playerIndex;
			currentSelected = winners[playerIndex];
		}
		else
		{
			participantSelector.Selected = 0;
			currentSelected = winners[0];
		}

		SetGameUiVisible(false);
		GameState.Instance.currentWorld.clock.pause();
		loserLabel.Text = $"Negotations: {pLoser.countryName}";
		GameState.Instance.inPeaceProcess = true;
		_update_summary();
	}

	private void SetGameUiVisible(bool aVisible)
	{
		if (GetTree().Root.FindChild("ui_game", true, false) is CanvasLayer gameUi)
			gameUi.Visible = aVisible;
	}

	private void _update_summary()
	{
		summaryLabel.Text = $"Provinces Selected: {provincesToTake.Count}";

		// Calculate percentage for flavor
		var totalLoserProvinces = 0;
		foreach (var p in MapManager.Instance.provinceObjects.Values)
		{
			if (p.country == currentLoser.countryName)
				totalLoserProvinces++;
		}

		if (totalLoserProvinces > 0)
			statsLabel.Text = $"Total Country Loss: {(int)((float)provincesToTake.Count / totalLoserProvinces * 100)}%";
	}

	public void _on_confirm_pressed()
	{
		var map = MapManager.Instance;
		var cm = CountryManager.Instance;
		foreach (var (pid, recipient) in provincesToTake)
			ActionDispatcher.Instance.Submit(new TransferProvinceAction
			{
				Issuer = recipient.countryName, ProvinceId = pid, NewOwner = recipient.countryName,
			});

		foreach (var (puppet, puppeter) in puppeting)
			cm.MakePuppet(puppeter, puppet);

		foreach (var country in creating)
		{
			var name = country["name"].AsString();
			if (creatingProvinces.TryGetValue(name, out var pids))
				map.InstantiateCountryFromProvinces(country, pids.ToArray());
		}

		foreach (var (puppet, owner) in ownersCreating)
		{
			if (cm.countries.ContainsKey(puppet))
				cm.MakePuppet(owner, cm.GetCountry(puppet));
		}

		SetGameUiVisible(true);
		GameState.Instance.inPeaceProcess = false;
		GameState.Instance.currentWorld.clock.resume();

		Hide();
	}

	public void ForceClose()
	{
		SetProcess(false);
		SetPhysicsProcess(false);
		Hide();

		// Clear selections & state
		provincesToTake.Clear();
		winners.Clear();
		currentLoser = null;
		puppeting.Clear();
		hoveredPid = -1;

		// Reset global flags
		GameState.Instance.inPeaceProcess = false;

		// Restore standard UI if possible
		SetGameUiVisible(true);

		// Essential to resume clock if we were paused by the peace menu
		GameState.Instance.currentWorld?.clock?.resume();
	}

	public int GetProvinceWithRadius(Vector2 globalPos, Sprite2D mapSprite, int radius)
		=> MapManager.Instance.GetProvinceWithRadius(globalPos, mapSprite, radius);

	private static void _update_map_visual(int pid, Color color, Color secondaryColor)
	{
		// We call MapManager's lookup update to refresh the shader texture
		MapManager.Instance.SetProvinceColors(pid, color, secondaryColor);
	}

	private void _reset_province_visual(int pid)
	{
		if (provincesToTake.TryGetValue(pid, out var taker))
		{
			_update_map_visual(pid, taker.countryColor.Lightened(0.2f), CountryColor(Prov(pid).country));
		}
		else if (provincesCreating.TryGetValue(pid, out var name))
		{
			var country = creating.First(c => c["name"].AsString() == name);
			var color = Color.FromString(country["color"].AsString(), Colors.DeepSkyBlue);
			_update_map_visual(pid, color.Lightened(0.2f), CountryColor(Prov(pid).country));
		}
		else
		{
			// Otherwise, revert to the original country color (legal owner)
			var objects = MapManager.Instance.provinceObjects;
			var province = objects.TryGetValue(pid, out var p) ? p : null;
			if (province != null && province.country != "Sea")
				_update_map_visual(pid, CountryColor(province.GetFunctionalOwner()), CountryColor(province.country));
		}
	}

	public void MOnLineEditFocusExited()
	{
		MOnNameNewSubmitted(newName.Text);
	}
}
