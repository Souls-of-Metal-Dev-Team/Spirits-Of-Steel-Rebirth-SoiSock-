using System.Collections.Generic;
using System.Globalization;
using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class TroopSelection : Node2D
{
	private const float FlagWidthBase = 24.0f;
	private const float FlagHeightBase = 20.0f;
	private const float PaddingBase = 6.0f;
	private const float GapBase = 8.0f;
	private const float ClickThreshold = 2.0f; // pixels - how far mouse can move and still count as a "click"
	private const float GhostMargin = 600.0f;

	// MusicManager.SFX.TROOP_SELECTED (GDScript enum, not visible from C#).
	private const int SfxTroopSelected = 1;

	public Font font = GD.Load<Font>("res://font/TTT-Regular.otf");

	// --- State (field names match the GDScript originals) ---
	public bool dragging = false;
	public Vector2 drag_start = Vector2.Zero;
	public Vector2 drag_end = Vector2.Zero;

	public bool right_dragging = false;
	public Godot.Collections.Array right_path = new Godot.Collections.Array();

	public Sprite2D map_sprite;

	// --- Path Length Limit ---
	public int max_path_length = 0;

	public Godot.Collections.Array<TroopData> selected_troops = new Godot.Collections.Array<TroopData>();

	[Signal]
	public delegate void SelectionChangedEventHandler(Godot.Collections.Array troops);

	public override void _Ready()
	{
		map_sprite = GetNode<Sprite2D>("../../../MapContainer/CultureSprite");

		// The generated C# signal name is "SelectionChanged". Keep the old camelCase
		// GDScript name alive until those call sites are patched centrally.
		if (!HasSignal("selectionChanged"))
		{
			AddUserSignal("selectionChanged", new Godot.Collections.Array
			{
				new Godot.Collections.Dictionary
				{
					{ "name", "troops" },
					{ "type", (int)Variant.Type.Array },
				},
			});
		}
	}

	private void EmitSelectionChanged()
	{
		var troops = (Godot.Collections.Array)selected_troops;
		EmitSignal(SignalName.SelectionChanged, troops);
		if (HasSignal("selectionChanged"))
			EmitSignal("selectionChanged", troops);
	}

	public override void _Input(InputEvent @event)
	{
		if (map_sprite == null || GameConsole.IsVisible())
			return;

		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
				_handle_left_mouse(mouseButton);
			else if (mouseButton.ButtonIndex == MouseButton.Right)
				_handle_right_mouse(mouseButton);
		}
		else if (@event is InputEventMouseMotion)
		{
			_handle_mouse_motion();
		}
	}

	public void deselect_all()
	{
		selected_troops.Clear();
		EmitSelectionChanged();
	}

	private void _handle_mouse_motion()
	{
		if (dragging)
		{
			drag_end = GetGlobalMousePosition();
			if (drag_start.DistanceTo(drag_end) >= ClickThreshold)
				_perform_selection();
		}

		if (right_dragging && drag_start.DistanceTo(GetGlobalMousePosition()) >= ClickThreshold)
			_sample_province_under_mouse();
	}

	private void _handle_left_mouse(InputEventMouseButton @event)
	{
		var interactionType = GameState.Instance.interactionType;
		if (interactionType == GameState.InteractionType.SELECT_COUNTRY
			|| (!dragging && GetViewport().GuiGetHoveredControl() != null))
			return;

		if (@event.Pressed)
		{
			dragging = true;
			drag_start = GetGlobalMousePosition();
			drag_end = drag_start;
		}
		else if (dragging)
		{
			drag_end = GetGlobalMousePosition();
			dragging = false;

			if (selected_troops.Count > 0)
				MusicManager.Instance.play_sfx(SfxTroopSelected);
			// Empty selection (clicked empty ground) hides the container.
			EmitSelectionChanged();
		}
	}

	private void _handle_right_mouse(InputEventMouseButton @event)
	{
		if (@event.Pressed && selected_troops.Count > 0)
		{
			right_dragging = true;
			drag_start = GetGlobalMousePosition();
			right_path.Clear();
			_sample_province_under_mouse();
		}
		else if (right_dragging)
		{
			_perform_path_assignment();
			right_path.Clear();
			right_dragging = false;
		}
	}

	private void _perform_selection()
	{
		if (map_sprite == null)
			return;

		var worldRect = new Rect2(drag_start, drag_end - drag_start).Abs();
		var textureWidth = map_sprite.Texture.GetWidth();
		var cam = GetViewport().GetCamera2D();
		var invZoom = cam != null ? 1.0f / cam.Zoom.X : 1.0f;

		var selectedList = new Godot.Collections.Array<TroopData>();
		var flagSize = new Vector2(FlagWidthBase, FlagHeightBase) * invZoom;
		var pad = PaddingBase * invZoom * 2f;
		var baseBoxSize = new Vector2(flagSize.X + (GapBase * invZoom) + pad, pad);

		var playerCountry = CountryManager.Instance.player_country;
		var playerCountryName = playerCountry == null ? "" : CountryData.NameOf(playerCountry);
		var fontSize = MapContainerFontSize();

		foreach (var troopValue in TroopManager.Instance.troops)
		{
			var t = troopValue.AsGodotObject() as TroopData;
			if (t == null || t.country_name != playerCountryName)
				continue;

			var textSize = font.GetStringSize(
				t.divisions_count.ToString(CultureInfo.InvariantCulture),
				HorizontalAlignment.Center, -1f, fontSize) * invZoom;

			var boxSize = baseBoxSize + new Vector2(textSize.X, Mathf.Max(flagSize.Y, textSize.Y));
			if (_check_rect_intersection(worldRect,
				new Rect2(t.position + map_sprite.Position - boxSize * 0.5f, boxSize),
				t.position.X, textureWidth))
				selectedList.Add(t);
		}

		// Apply selection.
		if (!Input.IsKeyPressed(Key.Shift))
			selected_troops.Clear();

		foreach (var t in selectedList)
		{
			if (!selected_troops.Contains(t))
				selected_troops.Add(t);
		}

		// Update max_path_length based on current live selection.
		max_path_length = 0;
		foreach (var troop in selectedList)
			max_path_length += troop.divisions_count;
	}

	private static int MapContainerFontSize()
	{
		return MapContainer.LAYOUT.ContainsKey("font_size")
			? MapContainer.LAYOUT["font_size"].AsInt32()
			: 16;
	}

	private bool _check_rect_intersection(Rect2 selectionRect, Rect2 troopRect, float tx, float texW)
	{
		// Standard check.
		if (selectionRect.Intersects(troopRect))
			return true;

		// Ghost check (wrapping).
		if (tx < GhostMargin)
		{
			var wrapped = troopRect;
			wrapped.Position = new Vector2(wrapped.Position.X + texW, wrapped.Position.Y);
			if (selectionRect.Intersects(wrapped))
				return true;
		}
		else if (tx > texW - GhostMargin)
		{
			var wrapped = troopRect;
			wrapped.Position = new Vector2(wrapped.Position.X - texW, wrapped.Position.Y);
			if (selectionRect.Intersects(wrapped))
				return true;
		}

		return false;
	}

	private void _sample_province_under_mouse()
	{
		if (map_sprite == null)
			return;

		// Stop sampling if we've reached max provinces.
		if (right_path.Count >= max_path_length)
			return;

		var pid = MapManager.Instance.GetProvinceAtPos(GetGlobalMousePosition(), map_sprite);

		// Check for military access.
		var provinceObjects = MapManager.Instance.province_objects;
		if (!provinceObjects.ContainsKey(pid))
			return;
		var prov = provinceObjects[pid].AsGodotObject() as Province;
		var player = CountryManager.Instance.player_country;
		if (prov == null || player == null)
			return;
		var allowed = player.Call("get_all_allowed_countries").AsGodotArray();
		if (!allowed.Contains(prov.GetFunctionalOwner()))
			return;

		// Don't add duplicate consecutive provinces.
		if (right_path.Count > 0 && right_path[right_path.Count - 1].AsGodotDictionary()["pid"].AsInt32() == pid)
			return;

		// Force the next province to be a neighbor of the previous one (no "jumping").
		if (right_path.Count > 0)
		{
			var lastPid = right_path[right_path.Count - 1].AsGodotDictionary()["pid"].AsInt32();
			var graph = MapManager.Instance.province_graph;
			if (graph == null || !graph.ArePointsConnected(lastPid, pid))
				return;
		}

		var centers = MapManager.Instance.province_centers;
		if (!centers.ContainsKey(pid))
			return;
		var centerTex = centers[pid];
		if (centerTex.VariantType == Variant.Type.Nil)
			return;

		right_path.Add(new Godot.Collections.Dictionary
		{
			{ "pid", pid },
			{ "map_pos", centerTex },
			{ "texture_pos", centerTex },
		});

		GD.Print($"Sampled province {pid}. Path length: {right_path.Count}/{max_path_length}");
	}

	private void _perform_path_assignment()
	{
		if (right_path.Count == 0 || selected_troops.Count == 0)
			return;

		var pathPids = new Godot.Collections.Array();
		foreach (var entryValue in right_path)
		{
			var entry = entryValue.AsGodotDictionary();
			var pid = entry["pid"].AsInt32();
			if (pathPids.Count == 0 || pathPids[pathPids.Count - 1].AsInt32() != pid)
				pathPids.Add(pid);
		}

		if (pathPids.Count == 0)
			return;

		// 1. Cast the moving pool correctly.
		var gameUi = GameState.Instance.game_ui;
		var uiSelected = gameUi != null && GodotObject.IsInstanceValid(gameUi)
			? gameUi.selected_division_objects
			: new Godot.Collections.Array<DivisionData>();

		var movingPool = new Godot.Collections.Array<DivisionData>();
		if (uiSelected.Count > 0)
		{
			foreach (var div in uiSelected)
				movingPool.Add(div);
		}
		else
		{
			foreach (var t in selected_troops)
			{
				foreach (var div in t.stored_divisions)
					movingPool.Add(div);
			}
		}

		// 2. Ensure the Dictionary values are typed arrays.
		var poolByOrigin = new Dictionary<int, Godot.Collections.Array<DivisionData>>();
		foreach (var div in movingPool)
		{
			var owningPolity = TroopManager.Instance.find_troop_owning_division(div) as TroopData;
			if (owningPolity == null)
				continue;

			var originId = owningPolity.province_id;
			if (!poolByOrigin.TryGetValue(originId, out var originBatch))
			{
				originBatch = new Godot.Collections.Array<DivisionData>();
				poolByOrigin[originId] = originBatch;
			}
			originBatch.Add(div);
		}

		var allAssignments = new Godot.Collections.Array();
		foreach (var pair in poolByOrigin)
		{
			var originId = pair.Key;
			var originBatch = pair.Value;

			TroopData template = null;
			foreach (var t in selected_troops)
			{
				if (t.province_id == originId)
				{
					template = t;
					break;
				}
			}

			if (template == null)
			{
				var troopsAtOrigin = TroopManager.Instance.get_troops_in_province(originId);
				if (troopsAtOrigin.Count > 0)
					template = troopsAtOrigin[0].AsGodotObject() as TroopData;
				if (template == null)
					continue;
			}

			// GDScript suppresses the integer-division warning here; C# int / int
			// already truncates the same way.
			var divsPerTarget = Mathf.Max(1, originBatch.Count / pathPids.Count);
			var remainder = originBatch.Count % pathPids.Count;
			var currentBatchIdx = 0;

			for (var provinceIdx = 0; provinceIdx < pathPids.Count; provinceIdx++)
			{
				var finalDivs = new Godot.Collections.Array<DivisionData>();
				var batchCount = divsPerTarget + (provinceIdx < remainder ? 1 : 0);
				for (var i = 0; i < batchCount; i++)
				{
					if (currentBatchIdx >= originBatch.Count)
						break;

					var div = originBatch[currentBatchIdx];
					_remove_division_from_current_owner(div);
					finalDivs.Add(div);
					currentBatchIdx++;
				}

				if (finalDivs.Count == 0)
					continue;

				var newTroop = TroopManager.Instance._create_new_split_troop(
					template, (Godot.Collections.Array)finalDivs) as TroopData;
				allAssignments.Add(new Godot.Collections.Dictionary
				{
					{ "troop", newTroop },
					{ "province_id", pathPids[provinceIdx] },
				});
			}
		}

		TroopManager.Instance.command_move_assigned(allAssignments);
		_cleanup_empty_troops();

		selected_troops.Clear();
		right_path.Clear();
		EmitSelectionChanged();
	}

	// --- Helper functions for the logic above ---

	private void _remove_division_from_current_owner(DivisionData div)
	{
		// Search all our selected troops and remove the div from their stored_divisions.
		foreach (var t in selected_troops)
		{
			if (t.stored_divisions.Contains(div))
			{
				t.stored_divisions.Remove(div);
				return;
			}
		}
	}

	private void _cleanup_empty_troops()
	{
		// If a troop gave away all its divisions, delete it from the world.
		foreach (var t in selected_troops)
		{
			if (t.stored_divisions.Count == 0)
				TroopManager.Instance.RemoveTroop(t);
		}
	}

	// --- Helpers to keep logic clean ---

	private TroopData _find_template_troop_for_divs(Godot.Collections.Array<DivisionData> batch)
	{
		// Find which troop currently owns the first division in this batch
		// to use as a template (country name, flag, etc).
		foreach (var t in selected_troops)
		{
			if (batch.Count > 0 && t.stored_divisions.Contains(batch[0]))
				return t;
		}
		return selected_troops[0];
	}

	// --- MODE A: Move only the units clicked in the UI ---
	private void _handle_selective_ui_move(Godot.Collections.Array divObjects, Godot.Collections.Array path)
	{
		// Group the divisions by their current "parent" troop so we can split them.
		var splitMap = new Dictionary<TroopData, Godot.Collections.Array<DivisionData>>();
		foreach (var divValue in divObjects)
		{
			if (divValue.AsGodotObject() is not DivisionData div)
				continue;
			var owningPolity = TroopManager.Instance.find_troop_owning_division(div) as TroopData;
			if (owningPolity == null)
				continue;

			if (!splitMap.TryGetValue(owningPolity, out var divsToMove))
			{
				divsToMove = new Godot.Collections.Array<DivisionData>();
				splitMap[owningPolity] = divsToMove;
			}
			divsToMove.Add(div);
		}

		foreach (var pair in splitMap)
		{
			var originalTroop = pair.Key;
			var divsToMove = pair.Value;

			// Remove these objects from the original stack.
			foreach (var d in divsToMove)
				originalTroop.stored_divisions.Remove(d);

			// Create the new troop instance for the split-off units.
			var newTroop = TroopManager.Instance._create_new_split_troop(
				originalTroop, (Godot.Collections.Array)divsToMove) as TroopData;
			if (newTroop == null)
				continue;
			newTroop.path = path.Duplicate();
			TroopManager.Instance._start_next_leg(newTroop);

			// If the original troop is now empty, delete it from the map.
			if (originalTroop.stored_divisions.Count == 0)
				TroopManager.Instance.RemoveTroop(originalTroop);
		}
	}

	// --- MODE B: Spread the whole selected army across the path ---
	private void _handle_frontline_spread(Godot.Collections.Array path)
	{
		// Collect ALL division objects from all selected troops.
		var allDivs = new Godot.Collections.Array<DivisionData>();
		foreach (var t in selected_troops)
		{
			foreach (var div in t.stored_divisions)
				allDivs.Add(div);
			// We will effectively "re-distribute" all these, so clear the originals.
			t.stored_divisions.Clear();
		}

		var divsPerProv = Mathf.Max(1, allDivs.Count / path.Count);
		var remainder = allDivs.Count % path.Count;

		var divIndex = 0;
		for (var i = 0; i < path.Count; i++)
		{
			var batch = new Godot.Collections.Array<DivisionData>();
			var batchCount = divsPerProv + (i < remainder ? 1 : 0);
			for (var j = 0; j < batchCount; j++)
			{
				if (divIndex >= allDivs.Count)
					break;
				batch.Add(allDivs[divIndex]);
				divIndex++;
			}

			if (batch.Count == 0)
				continue;

			var newTroop = TroopManager.Instance._create_new_split_troop(
				selected_troops[0], (Godot.Collections.Array)batch) as TroopData;
			if (newTroop == null)
				continue;
			newTroop.path = path.Slice(0, i + 1);
			TroopManager.Instance._start_next_leg(newTroop);
		}

		// Clean up the now-empty original troops.
		foreach (var t in selected_troops)
		{
			if (t.stored_divisions.Count == 0)
				TroopManager.Instance.RemoveTroop(t);
		}
	}

	private void _print_troop_details(TroopData troop)
	{
		GD.Print($"--- Selected Troop (Prov: {troop.province_id}) ---");
		foreach (var div in troop.stored_divisions)
		{
			var expLevel = "Green";
			if (div.experience > 0.7f)
				expLevel = "Veteran";
			else if (div.experience > 0.3f)
				expLevel = "Trained";

			GD.Print($" > {div.name} [{div.type.ToUpperInvariant()}] - HP: {(int)div.hp}% - Exp: {expLevel}");
		}
	}
}
