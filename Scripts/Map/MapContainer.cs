using System;
using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

// Script on Main/MapContainer: map mouse input (click/hover) and the
// retained-mode renderer for cities, troops, battles and paths.
//
// MapContainer lives in the main scene, so it stays in the tree in MENU and
// EDITOR as well. Input and rendering only run while the world is active.
//
// Rendering is built on RenderingServer.
//
// Nothing here uses _draw(). Every visual is a RenderingServer canvas item whose
// draw commands are recorded once and kept by the server. Per frame we only move
// items (canvas_item_set_transform) or rewrite a MultiMesh buffer. Commands are
// re-recorded only when what they show changes (division count, selection,
// battle progress, hovered city, ...).
//
// Item tree (all children of _root, drawn in this order). _root sits on z 20 so
// it draws above the map sprites, which are child nodes of this node:
//   _city_roots[0..2]  one per world-wrap copy, each holds the shared city MultiMesh
//   _city_label        hovered city name
//   _troop_root        one child item per troop (+ a detail child for flag/text)
//   _battle_root       one child item per battle
//   _overlay           selection box, path preview, movement lines

[GlobalClass]
public partial class MapContainer : Node2D
{
	// --- Constants & Config ---
	public static readonly Godot.Collections.Dictionary COLORS = new()
	{
		{ "background", new Color(0, 0, 0, 0.8f) },
		{ "text", new Color(1, 1, 1, 1) },
		{ "border_default", new Color(0, 1, 0, 1) },
		{ "border_selected", new Color(0.8f, 0.8f, 0.8f) },
		{ "border_other", new Color(0, 0, 0, 1) },
		{ "movement_active", new Color(0, 1, 0, 0.8f) },
		{ "path_active", new Color(1, 0.2f, 0.2f) },
		{ "path_inactive", new Color(0.5f, 0.5f, 0.5f) },
		{ "city", Colors.White },
		{ "capital", Colors.Red },
	};

	public static readonly Godot.Collections.Dictionary LAYOUT = new()
	{
		{ "flag_width", 24.0f },
		{ "flag_height", 20.0f },
		{ "min_text_width", 16.0f },
		{ "font_size", 16 },
	};

	public static readonly Godot.Collections.Dictionary ZOOM_LIMITS = new()
	{
		{ "min_scale", 0.05f },
		{ "max_scale", 0.5f },
	};

	// Extra map copies drawn to either side of the sprite so horizontal scrolling
	// never runs off the edge of the world.
	public static readonly int[] WRAP_MULTIPLIERS = { -2, -1, 1, 2 };

	public static readonly float STACKING_OFFSET_Y = 20.0f;
	public static readonly float TROOP_BORDER = 2.0f;

	// Troops (box, background, flag and text) are hidden while the raw Camera2D zoom
	// is below this value. Compare against the raw zoom, not
	// _current_inv_zoom: that value is clamped, so it stops changing at zoom <= 2.
	public static readonly float TROOP_DETAIL_MIN_ZOOM = 0.5f;

	public static readonly float BASE_DOT_RADIUS = 4.0f;
	public static readonly int DOT_SEGMENTS = 12;
	public static readonly int BASE_FONT_SIZE = 24;
	public static readonly Vector2 OFFSET = new Vector2(10, BASE_FONT_SIZE * 0.3f);

	public static readonly Godot.Collections.Dictionary BATTLE = new()
	{
		{ "base_radius", 1.0f },
		{ "ring_radius", 1.2f },
		{ "line_width", 0.5f },
		{ "segments", 16 },
	};

	// 2D MultiMesh buffer layout: 8 floats transform + 4 floats color.
	public static readonly int MM_STRIDE = 12;

	public static readonly int RENDER_Z_INDEX = 20;

	// --- Variables ---
	private Font _font;
	public Sprite2D mapSprite;
	private float _mapWidth;
	public float mapWidth
	{
		get => _mapWidth;
		set
		{
			_mapWidth = value;
			_RebuildCities();
		}
	}

	private Godot.Collections.Array<Sprite2D> _wrapSprites = new();

	private float _currentInvZoom = 1.0f;
	private float _camX = 0.0f;
	private float _camZoom = 1.0f; // raw Camera2D zoom, not clamped

	private Vector2 _lastCamPos = Vector2.Inf;
	private Vector2 _lastCamZoom = Vector2.Inf;

