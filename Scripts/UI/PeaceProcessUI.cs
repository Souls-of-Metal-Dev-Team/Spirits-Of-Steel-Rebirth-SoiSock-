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

	[Export] public PanelContainer sidebar_panel;
	[Export] public Label summary_label;
	[Export] public Label stats_label;
	[Export] public Label loser_label;

	[Export] public OptionButton newDropdown;
	[Export] public LineEdit newName;
	[Export] public OptionButton newOwner;
	[Export] public ColorPickerButton newColor;

	[Export] public Button subsumeButton;

	public MouseMode mouseMode = MouseMode.ANNEX;

	// Country dictionaries handed to MapManager.InstantiateCountryFromProvinces.
	private readonly List<Godot.Collections.Dictionary> creating = new();
	private readonly List<CountryData> winners = new();
	private int creating_selected = 0;
	private CountryData current_selected;
	private CountryData current_loser;
	private readonly Dictionary<int, CountryData> provinces_to_take = new();
	private readonly Dictionary<int, string> provincesCreating = new(); // PID -> name of the new country
	private readonly Dictionary<string, List<int>> creatingProvinces = new();
	private readonly Dictionary<string, CountryData> ownersCreating = new();
	private int hovered_pid = -1;
	private readonly Dictionary<CountryData, CountryData> puppeting = new();
	[Export] public OptionButton participant_selector;
	private List<int> provinces_discussing = new();

	// Color Palette
	private static readonly Color COLOR_BG = new(0.1f, 0.1f, 0.12f, 0.98f);
	private static readonly Color COLOR_GOLD = new(0.85f, 0.65f, 0.2f);
	private static readonly Color COLOR_SELECT = new(0.0f, 1.0f, 0.8f); // Cyan/Teal for treaty selection
	private static readonly Color COLOR_DANGER = new(0.7f, 0.2f, 0.2f);

	public override void _Ready()
	{
		Hide();
		WarManager.Instance.PeaceConferenceRequested += (p_winners, p_loser) =>
			open_menu(p_winners.Select(w => w.As<CountryData>()).ToList(), (CountryData)p_loser);
	}

	private static Province Prov(int pid) => MapManager.Instance.province_objects[pid].As<Province>();

	private static Color CountryColor(string country) => CountryManager.Instance.GetCountryColor(country);

	private string SelectedName => creating[creating_selected]["name"].AsString();

	private Color SelectedColor => Color.FromString(creating[creating_selected]["color"].AsString(), Colors.DeepSkyBlue);

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
		if (GetViewport().GetMousePosition().X < sidebar_panel.Size.X)
			return;

		// 2. Convert Screen position to Map/World position
		var world = GameState.Instance.current_world;
		if (world == null)
			return;

		var map_pos = world.GetGlobalMousePosition();

		// 3. Handle Hover (Motion) and Click (Button) separately
		if (@event is InputEventMouseMotion)
			_process_hover(map_pos);
		else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			_process_click(map_pos);
	}

	private void _process_hover(Vector2 map_pos)
	{
		var pid = get_province_with_radius(map_pos, GameState.Instance.current_world.map_sprite, 5);
		if (pid == hovered_pid)
			return;

		// Reset the visual of the PREVIOUS hovered province
		if (hovered_pid > 1)
			_reset_province_visual(hovered_pid);

		hovered_pid = pid;

		// Apply NEW hover visual (if it's a valid land province belonging to the loser)
		if (hovered_pid > 1)
		{
			var objects = MapManager.Instance.province_objects;
			var province = objects.TryGetValue(hovered_pid, out var p) ? p.As<Province>() : null;
			if (province != null && province.country == current_loser.country_name)
			{
				_update_map_visual(hovered_pid, new Color(1.5f, 1.5f, 1.5f), CountryColor(province.country));
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
		foreach (var province in provinces_discussing)
		{
			if (provinces_to_take.ContainsKey(province) || provincesCreating.ContainsKey(province))
				continue;
			var baseColor = CountryColor(Prov(province).country);
			_update_map_visual(province, baseColor * 0.8f + puppeting[puppet].country_color * 0.2f, baseColor);
		}
	}

	private void _process_click(Vector2 map_pos)
	{
		var pid = get_province_with_radius(map_pos, GameState.Instance.current_world.map_sprite, 5);
		if (pid <= 1)
			return;

		if (Input.IsKeyPressed(Key.Ctrl))
		{
			current_selected = CountryManager.Instance.GetCountry(Prov(pid).country);
		}
		else
		{
			if (!provinces_discussing.Contains(pid))
				return;

			switch (mouseMode)
			{
				case MouseMode.ANNEX:
					if (provincesCreating.TryGetValue(pid, out var creatingName))
					{
						creatingProvinces[creatingName].Remove(pid);
						provincesCreating.Remove(pid);
					}

					if (provinces_to_take.TryGetValue(pid, out var taker) && taker == current_selected)
					{
						provinces_to_take.Remove(pid);
						_reset_province_visual(pid);
					}
					else
					{
						provinces_to_take[pid] = current_selected;
						_update_map_visual(pid, current_selected.country_color.Lightened(0.2f), CountryColor(Prov(pid).country));
					}
					break;
				case MouseMode.PUPPET:
				{
					var count = CountryManager.Instance.GetCountry(Prov(pid).country);
					if (count == current_selected)
						break;
					if (puppeting.TryGetValue(count, out var puppeter) && puppeter == current_selected)
					{
						puppeting.Remove(count);
						foreach (var province in provinces_discussing)
						{
							if (!provinces_to_take.ContainsKey(province) && !provincesCreating.ContainsKey(province))
								_reset_province_visual(province);
						}
					}
					else
					{
						puppeting[count] = current_selected;
						ShadeUntakenForPuppet(count);
					}
					break;
				}
				case MouseMode.ANNEX_NEW:
				{
					provinces_to_take.Remove(pid);

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

	public void m_OnDropdownSelected(int a_selected)
	{
		if (a_selected == 0)
		{
			creating_selected = newDropdown.ItemCount - 1;
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
			creating_selected = a_selected - 1;
			newName.Text = SelectedName;
			if (!ownersCreating.TryGetValue(SelectedName, out var owner))
			{
				newOwner.Select(0);
			}
			else
			{
				for (var i = 0; i < newOwner.ItemCount; i++)
				{
					if (newOwner.GetItemText(i) == owner.country_name)
					{
						newOwner.Select(i);
						break;
					}
				}
			}
			newColor.Color = SelectedColor;
		}
	}

	public void m_OnNameNewSubmitted(string a_value)
	{
		if (creating_selected < 0)
			return;
		var oldName = SelectedName;
		if (creatingProvinces.TryGetValue(oldName, out var pids))
		{
			creatingProvinces[a_value] = new List<int>(pids);
			foreach (var pid in pids)
				provincesCreating[pid] = a_value;
		}
		if (oldName != a_value)
			creatingProvinces.Remove(oldName);
		newDropdown.SetItemText(creating_selected + 1, a_value);
		if (ownersCreating.TryGetValue(oldName, out var owner))
		{
			ownersCreating.Remove(oldName);
			ownersCreating[a_value] = owner;
		}
		creating[creating_selected]["name"] = a_value;
	}

	public void m_OnColorNewSubmitted(Color a_value)
	{
		creating[creating_selected]["color"] = "#" + a_value.ToHtml(false).ToUpper();
		if (!creatingProvinces.TryGetValue(SelectedName, out var pids))
			return;
		foreach (var pid in pids)
			_update_map_visual(pid, a_value.Lightened(0.2f), CountryColor(Prov(pid).country));
	}

	public void m_OnOwnerNewSelected(int a_index)
	{
		if (a_index == 0)
			ownersCreating.Remove(SelectedName);
		else
			// The last entry is the loser; the GDScript compared against item_count, which never matched.
			ownersCreating[SelectedName] = a_index == newOwner.ItemCount - 1 ? current_loser : winners[a_index - 1];
	}

	public void m_OnAnnexNewPressed()
	{
		mouseMode = MouseMode.ANNEX_NEW;
		subsumeButton.Disabled = false;
	}

	public void m_OnDeleteNewPressed()
	{
		if (creating_selected < 0)
			return;
		if (creatingProvinces.TryGetValue(SelectedName, out var pids))
		{
			foreach (var pid in pids)
				provincesCreating.Remove(pid);
		}
		creatingProvinces.Remove(SelectedName);
		newDropdown.RemoveItem(creating_selected + 1);

		creating.RemoveAt(creating_selected);
		creating_selected = -1;
	}

	public void _on_annex_all_pressed()
	{
		mouseMode = MouseMode.ANNEX;
		subsumeButton.Disabled = false;
	}

	public void _on_winner_selected(int index)
	{
		current_selected = index < winners.Count ? winners[index] : current_loser;
		mouseMode = MouseMode.ANNEX;
	}

	public void _on_puppet_pressed()
	{
		mouseMode = MouseMode.PUPPET;
		subsumeButton.Disabled = true;
	}

	public void _on_clear_selection_pressed()
	{
		var pids = provinces_to_take.Keys.ToList();
		provinces_to_take.Clear();
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

	public void m_AnnexAll()
	{
		switch (mouseMode)
		{
			case MouseMode.ANNEX:
				foreach (var province in provinces_discussing)
				{
					if (provinces_to_take.ContainsKey(province) || provincesCreating.ContainsKey(province))
						continue;
					provinces_to_take[province] = current_selected;
					_update_map_visual(province, current_selected.country_color.Lightened(0.2f), CountryColor(Prov(province).country));
				}
				break;
			case MouseMode.ANNEX_NEW:
				foreach (var province in provinces_discussing)
				{
					if (provinces_to_take.ContainsKey(province) || provincesCreating.ContainsKey(province))
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

	public void open_menu(List<CountryData> p_winners, CountryData p_loser)
	{
		mouseMode = MouseMode.ANNEX;
		subsumeButton.Disabled = false;
		Show();
		winners.Clear();
		winners.AddRange(p_winners);
		current_loser = p_loser;
		provinces_to_take.Clear();
		provinces_discussing = MapManager.Instance.country_to_owned_provinces[p_loser.country_name]
			.AsGodotArray().Select(v => v.AsInt32()).ToList();
		puppeting.Clear();
		creating.Clear();
		creatingProvinces.Clear();
		provincesCreating.Clear();
		creating_selected = -1;
		ownersCreating.Clear();

		newName.Text = "";

		// Setup Winner Selector
		newDropdown.Clear();
		newDropdown.AddItem("New");
		newOwner.Clear();
		newOwner.AddItem("None");
		participant_selector.Clear();
		var player_index = -1;
		for (var i = 0; i < winners.Count; i++)
		{
			newOwner.AddItem(winners[i].country_name);
			participant_selector.AddItem(winners[i].country_name);
			if (winners[i].is_player)
				player_index = i;
		}
		newOwner.AddItem(p_loser.country_name);
		participant_selector.AddItem(p_loser.country_name);

		if (player_index != -1)
		{
			participant_selector.Selected = player_index;
			current_selected = winners[player_index];
		}
		else
		{
			participant_selector.Selected = 0;
			current_selected = winners[0];
		}

		SetGameUiVisible(false);
		GameState.Instance.current_world.clock.pause();
		loser_label.Text = $"Negotations: {p_loser.country_name}";
		GameState.Instance.in_peace_process = true;
		_update_summary();
	}

	private void SetGameUiVisible(bool a_visible)
	{
		if (GetTree().Root.FindChild("ui_game", true, false) is CanvasLayer game_ui)
			game_ui.Visible = a_visible;
	}

	private void _update_summary()
	{
		summary_label.Text = $"Provinces Selected: {provinces_to_take.Count}";

		// Calculate percentage for flavor
		var total_loser_provinces = 0;
		foreach (var p in MapManager.Instance.province_objects.Values)
		{
			if (p.As<Province>().country == current_loser.country_name)
				total_loser_provinces++;
		}

		if (total_loser_provinces > 0)
			stats_label.Text = $"Total Country Loss: {(int)((float)provinces_to_take.Count / total_loser_provinces * 100)}%";
	}

	public void _on_confirm_pressed()
	{
		var map = MapManager.Instance;
		var cm = CountryManager.Instance;
		foreach (var (pid, recipient) in provinces_to_take)
			map.TransferOwnership(pid, recipient.country_name);

		foreach (var (puppet, puppeter) in puppeting)
			cm.make_puppet(puppeter, puppet);

		foreach (var country in creating)
		{
			var name = country["name"].AsString();
			if (creatingProvinces.TryGetValue(name, out var pids))
				map.InstantiateCountryFromProvinces(country, pids.ToArray());
		}

		foreach (var (puppet, owner) in ownersCreating)
		{
			if (cm.countries.ContainsKey(puppet))
				cm.make_puppet(owner, cm.GetCountry(puppet));
		}

		SetGameUiVisible(true);
		GameState.Instance.in_peace_process = false;
		GameState.Instance.current_world.clock.resume();

		Hide();
	}

	public void force_close()
	{
		SetProcess(false);
		SetPhysicsProcess(false);
		Hide();

		// Clear selections & state
		provinces_to_take.Clear();
		winners.Clear();
		current_loser = null;
		puppeting.Clear();
		hovered_pid = -1;

		// Reset global flags
		GameState.Instance.in_peace_process = false;

		// Restore standard UI if possible
		SetGameUiVisible(true);

		// Essential to resume clock if we were paused by the peace menu
		GameState.Instance.current_world?.clock?.resume();
	}

	public int get_province_with_radius(Vector2 global_pos, Sprite2D map_sprite, int radius)
		=> MapManager.Instance.GetProvinceWithRadius(global_pos, map_sprite, radius);

	private static void _update_map_visual(int pid, Color color, Color secondary_color)
	{
		// We call MapManager's lookup update to refresh the shader texture
		MapManager.Instance.SetProvinceColors(pid, color, secondary_color);
	}

	private void _reset_province_visual(int pid)
	{
		if (provinces_to_take.TryGetValue(pid, out var taker))
		{
			_update_map_visual(pid, taker.country_color.Lightened(0.2f), CountryColor(Prov(pid).country));
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
			var objects = MapManager.Instance.province_objects;
			var province = objects.TryGetValue(pid, out var p) ? p.As<Province>() : null;
			if (province != null && province.country != "Sea")
				_update_map_visual(pid, CountryColor(province.GetFunctionalOwner()), CountryColor(province.country));
		}
	}

	public void m_OnLineEditFocusExited()
	{
		m_OnNameNewSubmitted(newName.Text);
	}
}
