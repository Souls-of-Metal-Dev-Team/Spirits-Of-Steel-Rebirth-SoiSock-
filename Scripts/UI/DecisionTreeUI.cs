using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace Sosr;

public partial class DecisionTreeUI : CanvasLayer
{
	// --- CONFIGURATION ---
	private const float GRID_SIZE = 50.0f;
	private static readonly Vector2 NODE_SIZE = new(220, 90);

	private static readonly Color COL_LINE_INACTIVE = new(0.3f, 0.3f, 0.3f);
	private static readonly Color COL_LINE_ACTIVE = new(0.2f, 0.8f, 0.2f);
	private static readonly Color COL_LINE_EXCLUSIVE = new(0.8f, 0.2f, 0.2f);

	// GraphNode slots. The body carries the input port; the two labels carry outputs.
	private const int SLOT_BODY = 0;
	private const int SLOT_PREREQ = 1;
	private const int SLOT_EXCLUSIVE = 2;

	// GraphNode ports are numbered per side. Slot 1 -> output port 0, slot 2 -> output port 1.
	private const int PORT_INPUT = 0;
	private const int PORT_PREREQ_OUT = 0;
	private const int PORT_EXCLUSIVE_OUT = 1;

	// Port types. TYPE_INPUT is accepted from both relation types via GraphEdit.
	private const int TYPE_PREREQ = 0;
	private const int TYPE_EXCLUSIVE = 1;
	private const int TYPE_INPUT = 2;

	// --- NODES ---
	private GraphEdit treeGraph;
	private TabBar tabsContainer;
	[Export] public RichTextLabel infoText;
	[Export] public TabContainer infoTab;
	[Export] public TextEdit editLabel;
	[Export] public TextEdit editDesc;
	[Export] public SpinBox editDays;
	[Export] public SpinBox editPpcost;
	[Export] public Button closeButton;
	[Export] public Button editButton;
	[Export] public HBoxContainer editingButtons;
	[Export] public CheckBox editFunction;
	[Export] public Button saveButton;
	[Export] public Button addDecision;
	[Export] public Button addReq;
	[Export] public Button addFinished;
	[Export] public TabContainer editorTab;
	[Export] public VBoxContainer reqsBlock;
	[Export] public VBoxContainer finishedBlock;

	public string currentCategory = "Economy";
	private readonly Dictionary<string, GraphNode> graphNodes = new(); // decision id -> GraphNode
	public bool editMode = false;
	private GDict selectedDecision = new();
	private GraphNode selectedNode = null;
	private bool _is_loading_expr = false;
	private (StringName node, long port, bool isOutput)? _drag_origin = null;

	private static bool DebugMode
		=> GameState.Instance.settings.GetValue("Developer Options", "debug_mode").AsBool();

	private static CountryData Player => CountryManager.Instance.Player;

	private static bool Finished(CountryData player, Variant id) => player.HasMeta("finished_" + id.ToString());

	public override void _Ready()
	{
		treeGraph = GetNode<GraphEdit>("CanvasAnchor/TreeGraph");
		tabsContainer = GetNode<TabBar>("StaticUI/Header/TabsContainer");

		DecisionManager.Instance.uiOverlay = this;
		editButton.Visible = DebugMode;

		treeGraph.AddValidConnectionType(TYPE_PREREQ, TYPE_INPUT);
		treeGraph.AddValidConnectionType(TYPE_INPUT, TYPE_PREREQ);
		treeGraph.AddValidConnectionType(TYPE_EXCLUSIVE, TYPE_INPUT);
		treeGraph.AddValidConnectionType(TYPE_INPUT, TYPE_EXCLUSIVE);
		treeGraph.ConnectionDragStarted += _on_connection_drag_started;
		treeGraph.ConnectionRequest += _on_connection_request;
		treeGraph.DisconnectionRequest += _on_disconnection_request;
		treeGraph.DeleteNodesRequest += _on_delete_nodes_request;
		treeGraph.EndNodeMove += _on_end_node_move;
		treeGraph.NodeSelected += _on_graph_node_selected;

		_on_edit_mode_toggled(false);
		tabsContainer.TabChanged += tab => _on_tab_changed((int)tab);
	}

