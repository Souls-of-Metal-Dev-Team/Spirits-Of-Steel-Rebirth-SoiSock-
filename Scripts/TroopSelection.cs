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
	public Vector2 dragStart = Vector2.Zero;
	public Vector2 dragEnd = Vector2.Zero;

	public bool rightDragging = false;
	public Godot.Collections.Array rightPath = new Godot.Collections.Array();

	public Sprite2D mapSprite;

	// --- Path Length Limit ---
	public int maxPathLength = 0;

	public List<TroopData> selectedTroops = new();

	private string CurrentIssuer() => selectedTroops.Count > 0
		? selectedTroops[0].countryName
		: CountryData.NameOf(CountryManager.Instance.playerCountry);

	[Signal]
	public delegate void SelectionChangedEventHandler();

	public override void _Ready()
	{
		mapSprite = GetNode<Sprite2D>("../../../MapContainer/CultureSprite");
	}

	private void EmitSelectionChanged()
	{
		EmitSignal(SignalName.SelectionChanged);
	}

	public override void _Input(InputEvent @event)
	{
		if (mapSprite == null || GameConsole.IsVisible())
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

	public void DeselectAll()
	{
		selectedTroops.Clear();
		EmitSelectionChanged();
	}

	private void _handle_mouse_motion()
	{
		if (dragging)
		{
			dragEnd = GetGlobalMousePosition();
			if (dragStart.DistanceTo(dragEnd) >= ClickThreshold)
				_perform_selection();
		}

		if (rightDragging && dragStart.DistanceTo(GetGlobalMousePosition()) >= ClickThreshold)
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
			dragStart = GetGlobalMousePosition();
			dragEnd = dragStart;
		}
		else if (dragging)
		{
			dragEnd = GetGlobalMousePosition();
			dragging = false;

			if (selectedTroops.Count > 0)
				MusicManager.Instance.PlaySfx(SfxTroopSelected);
			// Empty selection (clicked empty ground) hides the container.
			EmitSelectionChanged();
		}
	}

	private void _handle_right_mouse(InputEventMouseButton @event)
	{
		if (@event.Pressed && selectedTroops.Count > 0)
		{
			rightDragging = true;
			dragStart = GetGlobalMousePosition();
			rightPath.Clear();
			_sample_province_under_mouse();
		}
		else if (rightDragging)
		{
			_perform_path_assignment();
			rightPath.Clear();
			rightDragging = false;
		}
	}

	private void _perform_selection()
	{
		if (mapSprite == null)
			return;

		var worldRect = new Rect2(dragStart, dragEnd - dragStart).Abs();
		var textureWidth = mapSprite.Texture.GetWidth();
		var cam = GetViewport().GetCamera2D();
		var invZoom = cam != null ? 1.0f / cam.Zoom.X : 1.0f;

		var selectedList = new List<TroopData>();
		var flagSize = new Vector2(FlagWidthBase, FlagHeightBase) * invZoom;
		var pad = PaddingBase * invZoom * 2f;
		var baseBoxSize = new Vector2(flagSize.X + (GapBase * invZoom) + pad, pad);

		var playerCountry = CountryManager.Instance.playerCountry;
		var playerCountryName = playerCountry == null ? "" : CountryData.NameOf(playerCountry);
		var fontSize = MapContainerFontSize();

		foreach (var troopValue in TroopManager.Instance.troops)
		{
			var t = troopValue;
			if (t == null || t.countryName != playerCountryName)
				continue;

			var textSize = font.GetStringSize(
				t.divisionsCount.ToString(CultureInfo.InvariantCulture),
				HorizontalAlignment.Center, -1f, fontSize) * invZoom;

			var boxSize = baseBoxSize + new Vector2(textSize.X, Mathf.Max(flagSize.Y, textSize.Y));
			if (_check_rect_intersection(worldRect,
				new Rect2(t.position.ToGodot() + mapSprite.Position - boxSize * 0.5f, boxSize),
				t.position.X, textureWidth))
				selectedList.Add(t);
		}

		// Apply selection.
		if (!Input.IsKeyPressed(Key.Shift))
			selectedTroops.Clear();

		foreach (var t in selectedList)
		{
			if (!selectedTroops.Contains(t))
				selectedTroops.Add(t);
		}

		// Update max_path_length based on current live selection.
		maxPathLength = 0;
		foreach (var troop in selectedList)
			maxPathLength += troop.divisionsCount;
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
		if (mapSprite == null)
			return;

		// Stop sampling if we've reached max provinces.
		if (rightPath.Count >= maxPathLength)
			return;

		var pid = MapManager.Instance.GetProvinceAtPos(GetGlobalMousePosition(), mapSprite);

		// Check for military access.
		var provinceObjects = MapManager.Instance.provinceObjects;
		if (!provinceObjects.TryGetValue(pid, out var prov))
			return;
		var player = CountryManager.Instance.playerCountry;
		if (prov == null || player == null)
			return;
		var allowed = player.Call("GetAllAllowedCountries").AsGodotArray();
		if (!allowed.Contains(prov.GetFunctionalOwner()))
			return;

		// Don't add duplicate consecutive provinces.
		if (rightPath.Count > 0 && rightPath[rightPath.Count - 1].AsGodotDictionary()["pid"].AsInt32() == pid)
			return;

		// Force the next province to be a neighbor of the previous one (no "jumping").
		if (rightPath.Count > 0)
		{
			var lastPid = rightPath[rightPath.Count - 1].AsGodotDictionary()["pid"].AsInt32();
			var graph = MapManager.Instance.provinceGraph;
			if (graph == null || !graph.ArePointsConnected(lastPid, pid))
				return;
		}

		var centers = MapManager.Instance.provinceCenters;
		if (!centers.TryGetValue(pid, out var centerTex) || centerTex == Vector2.Zero)
			return;

		rightPath.Add(new Godot.Collections.Dictionary
		{
			{ "pid", pid },
			{ "map_pos", centerTex },
			{ "texture_pos", centerTex },
		});

		GD.Print($"Sampled province {pid}. Path length: {rightPath.Count}/{maxPathLength}");
	}

	private void _perform_path_assignment()
	{
		if (rightPath.Count == 0 || selectedTroops.Count == 0)
			return;

		var pathPids = new Godot.Collections.Array();
		foreach (var entryValue in rightPath)
		{
			var entry = entryValue.AsGodotDictionary();
			var pid = entry["pid"].AsInt32();
			if (pathPids.Count == 0 || pathPids[pathPids.Count - 1].AsInt32() != pid)
				pathPids.Add(pid);
		}

		if (pathPids.Count == 0)
			return;

		// 1. Cast the moving pool correctly.
		var gameUi = GameState.Instance.gameUi;
		var uiSelected = gameUi != null && GodotObject.IsInstanceValid(gameUi)
			? gameUi.selectedDivisionObjects
			: new List<DivisionData>();

		var movingPool = new List<DivisionData>();
		if (uiSelected.Count > 0)
		{
			foreach (var div in uiSelected)
				movingPool.Add(div);
		}
		else
		{
			foreach (var t in selectedTroops)
			{
				foreach (var div in t.storedDivisions)
					movingPool.Add(div);
			}
		}

		// 2. Ensure the Dictionary values are typed arrays.
		var poolByOrigin = new Dictionary<int, List<DivisionData>>();
		foreach (var div in movingPool)
		{
			var owningPolity = TroopManager.Instance.FindTroopOwningDivision(div) as TroopData;
			if (owningPolity == null)
				continue;

			var originId = owningPolity.provinceId;
			if (!poolByOrigin.TryGetValue(originId, out var originBatch))
			{
				originBatch = new List<DivisionData>();
				poolByOrigin[originId] = originBatch;
			}
			originBatch.Add(div);
		}

		var allAssignments = new List<Dictionary<string, object?>>();
		foreach (var pair in poolByOrigin)
		{
			var originId = pair.Key;
			var originBatch = pair.Value;

			TroopData template = null;
			foreach (var t in selectedTroops)
			{
				if (t.provinceId == originId)
				{
					template = t;
					break;
				}
			}

			if (template == null)
			{
				var troopsAtOrigin = TroopManager.Instance.GetTroopsInProvince(originId);
				if (troopsAtOrigin.Count > 0)
					template = troopsAtOrigin[0];
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
				var finalDivs = new List<DivisionData>();
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

				var dispatcher = ActionDispatcher.Instance;
				dispatcher.Submit(new SplitTroopAction
				{
					Issuer = CurrentIssuer(), Original = template, Divisions = finalDivs,
				});
				var newTroop = dispatcher.LastResult as TroopData;
				allAssignments.Add(new Dictionary<string, object?>
				{
					{ "troop", newTroop },
					{ "province_id", (double)pathPids[provinceIdx] },
				});
			}
		}

		ActionDispatcher.Instance.Submit(new MoveTroopsAction
		{
			Issuer = CurrentIssuer(), Assignments = allAssignments,
		});
		_cleanup_empty_troops();

		selectedTroops.Clear();
		rightPath.Clear();
		EmitSelectionChanged();
	}

	// --- Helper functions for the logic above ---

	private void _remove_division_from_current_owner(DivisionData div)
	{
		// Search all our selected troops and remove the div from their stored_divisions.
		foreach (var t in selectedTroops)
		{
			if (t.storedDivisions.Contains(div))
			{
				t.storedDivisions.Remove(div);
				return;
			}
		}
	}

	private void _cleanup_empty_troops()
	{
		// If a troop gave away all its divisions, delete it from the world.
		foreach (var t in selectedTroops)
		{
			if (t.storedDivisions.Count == 0)
				ActionDispatcher.Instance.Submit(new RemoveTroopAction { Issuer = CurrentIssuer(), Troop = t });
		}
	}

}