	// Cities
	private ArrayMesh _cityMesh;
	private Rid _cityMm;
	private Godot.Collections.Array<Rid> _cityRoots = new();
	private Godot.Collections.Array<Rid> _cityItems = new();
	private Vector2[] _cityPositions = Array.Empty<Vector2>();
	private int[] _cityPids = Array.Empty<int>();
	private string[] _cityNames = Array.Empty<string>();
	private Dictionary<int, int> _cityIndexByPid = new(); // pid -> index of its first city
	private Color[] _cityColors = Array.Empty<Color>();
	private float[] _cityBuffer = Array.Empty<float>();
	private bool _cityColorsDirty;
	private Rid _cityLabel;
	private int _labelIndex = -2; // index into _city_* arrays, -1 = none, -2 = force refresh

	// Troops: TroopData -> { item, detail, sig }
	private Rid _troopRoot;
	private Dictionary<TroopData, TroopEntry> _troopItems = new();

	// Battles: Battle -> { item, sig }
	private Rid _battleRoot;
	private Dictionary<Battle, BattleEntry> _battleItems = new();

	// Root of every item below; carries the z index.
	private Rid _root;

	// Overlay
	private Rid _overlay;
	private bool _overlayHadContent;

	private bool _signalsConnected;

	private sealed class TroopEntry
	{
		public Rid Item;
		public Rid Detail;
		public Godot.Collections.Array Sig = new();
	}

	private sealed class BattleEntry
	{
		public Rid Item;
		public Godot.Collections.Array Sig = new();
	}

	// --- Autoload access (never in a constructor) ---
	private MapManager Map => (MapManager)MapManager.Instance;
	private GameState State => (GameState)GameState.Instance;
	private TroopManager Troops => (TroopManager)TroopManager.Instance;
	private WarManager Wars => (WarManager)WarManager.Instance;

	private static bool IsWorldActive() => SceneManager.Instance.IsWorldActive();
	private static bool IsConsoleVisible() => GameConsole.IsVisible();

	private static Color Col(string aKey) => COLORS[aKey].AsColor();
	private static float LayoutF(string aKey) => LAYOUT[aKey].AsSingle();
	private static int LayoutI(string aKey) => LAYOUT[aKey].AsInt32();
	private static float BattleF(string aKey) => BATTLE[aKey].AsSingle();
	private static int BattleI(string aKey) => BATTLE[aKey].AsInt32();

	// GDScript compares Arrays element-wise; mirror that for the re-record signature.
	private static bool SigEquals(Godot.Collections.Array a, Godot.Collections.Array b)
	{
		if (a.Count != b.Count)
			return false;
		for (var i = 0; i < a.Count; i++)
		{
			if (!Equals(a[i], b[i]))
				return false;
		}
		return true;
	}