	// --- LOGIC ---
	public void OpenMenu()
	{
		Show();
		_rebuild_tabs();
		_load_category(currentCategory);
		GameState.Instance.decisionMenuOpen = true;
		// Editing is only available in debug mode
		editButton.Visible = DebugMode;
		if (!DebugMode)
			editButton.SetPressedNoSignal(false);
		_on_edit_mode_toggled(editButton.ButtonPressed);
		_toggle_pause(true);
	}

	public void CloseMenu()
	{
		Hide();
		GameState.Instance.decisionMenuOpen = false;
		_toggle_pause(false);
	}

	private void _toggle_pause(bool pause)
	{
		var world = GameState.Instance.currentWorld;
		if (world != null)
		{
			world.SetProcess(!pause);
			world.clock?.SetProcess(!pause);
			world.FindChild("CameraController", true, false)?.SetProcess(!pause);
		}
		TroopManager.Instance.SetProcess(!pause);
	}

	private List<string> CategoryNames()
		=> DecisionManager.Instance.GetCountryCategories(Player.countryName).Keys.Select(k => k.AsString()).ToList();

	private void _rebuild_tabs()
	{
		while (tabsContainer.TabCount > 0)
			tabsContainer.RemoveTab(0);

		var categories = CategoryNames();
		for (var i = 0; i < categories.Count; i++)
		{
			tabsContainer.AddTab(categories[i].ToUpper());
			if (categories[i] == currentCategory)
				tabsContainer.CurrentTab = i;
		}
	}

	private void _on_tab_changed(int tabIdx)
	{
		var categories = CategoryNames();
		if (tabIdx < categories.Count)
		{
			currentCategory = categories[tabIdx];
			_load_category(currentCategory);
		}
	}

	private void _load_category(string catName)
	{
		foreach (var c in treeGraph.GetChildren())
		{
			if (c is GraphNode)
			{
				// Remove immediately so new nodes can reuse the same names.
				treeGraph.RemoveChild(c);
				c.QueueFree();
			}
		}
		treeGraph.ClearConnections();
		graphNodes.Clear();
		_clear_selection();

		var player = Player;
		var nodes = _category_nodes(catName);
		for (var i = 0; i < nodes.Count; i++)
			_create_node(nodes[i].AsGodotDictionary(), i, player);

		_update_connections();
		Callable.From(_refresh_port_colors).CallDeferred();
	}

	private void _update_connections()
	{
		treeGraph.ClearConnections();
		var nodes = _current_nodes();

		var nameById = new Dictionary<string, StringName>();
		for (var i = 0; i < nodes.Count; i++)
			nameById[nodes[i].AsGodotDictionary()["id"].ToString()] = _node_name(i);

		// Prerequisites point from the required decision to the dependent decision.
		foreach (var nodeVariant in nodes)
		{
			var node = nodeVariant.AsGodotDictionary();
			if (!nameById.TryGetValue(node["id"].ToString(), out var targetName))
				continue;
			foreach (var reqId in _as_array(Get(node, "prereq")))
			{
				if (nameById.TryGetValue(reqId.ToString(), out var sourceName) && sourceName != targetName)
					treeGraph.ConnectNode(sourceName, PORT_PREREQ_OUT, targetName, PORT_INPUT, true);
			}
		}

		// Exclusive edges are drawn once, between the lexicographically lower and higher ids.
		var drawnExclusives = new HashSet<string>();
		foreach (var nodeVariant in nodes)
		{
			var node = nodeVariant.AsGodotDictionary();
			var nodeId = node["id"].ToString();
			foreach (var exId in _as_array(Get(node, "exclusive")))
			{
				var otherId = exId.ToString();
				if (otherId == nodeId)
					continue;
				var lower = string.CompareOrdinal(nodeId, otherId) < 0;
				var lowId = lower ? nodeId : otherId;
				var highId = lower ? otherId : nodeId;
				if (!drawnExclusives.Add(lowId + "|" + highId))
					continue;

				if (nameById.TryGetValue(lowId, out var sourceName) && nameById.TryGetValue(highId, out var targetName))
					treeGraph.ConnectNode(sourceName, PORT_EXCLUSIVE_OUT, targetName, PORT_INPUT, true);
			}
		}
	}

	private static Variant Get(GDict dict, string key, Variant fallback = default)
		=> dict.TryGetValue(key, out var v) ? v : fallback;

	private static Label PortLabel(string name, string text)
	{
		var label = new Label { Name = name, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", 10);
		label.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
		return label;
	}

	private GraphNode _create_node(GDict data, int idx, CountryData player)
	{
		var node = new GraphNode
		{
			Name = _node_name(idx),
			Title = Get(data, "title", "").ToString(),
			PositionOffset = new Vector2(data["pos"].AsGodotArray()[0].AsSingle(), data["pos"].AsGodotArray()[1].AsSingle()),
			CustomMinimumSize = NODE_SIZE,
			Resizable = false,
			Draggable = editMode,
		};
		node.SetMeta("idx", idx);
		node.SetMeta("id", data["id"]);
		node.MouseEntered += () => _show_info(data);
		node.MouseExited += _reset_info;
		node.NodeSelected += () => _select_node(node);

		var body = new Button
		{
			Name = "Body",
			CustomMinimumSize = new Vector2(NODE_SIZE.X - 24, 44),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			TooltipText = _make_tooltip(data),
		};
		body.MouseEntered += () => _show_info(data);
		body.MouseExited += _reset_info;
		body.Pressed += () =>
		{
			if (editMode)
				_select_node(node);
			else
				DecisionManager.Instance.StartDecision(player, currentCategory, idx);
		};
		node.AddChild(body);

		node.AddChild(PortLabel("PrereqPort", "REQ"));
		node.AddChild(PortLabel("ExclusivePort", "EX"));

		// GraphNode builds its port cache on ready, so slots are set up then.
		node.Connect(Node.SignalName.Ready, Callable.From(() => _setup_node_slots(node)), (uint)ConnectFlags.OneShot);
		treeGraph.AddChild(node);
		_apply_node_style(node, data, player);

		graphNodes[data["id"].ToString()] = node;
		return node;
	}

	private void _setup_node_slots(GraphNode node)
	{
		if (!GodotObject.IsInstanceValid(node))
			return;
		var data = _data_for_node(node);
		if (data.Count == 0)
			return;

		var prereqColor = Finished(Player, data["id"]) ? COL_LINE_ACTIVE : COL_LINE_INACTIVE;
		node.SetSlot(SLOT_BODY, true, TYPE_INPUT, COL_LINE_INACTIVE, false, 0, Colors.White);
		node.SetSlot(SLOT_PREREQ, false, 0, Colors.White, true, TYPE_PREREQ, prereqColor);
		node.SetSlot(SLOT_EXCLUSIVE, false, 0, Colors.White, true, TYPE_EXCLUSIVE, COL_LINE_EXCLUSIVE);
	}

	private void _refresh_port_colors()
	{
		var player = Player;
		foreach (var node in graphNodes.Values)
		{
			if (!GodotObject.IsInstanceValid(node) || node.GetOutputPortCount() < 2)
				continue;
			var data = _data_for_node(node);
			if (data.Count == 0)
				continue;
			var prereqColor = Finished(player, data["id"]) ? COL_LINE_ACTIVE : COL_LINE_INACTIVE;
			node.SetSlotColorLeft(SLOT_BODY, COL_LINE_INACTIVE);
			node.SetSlotColorRight(SLOT_PREREQ, prereqColor);
			node.SetSlotColorRight(SLOT_EXCLUSIVE, COL_LINE_EXCLUSIVE);
		}
	}

	private void _show_info(GDict data)
	{
		var text = $"[b][font_size=26][color=#e8c070]{data["title"]}[/color][/font_size][/b]\n";
		text += $"[font_size=18][i][color=#c8c4ba]{Get(data, "desc", "")}[/color][/i][/font_size]\n\n";
		text += $"[color=#d9a35a]Time: {(int)data["days"].AsDouble()} Days | Cost: {(int)data["cost_pp"].AsDouble()} Political Power[/color]";

		if (data.TryGetValue("reqs", out var reqsVariant))
		{
			var finalReqTexts = new List<string>();
			foreach (var reqVariant in _as_array(reqsVariant))
			{
				if (reqVariant.VariantType != Variant.Type.Dictionary)
					continue;
				var req = reqVariant.AsGodotDictionary();
				var args = Get(req, "args", new GArray());
				var reqLine = Get(req, "func", "Unknown").ToString().Capitalize();
				if (args.VariantType == Variant.Type.Array && args.AsGodotArray().Count > 0)
					reqLine += ": " + string.Join(", ", args.AsGodotArray().Select(a => a.ToString().Capitalize()));
				finalReqTexts.Add(reqLine);
			}

			if (finalReqTexts.Count > 0)
				text += $"\n[color=#e07a6a]Requirement: {string.Join("\n", finalReqTexts)}[/color]";
		}
		infoText.Text = text;
	}

	private void _reset_info()
	{
		infoText.Text = "[center][color=gray]Hover over a node to see info[/color][/center]";
	}

	private static string _make_tooltip(GDict data)
	{
		var tt = data["title"].ToString();
		var desc = Get(data, "desc", "").ToString();
		if (desc != "")
			tt += "\n" + desc;

		var reqsText = Get(data, "reqs", new GArray()).ToString();
		if (reqsText != "" && reqsText != "[]")
			tt += "\n\n[ Requirements ]\n" + reqsText;

		var actionText = Get(data, "action", new GArray()).ToString();
		if (actionText != "" && actionText != "[]")
			tt += "\n\n[ On Finished ]\n" + actionText;
		return tt;
	}

	private static StyleBoxFlat Box(Color bg, Color border, int top, int bottom, int borderBottom)
	{
		var box = new StyleBoxFlat
		{
			BgColor = bg,
			BorderColor = border,
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = top > 0 ? 2 : 0,
			BorderWidthBottom = borderBottom,
			CornerRadiusTopLeft = top,
			CornerRadiusTopRight = top,
			CornerRadiusBottomLeft = bottom,
			CornerRadiusBottomRight = bottom,
		};
		box.SetContentMarginAll(6);
		return box;
	}

	private void _apply_node_style(GraphNode node, GDict data, CountryData player)
	{
		var body = node.GetNodeOrNull<Button>("Body");
		var id = data["id"].ToString();
		var decisions = DecisionManager.Instance;

		var status = "";
		var disabled = false;
		Color accent;

		if (Finished(player, id))
		{
			status = "✔ Completed";
			accent = new Color(0.4f, 0.78f, 0.42f);
			disabled = true;
		}
		else if (decisions.IsInProgress(player, id))
		{
			status = $"⌛ {decisions.GetDaysLeft(player, id)} days left";
			accent = new Color(0.4f, 0.62f, 0.95f);
			disabled = true;
		}
		else
		{
			var isMutuallyLocked = _as_array(Get(data, "exclusive"))
				.Any(exId => Finished(player, exId) || decisions.IsInProgress(player, exId.ToString()));
			var isPrereqLocked = _as_array(Get(data, "prereq")).Any(reqId => !Finished(player, reqId));

			if (isMutuallyLocked)
			{
				status = "✖ Excluded";
				accent = new Color(0.85f, 0.32f, 0.3f);
				disabled = true;
			}
			else if (isPrereqLocked)
			{
				status = "🔒 Needs prerequisite";
				accent = new Color(0.45f, 0.42f, 0.4f);
				disabled = true;
			}
			else
			{
				var costPp = data["cost_pp"].AsDouble();
				// Also check if another decision is already running
				disabled = player.politicalPower < costPp || decisions.IsCountryBusy(player);
				status = $"{(int)costPp} PP · {(int)data["days"].AsDouble()} days";
				accent = disabled ? new Color(0.6f, 0.55f, 0.45f) : new Color(0.9f, 0.72f, 0.36f);
			}
		}

		var dark = new Color(0.09f, 0.095f, 0.105f);
		if (body != null)
		{
			var normal = Box(dark.Lerp(accent, 0.12f), accent.Darkened(0.4f), 4, 4, 1);
			var hover = Box(dark.Lerp(accent, 0.28f), accent, 4, 4, 1);
			// Keep bodies clickable in edit mode so they can be selected for editing.
			body.Disabled = disabled && !editMode;
			body.Text = status;
			body.AddThemeStyleboxOverride("normal", normal);
			body.AddThemeStyleboxOverride("disabled", normal);
			body.AddThemeStyleboxOverride("hover", hover);
			body.AddThemeStyleboxOverride("pressed", Box(dark.Lerp(accent, 0.4f), accent, 4, 4, 1));
			body.AddThemeColorOverride("font_color", accent.Lightened(0.35f));
			body.AddThemeColorOverride("font_hover_color", Colors.White);
			body.AddThemeColorOverride("font_disabled_color", accent.Lightened(0.15f));
		}

		var panel = Box(dark, accent.Darkened(0.2f), 0, 6, 3);
		panel.ShadowColor = new Color(0, 0, 0, 0.55f);
		panel.ShadowSize = 8;
		panel.ShadowOffset = new Vector2(0, 3);
		var selected = (StyleBoxFlat)panel.Duplicate();
		selected.BorderColor = Colors.White;
		var title = Box(dark.Lerp(accent, 0.35f), accent.Darkened(0.2f), 6, 0, 0);
		var titleSelected = (StyleBoxFlat)title.Duplicate();
		titleSelected.BorderColor = Colors.White;

		node.Title = data["title"].ToString();
		node.AddThemeStyleboxOverride("panel", panel);
		node.AddThemeStyleboxOverride("panel_selected", selected);
		node.AddThemeStyleboxOverride("titlebar", title);
		node.AddThemeStyleboxOverride("titlebar_selected", titleSelected);
	}

	private void RestyleAll()
	{
		var player = Player;
		if (player == null)
			return;
		var nodes = _current_nodes();
		foreach (var node in graphNodes.Values)
		{
			if (!GodotObject.IsInstanceValid(node))
				continue;
			var idx = node.GetMeta("idx", -1).AsInt32();
			if (idx >= 0 && idx < nodes.Count)
				_apply_node_style(node, nodes[idx].AsGodotDictionary(), player);
		}
	}

	public void RefreshStatusOnly()
	{
		if (!Visible)
			return;
		RestyleAll();
		_update_connections();
		Callable.From(_refresh_port_colors).CallDeferred();
	}

	public void _on_reload_decisions_pressed()
	{
		var relPath = "res://starts/" + GameState.Instance.currentStart + "/decisions/";
		DecisionManager.Instance.LoadDecisionsFromPath(relPath);
		_load_category(currentCategory);
	}

	public void _on_edit_mode_toggled(bool toggledOn)
	{
		editMode = toggledOn && DebugMode;
		editingButtons.Visible = editMode;

		if (infoTab != null)
			infoTab.CurrentTab = editMode ? 1 : 0;

		foreach (var node in graphNodes.Values)
		{
			if (GodotObject.IsInstanceValid(node))
				node.Draggable = editMode;
		}
		RestyleAll();

		if (!editMode)
			_clear_selection();
		Callable.From(_refresh_port_colors).CallDeferred();
	}

	private void _clear_selection()
	{
		selectedDecision = new GDict();
		selectedNode = null;
		editLabel.Text = "";
		editDesc.Text = "";
		editDays.Value = 0;
		editPpcost.Value = 0;
		_load_expression_blocks(new GDict());
	}

	private void ShowInEditor(GDict data)
	{
		editLabel.Text = Get(data, "title", "").ToString();
		editDesc.Text = Get(data, "desc", "").ToString();
		editDays.Value = Get(data, "days", 0).AsDouble();
		editPpcost.Value = Get(data, "cost_pp", 0).AsDouble();
	}

	private void _select_node(GraphNode node)
	{
		if (!editMode || !GodotObject.IsInstanceValid(node))
			return;
		var data = _data_for_node(node);
		if (data.Count == 0 || (selectedNode == node && selectedDecision == data))
			return;

		selectedDecision = data;
		selectedNode = node;
		ShowInEditor(data);
		_load_expression_blocks(editFunction.ButtonPressed ? data : new GDict());
	}

	private static GArray LiveChildDicts(Node block)
		=> new(block.GetChildren()
			.Where(x => !x.IsQueuedForDeletion())
			.Select(x => ((ExpressionBox)x).ToDict()));

	private void StoreExpressions()
	{
		selectedDecision["reqs"] = LiveChildDicts(reqsBlock);
		selectedDecision["action"] = LiveChildDicts(finishedBlock);
		_update_node_visuals(selectedNode, selectedDecision);
	}

	public void _on_save_decisions_pressed()
	{
		if (selectedDecision.Count > 0 && GodotObject.IsInstanceValid(selectedNode))
			StoreExpressions();
		DecisionManager.Instance.SaveCountryDecisions(Player.countryName);
	}

	// --- GRAPH EDIT INTERACTIONS ---
	private void _on_graph_node_selected(Node node)
	{
		if (node is GraphNode graphNode)
			_select_node(graphNode);
	}

	private void _on_end_node_move()
	{
		if (!editMode)
			return;
		var nodes = _current_nodes();
		foreach (var child in treeGraph.GetChildren())
		{
			if (child is not GraphNode { Selected: true } graphNode)
				continue;
			var idx = graphNode.GetMeta("idx", -1).AsInt32();
			if (idx < 0 || idx >= nodes.Count)
				continue;
			var pos = (graphNode.PositionOffset / GRID_SIZE).Round() * GRID_SIZE;
			graphNode.PositionOffset = pos;
			nodes[idx].AsGodotDictionary()["pos"] = new GArray { pos.X, pos.Y };
		}
	}

	private void _on_connection_drag_started(StringName fromNode, long fromPort, bool isOutput)
	{
		// GraphEdit port indices overlap between sides; remember which side started the drag.
		_drag_origin = (fromNode, fromPort, isOutput);
	}

	private void _on_connection_request(StringName fromNode, long fromPort, StringName toNode, long toPort)
		=> HandleConnection(fromNode, fromPort, toNode, toPort, true);

	private void _on_disconnection_request(StringName fromNode, long fromPort, StringName toNode, long toPort)
		=> HandleConnection(fromNode, fromPort, toNode, toPort, false);

	private void HandleConnection(StringName fromNode, long fromPort, StringName toNode, long toPort, bool adding)
	{
		if (!editMode)
			return;
		var relation = _resolve_connection(fromNode, (int)fromPort, toNode, (int)toPort);
		_drag_origin = null;
		if (relation == null)
			return;
		var (sourceId, targetId, key) = relation.Value;
		_apply_relation(sourceId, targetId, key, adding);
	}

	private void _on_delete_nodes_request(Godot.Collections.Array<StringName> nodes)
	{
		if (!editMode || nodes.Count == 0)
			return;

		var removedIds = nodes.Select(n => _id_for_node_name(n)).Where(id => id != "").ToList();
		if (removedIds.Count == 0)
			return;

		var categories = DecisionManager.Instance.GetCountryCategoriesEditable(Player.countryName);
		var categoryNodes = Get(categories, currentCategory, new GArray()).AsGodotArray();
		foreach (var nodeVariant in categoryNodes)
		{
			var nodeData = nodeVariant.AsGodotDictionary();
			foreach (var key in new[] { "prereq", "exclusive" })
			{
				var values = _as_array(Get(nodeData, key));
				foreach (var removedId in removedIds)
					values.Remove(removedId);
				if (values.Count == 0)
					nodeData.Remove(key);
				else
					nodeData[key] = values;
			}
		}

		for (var i = categoryNodes.Count - 1; i >= 0; i--)
		{
			if (removedIds.Contains(categoryNodes[i].AsGodotDictionary()["id"].ToString()))
				categoryNodes.RemoveAt(i);
		}

		_clear_selection();
		_load_category(currentCategory);
	}

	private int _port_type(StringName nodeName, int portIdx, bool isOutput)
	{
		if (treeGraph.GetNodeOrNull(new NodePath(nodeName.ToString())) is not GraphNode node)
			return -1;
		if (isOutput)
		{
			if (portIdx < node.GetOutputPortCount())
				return node.GetOutputPortType(portIdx);
		}
		else if (portIdx < node.GetInputPortCount())
		{
			return node.GetInputPortType(portIdx);
		}
		return -1;
	}

	// Accepts both drag directions: output -> input and input -> output.
	private (string sourceId, string targetId, string key)? _resolve_connection(
		StringName fromNode, int fromPort, StringName toNode, int toPort)
	{
		var fromIsOutput = true;
		if (_drag_origin is { } origin && origin.node.ToString() == fromNode.ToString() && origin.port == fromPort)
			fromIsOutput = origin.isOutput;

		var fromType = _port_type(fromNode, fromPort, fromIsOutput);
		var toType = _port_type(toNode, toPort, !fromIsOutput);
		if (fromType < 0 || toType < 0)
			return null;

		StringName sourceName;
		StringName targetName;
		int portType;
		if (fromIsOutput)
		{
			if (toType != TYPE_INPUT)
				return null;
			sourceName = fromNode;
			targetName = toNode;
			portType = fromType;
		}
		else
		{
			if (fromType != TYPE_INPUT)
				return null;
			sourceName = toNode;
			targetName = fromNode;
			portType = toType;
		}

		if (sourceName == targetName)
			return null;
		string key;
		if (portType == TYPE_PREREQ)
			key = "prereq";
		else if (portType == TYPE_EXCLUSIVE)
			key = "exclusive";
		else
			return null;

		var sourceId = _id_for_node_name(sourceName);
		var targetId = _id_for_node_name(targetName);
		if (sourceId == "" || targetId == "")
			return null;
		return (sourceId, targetId, key);
	}

	private static void SetOrErase(GDict data, string key, GArray values)
	{
		if (values.Count == 0)
			data.Remove(key);
		else
			data[key] = values;
	}

	private void _apply_relation(string sourceId, string targetId, string key, bool adding)
	{
		var targetData = _data_for_id(targetId);
		var sourceData = _data_for_id(sourceId);
		if (targetData.Count == 0 || sourceData.Count == 0)
			return;

		var values = _as_array(Get(targetData, key));
		if (adding)
		{
			if (!values.Contains(sourceId))
				values.Add(sourceId);
		}
		else
		{
			values.Remove(sourceId);
		}
		SetOrErase(targetData, key, values);

		// A relation is always either a prerequisite or an exclusion, never both.
		var otherKey = key == "prereq" ? "exclusive" : "prereq";
		_erase_relation(targetData, otherKey, sourceId);
		_erase_relation(sourceData, otherKey, targetId);

		// Keep mutual exclusion symmetrical so both nodes show the lock.
		if (key == "exclusive")
		{
			var reverse = _as_array(Get(sourceData, "exclusive"));
			if (adding)
			{
				if (!reverse.Contains(targetId))
					reverse.Add(targetId);
			}
			else
			{
				reverse.Remove(targetId);
			}
			SetOrErase(sourceData, "exclusive", reverse);
		}

		_update_connections();
	}

	private static void _erase_relation(GDict data, string key, string id)
	{
		var values = _as_array(Get(data, key));
		values.Remove(id);
		SetOrErase(data, key, values);
	}

	// --- EDITING HELPERS ---
	private GArray _category_nodes(string category)
		=> Get(DecisionManager.Instance.GetCountryCategories(Player.countryName), category, new GArray()).AsGodotArray();

	private GArray _current_nodes() => _category_nodes(currentCategory);

	private GDict _data_for_node(GraphNode node)
	{
		var nodes = _current_nodes();
		var idx = node.GetMeta("idx", -1).AsInt32();
		return idx >= 0 && idx < nodes.Count ? nodes[idx].AsGodotDictionary() : new GDict();
	}

	private GDict _data_for_id(string id)
	{
		foreach (var nodeData in _current_nodes())
		{
			var dict = nodeData.AsGodotDictionary();
			if (dict["id"].ToString() == id)
				return dict;
		}
		return new GDict();
	}

	private string _id_for_node_name(StringName nodeName)
	{
		var wanted = nodeName.ToString();
		foreach (var node in graphNodes.Values)
		{
			if (GodotObject.IsInstanceValid(node) && node.Name.ToString() == wanted)
				return node.GetMeta("id", "").ToString();
		}
		return "";
	}

	private static StringName _node_name(int idx) => new($"Decision_{idx}");

	// Note: an Array value is returned as-is (same reference), like the GDScript original.
	private static GArray _as_array(Variant value)
	{
		if (value.VariantType == Variant.Type.Array)
			return value.AsGodotArray();
		if (value.VariantType == Variant.Type.Nil || (value.VariantType == Variant.Type.String && value.AsString() == ""))
			return new GArray();
		return new GArray { value };
	}

	public void _on_add_decision_pressed()
	{
		var player = Player;
		var countryCats = DecisionManager.Instance.GetCountryCategoriesEditable(player.countryName);

		if (!countryCats.ContainsKey(currentCategory))
			countryCats[currentCategory] = new GArray();

		var nodes = countryCats[currentCategory].AsGodotArray();
		var id = $"{currentCategory}_{nodes.Count}#{GD.RandRange(0, 100)}";
		var newDecision = new GDict
		{
			{ "id", id },
			{ "title", id },
			{ "pos", new GArray { treeGraph.ScrollOffset.X + 100.0f, treeGraph.ScrollOffset.Y + 150.0f } },
			{ "days", 20.0 },
			{ "cost_pp", 25.0 },
			{ "reqs", new GArray() },
			{ "action", new GArray() },
			{ "desc", "" },
		};
		nodes.Add(newDecision);
		var btn = _create_node(newDecision, nodes.Count - 1, player);

		// Automatically select the new decision
		selectedDecision = newDecision;
		selectedNode = btn;
		ShowInEditor(newDecision);
		_load_expression_blocks(newDecision);

		_update_connections();
		Callable.From(_refresh_port_colors).CallDeferred();
		_on_save_decisions_pressed();
	}

	private void _load_expression_blocks(GDict data)
	{
		_is_loading_expr = true;
		foreach (var block in new Node[] { reqsBlock, finishedBlock })
		{
			foreach (var c in block.GetChildren())
			{
				block.RemoveChild(c);
				c.QueueFree();
			}
		}

		foreach (var req in _as_array(Get(data, "reqs")))
		{
			var expr = ExpressionBox.FromDict(req, true);
			reqsBlock.AddChild(expr);
			expr.changed += _sync_expressions;
		}

		foreach (var act in _as_array(Get(data, "action")))
		{
			var expr = ExpressionBox.FromDict(act, true);
			finishedBlock.AddChild(expr);
			expr.changed += _sync_expressions;
		}
		_is_loading_expr = false;
	}

	public void _on_add_req_pressed() => _add_expr_to_block(reqsBlock);

	public void _on_add_finished_pressed() => _add_expr_to_block(finishedBlock);

	private void _add_expr_to_block(Control block)
	{
		if (selectedDecision.Count == 0)
			return;
		var expr = ExpressionBox.Instantiate();
		expr.isChild = true;
		block.AddChild(expr);
		expr.changed += _sync_expressions;
		_sync_expressions();
	}

	private void _sync_expressions()
	{
		if (_is_loading_expr || selectedDecision.Count == 0 || !GodotObject.IsInstanceValid(selectedNode))
			return;
		StoreExpressions();
		DecisionManager.Instance.SaveCountryDecisions(Player.countryName);
	}

	private bool HasSelection => selectedDecision.Count > 0 && GodotObject.IsInstanceValid(selectedNode);

	public void _on_edit_label_text_changed()
	{
		if (!HasSelection)
			return;
		selectedDecision["title"] = editLabel.Text;
		_update_node_visuals(selectedNode, selectedDecision);
	}

	public void _on_edit_desc_text_changed()
	{
		if (!HasSelection)
			return;
		selectedDecision["desc"] = editDesc.Text;
		_update_node_visuals(selectedNode, selectedDecision);
	}

	public void _on_edit_days_value_changed(float value)
	{
		if (!HasSelection)
			return;
		selectedDecision["days"] = (int)value;
		_update_node_visuals(selectedNode, selectedDecision);
	}

	public void _on_edit_ppcost_value_changed(float value)
	{
		if (!HasSelection)
			return;
		selectedDecision["cost_pp"] = (int)value;
		_update_node_visuals(selectedNode, selectedDecision);
	}

	private void _update_node_visuals(GraphNode node, GDict data)
	{
		_apply_node_style(node, data, Player);

		var tt = _make_tooltip(data);
		var body = node.GetNodeOrNull<Button>("Body");
		if (body != null)
			body.TooltipText = tt;
		node.TooltipText = tt;
	}

	public void _on_edit_func_toggled(bool toggledOn)
	{
		addReq.Visible = toggledOn;
		addFinished.Visible = toggledOn;
		editorTab.Visible = toggledOn;
		if (toggledOn && selectedDecision.Count > 0)
			_load_expression_blocks(selectedDecision);
		else if (!toggledOn)
			_load_expression_blocks(new GDict());
	}
}