	// --- Input ---
	public override void _Input(InputEvent @event)
	{
		if (!IsWorldActive() || mapSprite == null)
			return;
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (mouseButton.Pressed)
				HandleClickDown(GetGlobalMousePosition());
			else
				HandleClick(GetGlobalMousePosition());
		}
		if (@event is InputEventMouseMotion)
			HandleHover(GetGlobalMousePosition());
	}

	private bool _IsMouseOverUi() => GetViewport().GuiGetHoveredControl() != null;

	public void HandleClickDown(Vector2 aGlobalPos)
	{
		if (_IsMouseOverUi() || IsConsoleVisible())
			return;

		var selection = Troops?.troopSelection;
		if (selection != null && GodotObject.IsInstanceValid(selection))
			selection.Call("DeselectAll");
	}

	public void HandleHover(Vector2 aGlobalPos)
	{
		if (_IsMouseOverUi() || State.inPeaceProcess)
		{
			Map.ClearHover();
			return;
		}

		var map = Map;
		var pid = map.GetProvinceAtPos(aGlobalPos, mapSprite);
		map.currentHoveredPid = pid;
		if (pid == map.lastHoveredPid)
			return;

		var highlightColor = map.GetContextualHighlight(pid);
		if (State.interactionType == GameState.InteractionType.SELECT_COUNTRY)
			map.HoverCountry(pid, highlightColor);
		else
			map.HoverProvince(pid, highlightColor);
	}

	public void HandleClick(Vector2 aGlobalPos)
	{
		if (_IsMouseOverUi() || IsConsoleVisible() || State.inPeaceProcess)
			return;

		var map = Map;
		var pid = map.GetProvinceWithRadius(aGlobalPos, mapSprite, 5);
		if (!map.IsLandProvince(pid))
		{
			map.ClickSea();
			return;
		}

		switch (State.interactionType)
		{
			case GameState.InteractionType.SELECT_COUNTRY:
				ActionDispatcher.Instance.Submit(new SelectCountryAction { ProvinceId = pid });
				break;
			case GameState.InteractionType.DEPLOY_PROVINCE:
				ActionDispatcher.Instance.Submit(new DeployAction { ProvinceId = pid });
				break;
			case GameState.InteractionType.BUILD_PROVINCE:
				ActionDispatcher.Instance.Submit(new BuildAction { ProvinceId = pid });
				break;
			default:
				// Prevent menu from spawning when selecting troops (annoying)
				var selection = Troops?.troopSelection;
				var selected = selection != null && GodotObject.IsInstanceValid(selection)
					? selection.Get("selectedTroops").AsGodotArray()
					: null;
				if (selected == null || selected.Count == 0)
				{
					Province province = map.provinceObjects.TryGetValue(pid, out var clickedProvince)
						? clickedProvince
						: null;
					var owner = province != null ? province.GetFunctionalOwner() : "";
					MapManager.Instance.EmitSignal("CountryClicked", owner);
				}
				break;
		}
	}

	// --- Lifecycle ---
	public override void _Ready()
	{
		mapSprite = GetNode<Sprite2D>("CultureSprite");
		_font = GD.Load<Font>("res://font/arial.TTF");
		_CreateRoots();
	}

	// RIDs are freed only when the node itself is deleted, never in _ExitTree.
	public override void _Notification(int aWhat)
	{
		if (aWhat == NotificationPredelete)
			_FreeAll();
	}

	public override void _Process(double aDelta)
	{
		var active = IsWorldActive();
		RenderingServer.CanvasItemSetVisible(_root, active);
		if (!active || mapSprite == null || mapWidth <= 0)
			return;

		var cam = GetViewport().GetCamera2D();
		if (cam == null)
			return;

		_SyncWrapSprites();
		_LoopCameraX(cam);

		_ConnectSignals();
		_camX = cam.GlobalPosition.X;
		_camZoom = cam.Zoom.X;

		var zoomChanged = cam.Zoom != _lastCamZoom;
		if (zoomChanged || cam.GlobalPosition != _lastCamPos)
		{
			_currentInvZoom = Mathf.Clamp(1.0f / cam.Zoom.X, ZOOM_LIMITS["min_scale"].AsSingle(), ZOOM_LIMITS["max_scale"].AsSingle());
			_lastCamZoom = cam.Zoom;
			_lastCamPos = cam.GlobalPosition;
		}

		if (zoomChanged)
			_WriteCityBuffer();

		if (_cityColorsDirty)
			_RefreshCityColors();

		var peace = State.inPeaceProcess;
		// Far zoom hides the whole troop layer: box, background, flag and text together.
		var showTroops = !peace && _camZoom >= TROOP_DETAIL_MIN_ZOOM;
		RenderingServer.CanvasItemSetVisible(_troopRoot, showTroops);
		RenderingServer.CanvasItemSetVisible(_battleRoot, !peace);

		_UpdateCityLabel();
		if (showTroops)
			_UpdateTroops();
		if (!peace)
			_UpdateBattles();
		_UpdateOverlay(peace);
	}

	// --- Setup / teardown ---
	private void _CreateRoots()
	{
		// A plain child item would draw under the map sprites (parents draw before
		// their child nodes). Raising z on _root only keeps the sprites at z 0.
		_root = RenderingServer.CanvasItemCreate();
		RenderingServer.CanvasItemSetParent(_root, GetCanvasItem());
		RenderingServer.CanvasItemSetZIndex(_root, RENDER_Z_INDEX);
		RenderingServer.CanvasItemSetVisible(_root, false);

		var parent = _root;
		var drawIndex = 0;

		for (var m = -1; m <= 1; m++)
		{
			var root = RenderingServer.CanvasItemCreate();
			RenderingServer.CanvasItemSetParent(root, parent);
			RenderingServer.CanvasItemSetDrawIndex(root, drawIndex);
			_cityRoots.Add(root);
			drawIndex++;
		}

		_cityLabel = _MakeChild(parent, drawIndex); drawIndex++;
		_troopRoot = _MakeChild(parent, drawIndex); drawIndex++;
		_battleRoot = _MakeChild(parent, drawIndex); drawIndex++;
		_overlay = _MakeChild(parent, drawIndex);

		_cityMm = RenderingServer.MultimeshCreate();
		_cityMesh = _BuildDotMesh(BASE_DOT_RADIUS, DOT_SEGMENTS);
		RenderingServer.MultimeshSetMesh(_cityMm, _cityMesh.GetRid());
	}

	private Rid _MakeChild(Rid aParent, int aDrawIndex)
	{
		var item = RenderingServer.CanvasItemCreate();
		RenderingServer.CanvasItemSetParent(item, aParent);
		RenderingServer.CanvasItemSetDrawIndex(item, aDrawIndex);
		return item;
	}

	private void _FreeAll()
	{
		foreach (var entry in _troopItems.Values)
			_FreeTroopEntry(entry);
		_troopItems.Clear();
		foreach (var entry in _battleItems.Values)
			RenderingServer.FreeRid(entry.Item);
		_battleItems.Clear();
		foreach (var item in _cityItems)
			RenderingServer.FreeRid(item);
		_cityItems.Clear();
		var rids = new List<Rid>(_cityRoots)
		{
			_cityLabel, _troopRoot, _battleRoot, _overlay, _cityMm, _root,
		};
		foreach (var rid in rids)
		{
			if (rid.IsValid)
				RenderingServer.FreeRid(rid);
		}
		_cityRoots.Clear();
	}

	private void _ConnectSignals()
	{
		if (_signalsConnected)
			return;
		_signalsConnected = true;

		var map = Map;
		if (map != null)
			map.ProvinceOwnershipChanged += _OnProvinceOwnershipChanged;
		var countryManager = CountryManager.Instance;
		if (countryManager != null)
		{
			countryManager.PlayerCountryChanged += _OnPlayerCountryChanged;
			countryManager.DayProcessed += _OnDayPassed;
		}
	}

	private void _OnProvinceOwnershipChanged(int aPid, string aOldOwner, string aNewOwner)
	{
		_cityColorsDirty = true;
	}

	private void _OnDayPassed()
	{
		// Capitals can move through decisions/events; recheck once per day.
		_cityColorsDirty = true;
	}

	private void _OnPlayerCountryChanged()
	{
		// Border colors depend on the player country; force every troop to re-record.
		foreach (var entry in _troopItems.Values)
			entry.Sig.Clear();
		foreach (var entry in _battleItems.Values)
			entry.Sig.Clear();
	}

	// --- Cities ---
	private ArrayMesh _BuildDotMesh(float aRadius, int aSegments)
	{
		var verts = new Vector2[aSegments * 3];
		for (var i = 0; i < aSegments; i++)
		{
			var a0 = Mathf.Tau * i / aSegments;
			var a1 = Mathf.Tau * (i + 1) / aSegments;
			verts[i * 3] = Vector2.Zero;
			verts[i * 3 + 1] = Vector2.FromAngle(a0) * aRadius;
			verts[i * 3 + 2] = Vector2.FromAngle(a1) * aRadius;
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	// Collects city data and (re)allocates the city MultiMesh.
	// Runs whenever World sets map_width, i.e. on every map load.
	private void _RebuildCities()
	{
		if (!_cityMm.IsValid || mapSprite == null || mapWidth <= 0)
			return;

		var positions = new List<Vector2>();
		var pids = new List<int>();
		var names = new List<string>();
		var map = Map;
		_cityIndexByPid.Clear();
		foreach (var cityData in map.allCities)
		{
			var pid = cityData["id"].AsInt32();
			var pos = map.provinceCenters.TryGetValue(pid, out var center)
				? center
				: Vector2.Zero;
			if (pos == Vector2.Zero)
				continue;
			if (!_cityIndexByPid.ContainsKey(pid))
				_cityIndexByPid[pid] = positions.Count;
			positions.Add(pos);
			pids.Add(pid);
			names.Add(cityData["city"].AsString());
		}

		_cityPositions = positions.ToArray();
		_cityPids = pids.ToArray();
		_cityNames = names.ToArray();

		var count = _cityPositions.Length;
		RenderingServer.MultimeshAllocateData(_cityMm, count, RenderingServer.MultimeshTransformFormat.Transform2D, true);
		_cityBuffer = new float[count * MM_STRIDE];
		_cityColors = new Color[count];

		// One item per wrap copy, all drawing the same MultiMesh.
		foreach (var item in _cityItems)
			RenderingServer.FreeRid(item);
		_cityItems.Clear();
		for (var m = -1; m <= 1; m++)
		{
			var root = _cityRoots[m + 1];
			RenderingServer.CanvasItemSetTransform(root, new Transform2D(0f, mapSprite.Position + new Vector2(mapWidth * m, 0)));
			var item = RenderingServer.CanvasItemCreate();
			RenderingServer.CanvasItemSetParent(item, root);
			RenderingServer.CanvasItemAddMultimesh(item, _cityMm);
			_cityItems.Add(item);
		}

		_ComputeCityColors();
		_WriteCityBuffer();
		_labelIndex = -2;
	}

	private void _ComputeCityColors()
	{
		var map = Map;
		var countryManager = CountryManager.Instance;
		for (var i = 0; i < _cityPids.Length; i++)
		{
			Province province = map.provinceObjects.TryGetValue(_cityPids[i], out var cityProvince)
				? cityProvince
				: null;
			GodotObject country = null;
			if (province != null && countryManager != null)
			{
				var countryName = province.country;
				if (countryManager.countries.ContainsKey(countryName))
					country = countryManager.countries[countryName].AsGodotObject();
			}
			var isCapital = country != null && country.Get("capital").AsString() == _cityNames[i];
			_cityColors[i] = isCapital ? Col("capital") : Col("city");
		}
	}

	private void _RefreshCityColors()
	{
		_cityColorsDirty = false;
		_ComputeCityColors();
		_WriteCityBuffer();
		_labelIndex = -2;
	}

	// Writes transforms (scaled by inverse zoom) and colors in one call.
	private void _WriteCityBuffer()
	{
		var count = _cityPositions.Length;
		if (count == 0)
			return;
		var s = _currentInvZoom;
		var b = _cityBuffer;
		for (var i = 0; i < count; i++)
		{
			var o = i * MM_STRIDE;
			var p = _cityPositions[i];
			var c = _cityColors[i];
			b[o] = s; b[o + 1] = 0.0f; b[o + 2] = 0.0f; b[o + 3] = p.X;
			b[o + 4] = 0.0f; b[o + 5] = s; b[o + 6] = 0.0f; b[o + 7] = p.Y;
			b[o + 8] = c.R; b[o + 9] = c.G; b[o + 10] = c.B; b[o + 11] = c.A;
		}
		RenderingServer.MultimeshSetBuffer(_cityMm, b);
	}

	private void _UpdateCityLabel()
	{
		var hovered = Map.currentHoveredPid;
		var index = _cityIndexByPid.TryGetValue(hovered, out var found) ? found : -1;

		if (index != _labelIndex)
		{
			_labelIndex = index;
			RenderingServer.CanvasItemClear(_cityLabel);
			if (index >= 0)
			{
				var cityName = _cityNames[index];
				_font.DrawStringOutline(_cityLabel, OFFSET, cityName, HorizontalAlignment.Left, -1, BASE_FONT_SIZE, 4, new Color(0, 0, 0, 1.0f));
				_font.DrawString(_cityLabel, OFFSET, cityName, HorizontalAlignment.Left, -1, BASE_FONT_SIZE, _cityColors[index]);
			}
		}

		if (_labelIndex >= 0)
		{
			var pos = _WrapNearest(_cityPositions[_labelIndex] + mapSprite.Position);
			RenderingServer.CanvasItemSetTransform(_cityLabel, new Transform2D(0f, new Vector2(_currentInvZoom, _currentInvZoom), 0f, pos));
		}
	}

	// --- Troops ---
	private void _UpdateTroops()
	{
		var playerCountry = _PlayerCountryName();
		var selection = Troops?.troopSelection;
		var selected = new HashSet<TroopData>();
		if (selection != null)
			foreach (var troop in selection.selectedTroops)
				selected.Add(troop);

		// Create items for new troops, free items of removed troops.
		var alive = new HashSet<TroopData>();
		var troops = Troops?.troops;
		if (troops != null)
		{
			foreach (var troopVariant in troops)
			{
				if (troopVariant is not TroopData troop)
					continue;
				alive.Add(troop);
				if (!_troopItems.ContainsKey(troop))
				{
					var item = RenderingServer.CanvasItemCreate();
					RenderingServer.CanvasItemSetParent(item, _troopRoot);
					var detail = RenderingServer.CanvasItemCreate();
					RenderingServer.CanvasItemSetParent(detail, item);
					_troopItems[troop] = new TroopEntry { Item = item, Detail = detail };
				}
			}
		}
		foreach (var troop in new List<TroopData>(_troopItems.Keys))
		{
			if (!alive.Contains(troop))
			{
				_FreeTroopEntry(_troopItems[troop]);
				_troopItems.Remove(troop);
			}
		}

		var zoomVec = new Vector2(_currentInvZoom, _currentInvZoom);
		var scaledOffset = STACKING_OFFSET_Y * _currentInvZoom;
		var groups = _GroupTroopsByVisualPosition(troops ?? new List<TroopData>());

		foreach (var group in groups)
		{
			var basePos = group.Key;
			var stack = group.Value;
			var startY = (stack.Count - 1) * scaledOffset * 0.5f;
			for (var i = 0; i < stack.Count; i++)
			{
				var troop = stack[i];
				if (!_troopItems.TryGetValue(troop, out var entry))
					continue;

				var border = Col("border_other");
				if (troop.countryName == playerCountry)
					border = selected.Contains(troop) ? Col("border_selected") : Col("border_default");
				var ideology = "";
				if (troop.countryObj is GodotObject obj && GodotObject.IsInstanceValid(obj))
					ideology = obj.Get("ideologyName").AsString();
				var sig = new Godot.Collections.Array { troop.divisionsCount, border, troop.countryName, ideology };
				if (!SigEquals(sig, entry.Sig))
				{
					entry.Sig = sig;
					_RecordTroop(entry, troop, border, ideology);
				}

				var pos = basePos + mapSprite.Position + new Vector2(0, startY - i * scaledOffset);
				RenderingServer.CanvasItemSetTransform(entry.Item, new Transform2D(0f, zoomVec, 0f, _WrapNearest(pos)));
			}
		}
	}

	private void _RecordTroop(TroopEntry aEntry, TroopData aTroop, Color aBorder, string aIdeology)
	{
		var item = aEntry.Item;
		var detail = aEntry.Detail;
		var w = LayoutF("flag_width") + LayoutF("min_text_width");
		var h = LayoutF("flag_height");
		var rect = new Rect2(-w * 0.5f, -h * 0.5f, w, h);

		// Box: border strips.
		RenderingServer.CanvasItemClear(item);
		var b = TROOP_BORDER;
		RenderingServer.CanvasItemAddRect(item, new Rect2(rect.Position, new Vector2(w, b)), aBorder);
		RenderingServer.CanvasItemAddRect(item, new Rect2(rect.Position + new Vector2(0, h - b), new Vector2(w, b)), aBorder);
		RenderingServer.CanvasItemAddRect(item, new Rect2(rect.Position + new Vector2(0, b), new Vector2(b, h - 2 * b)), aBorder);
		RenderingServer.CanvasItemAddRect(item, new Rect2(rect.Position + new Vector2(w - b, b), new Vector2(b, h - 2 * b)), aBorder);

		// Detail: translucent inner fill + flag + division count.
		RenderingServer.CanvasItemClear(detail);
		RenderingServer.CanvasItemAddRect(detail, rect.Grow(-b), Col("background"));
		var flag = Troops?.GetFlag(aTroop.countryName, aIdeology);
		if (flag != null && GodotObject.IsInstanceValid(flag))
			flag.DrawRect(detail, new Rect2(rect.Position, new Vector2(LayoutF("flag_width"), h)).Grow(-1.0f), false);

		var label = aTroop.divisionsCount.ToString();
		var textSize = _font.GetStringSize(label, HorizontalAlignment.Left, -1, LayoutI("font_size"));
		_font.DrawString(
			detail,
			new Vector2(rect.Position.X + LayoutF("flag_width") + (LayoutF("min_text_width") - textSize.X) * 0.5f, textSize.Y * 0.3f),
			label,
			HorizontalAlignment.Left,
			-1,
			LayoutI("font_size"),
			Col("text")
		);
	}

	private void _FreeTroopEntry(TroopEntry aEntry)
	{
		RenderingServer.FreeRid(aEntry.Detail);
		RenderingServer.FreeRid(aEntry.Item);
	}

	private Dictionary<Vector2, List<TroopData>> _GroupTroopsByVisualPosition(List<TroopData> aTroops)
	{
		var g = new Dictionary<Vector2, List<TroopData>>();
		foreach (var troopVariant in aTroops)
		{
			if (troopVariant is not TroopData t)
				continue;
			if (!g.TryGetValue(t.position.ToGodot(), out var stack))
			{
				stack = new List<TroopData>();
				g[t.position.ToGodot()] = stack;
			}
			stack.Add(t);
		}
		return g;
	}

	// --- Battles ---
	private void _UpdateBattles()
	{
		var playerCountry = State.interactionType != GameState.InteractionType.SELECT_COUNTRY
			? _PlayerCountryName()
			: "";

		var alive = new HashSet<Battle>();
		var battles = Wars?.activeBattles;
		if (battles != null)
		{
			foreach (var battle in battles)
			{
				alive.Add(battle);
				if (!_battleItems.TryGetValue(battle, out var entry))
				{
					var item = RenderingServer.CanvasItemCreate();
					RenderingServer.CanvasItemSetParent(item, _battleRoot);
					entry = new BattleEntry { Item = item };
					_battleItems[battle] = entry;
				}

				var sig = new Godot.Collections.Array { battle.attackProgress, battle.attackerCountry, battle.defenderCountry, playerCountry };
				if (!SigEquals(sig, entry.Sig))
				{
					entry.Sig = sig;
					_RecordBattle(entry.Item, battle, playerCountry);
				}

				RenderingServer.CanvasItemSetTransform(entry.Item, new Transform2D(0f, _WrapNearest(battle.position)));
			}
		}

		foreach (var battle in new List<Battle>(_battleItems.Keys))
		{
			if (!alive.Contains(battle))
			{
				RenderingServer.FreeRid(_battleItems[battle].Item);
				_battleItems.Remove(battle);
			}
		}
	}

	private void _RecordBattle(Rid aItem, Battle aBattle, string aPlayerCountry)
	{
		var isPlayerInvolved = false;
		var isWinning = true;
		var displayRatio = aBattle.attackProgress;

		if (aBattle.attackerCountry == aPlayerCountry)
		{
			isPlayerInvolved = true;
			isWinning = displayRatio > 0.5f;
		}
		else if (aBattle.defenderCountry == aPlayerCountry)
		{
			isPlayerInvolved = true;
			displayRatio = 1.0f - displayRatio;
			isWinning = displayRatio > 0.5f;
		}

		var color = new Color(0.8f, 0.5f, 0.0f);
		if (isPlayerInvolved)
			color = isWinning ? new Color(0.0f, 1.0f, 0.0f) : new Color(1.0f, 0.0f, 0.0f);

		var startAngle = -Mathf.Pi * 0.5f; // Top
		var endAngle = startAngle;
		if (isWinning)
			endAngle += displayRatio * Mathf.Tau;
		else
			startAngle -= displayRatio * Mathf.Tau;

		RenderingServer.CanvasItemClear(aItem);
		// Dark backing circle so the tiny icon stands out.
		RenderingServer.CanvasItemAddCircle(aItem, Vector2.Zero, BattleF("ring_radius") + 0.3f, new Color(0, 0, 0, 0.8f));
		// Progress arc (RenderingServer has no arc primitive, so build a polyline).
		if (!Mathf.IsEqualApprox(startAngle, endAngle))
		{
			var segments = BattleI("segments");
			var points = new Vector2[segments + 1];
			for (var i = 0; i <= segments; i++)
				points[i] = Vector2.FromAngle(Mathf.Lerp(startAngle, endAngle, (float)i / segments)) * BattleF("ring_radius");
			RenderingServer.CanvasItemAddPolyline(aItem, points, new[] { color }, BattleF("line_width"), true);
		}
		RenderingServer.CanvasItemAddCircle(aItem, Vector2.Zero, BattleF("base_radius"), Colors.White);
	}

	// --- Overlay (selection box, path preview, movement lines) ---
	private void _UpdateOverlay(bool aPeace)
	{
		var ts = Troops?.troopSelection;
		var anyMoving = false;
		if (!aPeace)
		{
			foreach (var troopVariant in Troops?.troops ?? new List<TroopData>())
			{
				if (troopVariant is TroopData t && t.isMoving)
				{
					anyMoving = true;
					break;
				}
			}
		}

		var hasContent = !aPeace && ts != null && GodotObject.IsInstanceValid(ts)
			&& (ts.Get("dragging").AsBool() || ts.Get("rightDragging").AsBool() || anyMoving);
		if (!hasContent && !_overlayHadContent)
			return; // Nothing drawn last frame and nothing to draw now.
		_overlayHadContent = hasContent;

		RenderingServer.CanvasItemClear(_overlay);
		if (!hasContent)
			return;

		_RecordPathPreview(ts);
		if (anyMoving)
			_RecordActiveMovements();
		_RecordSelectionBox(ts);
	}

	private void _RecordSelectionBox(GodotObject aTs)
	{
		if (!aTs.Get("dragging").AsBool())
			return;
		var dragStart = aTs.Get("dragStart").AsVector2();
		var dragEnd = aTs.Get("dragEnd").AsVector2();
		if (dragStart == dragEnd)
			return;
		var rect = new Rect2(dragStart, dragEnd - dragStart).Abs();
		RenderingServer.CanvasItemAddRect(_overlay, rect, new Color(1, 1, 1, 0.3f));
		var outline = new[]
		{
			rect.Position,
			new Vector2(rect.End.X, rect.Position.Y),
			rect.End,
			new Vector2(rect.Position.X, rect.End.Y),
			rect.Position,
		};
		RenderingServer.CanvasItemAddPolyline(_overlay, outline, new[] { Colors.White }, 1.0f);
	}

	private void _RecordPathPreview(GodotObject aTs)
	{
		if (!aTs.Get("rightDragging").AsBool())
			return;
		var path = aTs.Get("rightPath").AsGodotArray();
		var maxLen = aTs.Get("maxPathLength").AsInt32();

		if (path.Count < 2)
		{
			if (path.Count == 1)
			{
				var p = path[0].AsGodotDictionary()["map_pos"].AsVector2() + mapSprite.Position;
				RenderingServer.CanvasItemAddCircle(_overlay, p, 2.0f, maxLen > 0 ? Col("path_active") : Col("path_inactive"));
			}
			return;
		}

		var activePoints = new List<Vector2>();
		var inactivePoints = new List<Vector2>();

		for (var i = 0; i < path.Count; i++)
		{
			var p = path[i].AsGodotDictionary()["map_pos"].AsVector2() + mapSprite.Position;
			if (i <= maxLen)
			{
				activePoints.Add(p);
				// The connection point between active and inactive needs to be in both
				if (i == maxLen && i < path.Count - 1)
					inactivePoints.Add(p);
			}
			else
			{
				inactivePoints.Add(p);
			}
		}

		if (activePoints.Count >= 2)
			RenderingServer.CanvasItemAddPolyline(_overlay, activePoints.ToArray(), new[] { Col("path_active") }, 1.5f);
		if (inactivePoints.Count >= 2)
			RenderingServer.CanvasItemAddPolyline(_overlay, inactivePoints.ToArray(), new[] { Col("path_inactive") }, 1.5f);
	}

	private void _RecordActiveMovements()
	{
		foreach (var troopVariant in Troops?.troops ?? new List<TroopData>())
		{
			if (troopVariant is not TroopData troop || !troop.isMoving)
				continue;
			var start = _WrapNearest(troop.position.ToGodot() + mapSprite.Position);
			var end = start + (troop.targetPosition - troop.position).ToGodot();
			RenderingServer.CanvasItemAddLine(_overlay, start, end, new Color(1, 0, 0, 0.2f), 1.0f);
			RenderingServer.CanvasItemAddLine(_overlay, start, troop.position.ToGodot(), Col("movement_active"), 1.5f);
		}
	}

	// --- Helpers ---
	// Keeps the camera inside one map width. Wrap sprites cover the seam, so the
	// jump from the right edge back to the left edge is invisible.
	private void _LoopCameraX(Camera2D aCam)
	{
		var originX = mapSprite.Position.X;
		var pos = aCam.Position;
		pos.X = originX + Mathf.PosMod(pos.X - originX, mapWidth);
		aCam.Position = pos;
	}

	// Mirrors the map sprite at +/- one and two map widths. Sync runs every frame
	// so a changed texture or material (scenario reload) updates every copy.
	private void _SyncWrapSprites()
	{
		if (mapSprite == null || mapWidth <= 0)
			return;
		if (_wrapSprites.Count == 0)
		{
			for (var i = 0; i < WRAP_MULTIPLIERS.Length; i++)
			{
				var ghost = new Sprite2D
				{
					Centered = mapSprite.Centered,
				};
				AddChild(ghost);
				_wrapSprites.Add(ghost);
			}
		}
		for (var i = 0; i < _wrapSprites.Count; i++)
		{
			var ghost = _wrapSprites[i];
			if (ghost.Texture != mapSprite.Texture)
				ghost.Texture = mapSprite.Texture;
			if (ghost.Material != mapSprite.Material)
				ghost.Material = mapSprite.Material;
			ghost.Visible = mapSprite.Visible;
			ghost.Position = mapSprite.Position + new Vector2(mapWidth * WRAP_MULTIPLIERS[i], 0.0f);
		}
	}

	// Picks the world-wrap copy of a position closest to the camera.
	// Correct while the visible area is narrower than one map width.
	private Vector2 _WrapNearest(Vector2 aP)
	{
		aP.X += Mathf.Round((_camX - aP.X) / mapWidth) * mapWidth;
		return aP;
	}

	private string _PlayerCountryName()
	{
		var player = CountryManager.Instance?.playerCountry;
		return player != null && GodotObject.IsInstanceValid(player)
			? player.Get("countryName").AsString()
			: "";
	}
}
