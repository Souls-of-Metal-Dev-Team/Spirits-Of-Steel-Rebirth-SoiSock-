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
	private GraphEdit tree_graph;
	private TabBar tabs_container;
	[Export] public RichTextLabel info_text;
	[Export] public TabContainer info_tab;
	[Export] public TextEdit edit_label;
	[Export] public TextEdit edit_desc;
	[Export] public SpinBox edit_days;
	[Export] public SpinBox edit_ppcost;
	[Export] public Button close_button;
	[Export] public Button edit_button;
	[Export] public HBoxContainer editing_buttons;
	[Export] public CheckBox edit_function;
	[Export] public Button save_button;
	[Export] public Button add_decision;
	[Export] public Button add_req;
	[Export] public Button add_finished;
	[Export] public TabContainer editor_tab;
	[Export] public VBoxContainer reqs_block;
	[Export] public VBoxContainer finished_block;

	public string current_category = "Economy";
	private readonly Dictionary<string, GraphNode> graph_nodes = new(); // decision id -> GraphNode
	public bool edit_mode = false;
	private GDict selected_decision = new();
	private GraphNode selected_node = null;
	private bool _is_loading_expr = false;
	private (StringName node, long port, bool is_output)? _drag_origin = null;

	private static bool DebugMode
		=> SettingsManager.Instance.settings.GetValue("Developer Options", "debug_mode").AsBool();

	private static CountryData Player => CountryManager.Instance.Player;

	private static bool Finished(CountryData player, Variant id) => player.HasMeta("finished_" + id.ToString());

	public override void _Ready()
	{
		tree_graph = GetNode<GraphEdit>("CanvasAnchor/TreeGraph");
		tabs_container = GetNode<TabBar>("StaticUI/Header/TabsContainer");

		DecisionManager.Instance.ui_overlay = this;
		edit_button.Visible = DebugMode;

		tree_graph.AddValidConnectionType(TYPE_PREREQ, TYPE_INPUT);
		tree_graph.AddValidConnectionType(TYPE_INPUT, TYPE_PREREQ);
		tree_graph.AddValidConnectionType(TYPE_EXCLUSIVE, TYPE_INPUT);
		tree_graph.AddValidConnectionType(TYPE_INPUT, TYPE_EXCLUSIVE);
		tree_graph.ConnectionDragStarted += _on_connection_drag_started;
		tree_graph.ConnectionRequest += _on_connection_request;
		tree_graph.DisconnectionRequest += _on_disconnection_request;
		tree_graph.DeleteNodesRequest += _on_delete_nodes_request;
		tree_graph.EndNodeMove += _on_end_node_move;
		tree_graph.NodeSelected += _on_graph_node_selected;

		_on_edit_mode_toggled(false);
		tabs_container.TabChanged += tab => _on_tab_changed((int)tab);
	}

	// --- LOGIC ---
	public void OpenMenu()
	{
		Show();
		_rebuild_tabs();
		_load_category(current_category);
		GameState.Instance.decision_menu_open = true;
		// Editing is only available in debug mode
		edit_button.Visible = DebugMode;
		if (!DebugMode)
			edit_button.SetPressedNoSignal(false);
		_on_edit_mode_toggled(edit_button.ButtonPressed);
		_toggle_pause(true);
	}

	public void CloseMenu()
	{
		Hide();
		GameState.Instance.decision_menu_open = false;
		_toggle_pause(false);
	}

	private void _toggle_pause(bool pause)
	{
		var world = GameState.Instance.current_world;
		if (world != null)
		{
			world.SetProcess(!pause);
			world.clock?.SetProcess(!pause);
			world.FindChild("CameraController", true, false)?.SetProcess(!pause);
		}
		TroopManager.Instance.SetProcess(!pause);
	}

	private List<string> CategoryNames()
		=> DecisionManager.Instance.get_country_categories(Player.country_name).Keys.Select(k => k.AsString()).ToList();

	private void _rebuild_tabs()
	{
		while (tabs_container.TabCount > 0)
			tabs_container.RemoveTab(0);

		var categories = CategoryNames();
		for (var i = 0; i < categories.Count; i++)
		{
			tabs_container.AddTab(categories[i].ToUpper());
			if (categories[i] == current_category)
				tabs_container.CurrentTab = i;
		}
	}

	private void _on_tab_changed(int tab_idx)
	{
		var categories = CategoryNames();
		if (tab_idx < categories.Count)
		{
			current_category = categories[tab_idx];
			_load_category(current_category);
		}
	}

	private void _load_category(string cat_name)
	{
		foreach (var c in tree_graph.GetChildren())
		{
			if (c is GraphNode)
			{
				// Remove immediately so new nodes can reuse the same names.
				tree_graph.RemoveChild(c);
				c.QueueFree();
			}
		}
		tree_graph.ClearConnections();
		graph_nodes.Clear();
		_clear_selection();

		var player = Player;
		var nodes = _category_nodes(cat_name);
		for (var i = 0; i < nodes.Count; i++)
			_create_node(nodes[i].AsGodotDictionary(), i, player);

		_update_connections();
		Callable.From(_refresh_port_colors).CallDeferred();
	}

	private void _update_connections()
	{
		tree_graph.ClearConnections();
		var nodes = _current_nodes();

		var name_by_id = new Dictionary<string, StringName>();
		for (var i = 0; i < nodes.Count; i++)
			name_by_id[nodes[i].AsGodotDictionary()["id"].ToString()] = _node_name(i);

		// Prerequisites point from the required decision to the dependent decision.
		foreach (var nodeVariant in nodes)
		{
			var node = nodeVariant.AsGodotDictionary();
			if (!name_by_id.TryGetValue(node["id"].ToString(), out var target_name))
				continue;
			foreach (var req_id in _as_array(Get(node, "prereq")))
			{
				if (name_by_id.TryGetValue(req_id.ToString(), out var source_name) && source_name != target_name)
					tree_graph.ConnectNode(source_name, PORT_PREREQ_OUT, target_name, PORT_INPUT, true);
			}
		}

		// Exclusive edges are drawn once, between the lexicographically lower and higher ids.
		var drawn_exclusives = new HashSet<string>();
		foreach (var nodeVariant in nodes)
		{
			var node = nodeVariant.AsGodotDictionary();
			var node_id = node["id"].ToString();
			foreach (var ex_id in _as_array(Get(node, "exclusive")))
			{
				var other_id = ex_id.ToString();
				if (other_id == node_id)
					continue;
				var lower = string.CompareOrdinal(node_id, other_id) < 0;
				var low_id = lower ? node_id : other_id;
				var high_id = lower ? other_id : node_id;
				if (!drawn_exclusives.Add(low_id + "|" + high_id))
					continue;

				if (name_by_id.TryGetValue(low_id, out var source_name) && name_by_id.TryGetValue(high_id, out var target_name))
					tree_graph.ConnectNode(source_name, PORT_EXCLUSIVE_OUT, target_name, PORT_INPUT, true);
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
			Draggable = edit_mode,
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
			if (edit_mode)
				_select_node(node);
			else
				DecisionManager.Instance.start_decision(player, current_category, idx);
		};
		node.AddChild(body);

		node.AddChild(PortLabel("PrereqPort", "REQ"));
		node.AddChild(PortLabel("ExclusivePort", "EX"));

		// GraphNode builds its port cache on ready, so slots are set up then.
		node.Connect(Node.SignalName.Ready, Callable.From(() => _setup_node_slots(node)), (uint)ConnectFlags.OneShot);
		tree_graph.AddChild(node);
		_apply_node_style(node, data, player);

		graph_nodes[data["id"].ToString()] = node;
		return node;
	}

	private void _setup_node_slots(GraphNode node)
	{
		if (!GodotObject.IsInstanceValid(node))
			return;
		var data = _data_for_node(node);
		if (data.Count == 0)
			return;

		var prereq_color = Finished(Player, data["id"]) ? COL_LINE_ACTIVE : COL_LINE_INACTIVE;
		node.SetSlot(SLOT_BODY, true, TYPE_INPUT, COL_LINE_INACTIVE, false, 0, Colors.White);
		node.SetSlot(SLOT_PREREQ, false, 0, Colors.White, true, TYPE_PREREQ, prereq_color);
		node.SetSlot(SLOT_EXCLUSIVE, false, 0, Colors.White, true, TYPE_EXCLUSIVE, COL_LINE_EXCLUSIVE);
	}

	private void _refresh_port_colors()
	{
		var player = Player;
		foreach (var node in graph_nodes.Values)
		{
			if (!GodotObject.IsInstanceValid(node) || node.GetOutputPortCount() < 2)
				continue;
			var data = _data_for_node(node);
			if (data.Count == 0)
				continue;
			var prereq_color = Finished(player, data["id"]) ? COL_LINE_ACTIVE : COL_LINE_INACTIVE;
			node.SetSlotColorLeft(SLOT_BODY, COL_LINE_INACTIVE);
			node.SetSlotColorRight(SLOT_PREREQ, prereq_color);
			node.SetSlotColorRight(SLOT_EXCLUSIVE, COL_LINE_EXCLUSIVE);
		}
	}

	private void _show_info(GDict data)
	{
		var text = $"[b][font_size=26][color=yellow]{data["title"]}[/color][/font_size][/b]\n";
		text += $"[font_size=18][i]{Get(data, "desc", "")}[/i][/font_size]\n\n";
		text += $"[color=orange]Time: {(int)data["days"].AsDouble()} Days | Cost: {(int)data["cost_pp"].AsDouble()} Political Power[/color]";

		if (data.TryGetValue("reqs", out var reqsVariant))
		{
			var final_req_texts = new List<string>();
			foreach (var reqVariant in _as_array(reqsVariant))
			{
				if (reqVariant.VariantType != Variant.Type.Dictionary)
					continue;
				var req = reqVariant.AsGodotDictionary();
				var args = Get(req, "args", new GArray());
				var req_line = Get(req, "func", "Unknown").ToString().Capitalize();
				if (args.VariantType == Variant.Type.Array && args.AsGodotArray().Count > 0)
					req_line += ": " + string.Join(", ", args.AsGodotArray().Select(a => a.ToString().Capitalize()));
				final_req_texts.Add(req_line);
			}

			if (final_req_texts.Count > 0)
				text += $"\n[color=red]Requirement: {string.Join("\n", final_req_texts)}[/color]";
		}
		info_text.Text = text;
	}

	private void _reset_info()
	{
		info_text.Text = "[center][color=gray]Hover over a node to see info[/color][/center]";
	}

	private static string _make_tooltip(GDict data)
	{
		var tt = data["title"].ToString();
		var desc = Get(data, "desc", "").ToString();
		if (desc != "")
			tt += "\n" + desc;

		var reqs_text = Get(data, "reqs", new GArray()).ToString();
		if (reqs_text != "" && reqs_text != "[]")
			tt += "\n\n[ Requirements ]\n" + reqs_text;

		var action_text = Get(data, "action", new GArray()).ToString();
		if (action_text != "" && action_text != "[]")
			tt += "\n\n[ On Finished ]\n" + action_text;
		return tt;
	}

	private void _apply_node_style(GraphNode node, GDict data, CountryData player)
	{
		var body = node.GetNodeOrNull<Button>("Body");
		var id = data["id"].ToString();
		var decisions = DecisionManager.Instance;

		var status = "";
		var disabled = false;
		var style = new StyleBoxFlat
		{
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			BorderWidthBottom = 4,
		};

		if (Finished(player, id))
		{
			status = "[DONE]";
			style.BgColor = new Color(0.1f, 0.4f, 0.1f);
			disabled = true;
		}
		else if (decisions.is_in_progress(player, id))
		{
			status = $"⌛ {decisions.get_days_left(player, id)} Days";
			style.BgColor = new Color(0.1f, 0.2f, 0.5f); // Dark Blue
			disabled = true;
		}
		else
		{
			var is_mutually_locked = _as_array(Get(data, "exclusive"))
				.Any(ex_id => Finished(player, ex_id) || decisions.is_in_progress(player, ex_id.ToString()));
			var is_prereq_locked = _as_array(Get(data, "prereq")).Any(req_id => !Finished(player, req_id));

			if (is_mutually_locked)
			{
				status = "[LOCKED]";
				style.BgColor = new Color(0.4f, 0.1f, 0.1f);
				disabled = true;
			}
			else if (is_prereq_locked)
			{
				style.BgColor = new Color(0.241f, 0.102f, 0.101f, 1.0f);
				disabled = true;
			}
			else
			{
				var cost_pp = data["cost_pp"].AsDouble();
				status = $"{(int)cost_pp} PP";
				style.BgColor = new Color(0.2f, 0.2f, 0.2f);
				// Also check if another decision is already running
				disabled = player.political_power < cost_pp || decisions.is_country_busy(player);
			}
		}

		if (body != null)
		{
			// Keep bodies clickable in edit mode so they can be selected for editing.
			body.Disabled = disabled && !edit_mode;
			body.Text = status;
			body.AddThemeStyleboxOverride("normal", style);
			body.AddThemeStyleboxOverride("disabled", style);
			body.AddThemeStyleboxOverride("hover", style);
			body.AddThemeStyleboxOverride("pressed", style);
		}

		var title_style = (StyleBoxFlat)style.Duplicate();
		title_style.BorderWidthBottom = 0;
		node.Title = data["title"].ToString();
		node.AddThemeStyleboxOverride("panel", style);
		node.AddThemeStyleboxOverride("panel_selected", style);
		node.AddThemeStyleboxOverride("titlebar", title_style);
		node.AddThemeStyleboxOverride("titlebar_selected", title_style);
	}

	private void RestyleAll()
	{
		var player = Player;
		if (player == null)
			return;
		var nodes = _current_nodes();
		foreach (var node in graph_nodes.Values)
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
		var rel_path = "res://starts/" + GameState.Instance.current_start + "/decisions/";
		DecisionManager.Instance.load_decisions_from_path(rel_path);
		_load_category(current_category);
	}

	public void _on_edit_mode_toggled(bool toggled_on)
	{
		edit_mode = toggled_on && DebugMode;
		editing_buttons.Visible = edit_mode;

		if (info_tab != null)
			info_tab.CurrentTab = edit_mode ? 1 : 0;

		foreach (var node in graph_nodes.Values)
		{
			if (GodotObject.IsInstanceValid(node))
				node.Draggable = edit_mode;
		}
		RestyleAll();

		if (!edit_mode)
			_clear_selection();
		Callable.From(_refresh_port_colors).CallDeferred();
	}

	private void _clear_selection()
	{
		selected_decision = new GDict();
		selected_node = null;
		edit_label.Text = "";
		edit_desc.Text = "";
		edit_days.Value = 0;
		edit_ppcost.Value = 0;
		_load_expression_blocks(new GDict());
	}

	private void ShowInEditor(GDict data)
	{
		edit_label.Text = Get(data, "title", "").ToString();
		edit_desc.Text = Get(data, "desc", "").ToString();
		edit_days.Value = Get(data, "days", 0).AsDouble();
		edit_ppcost.Value = Get(data, "cost_pp", 0).AsDouble();
	}

	private void _select_node(GraphNode node)
	{
		if (!edit_mode || !GodotObject.IsInstanceValid(node))
			return;
		var data = _data_for_node(node);
		if (data.Count == 0 || (selected_node == node && selected_decision == data))
			return;

		selected_decision = data;
		selected_node = node;
		ShowInEditor(data);
		_load_expression_blocks(edit_function.ButtonPressed ? data : new GDict());
	}

	private static GArray LiveChildDicts(Node block)
		=> new(block.GetChildren()
			.Where(x => !x.IsQueuedForDeletion())
			.Select(x => ((ExpressionBox)x).ToDict()));

	private void StoreExpressions()
	{
		selected_decision["reqs"] = LiveChildDicts(reqs_block);
		selected_decision["action"] = LiveChildDicts(finished_block);
		_update_node_visuals(selected_node, selected_decision);
	}

	public void _on_save_decisions_pressed()
	{
		if (selected_decision.Count > 0 && GodotObject.IsInstanceValid(selected_node))
			StoreExpressions();
		DecisionManager.Instance.save_country_decisions(Player.country_name);
	}

	// --- GRAPH EDIT INTERACTIONS ---
	private void _on_graph_node_selected(Node node)
	{
		if (node is GraphNode graph_node)
			_select_node(graph_node);
	}

	private void _on_end_node_move()
	{
		if (!edit_mode)
			return;
		var nodes = _current_nodes();
		foreach (var child in tree_graph.GetChildren())
		{
			if (child is not GraphNode { Selected: true } graph_node)
				continue;
			var idx = graph_node.GetMeta("idx", -1).AsInt32();
			if (idx < 0 || idx >= nodes.Count)
				continue;
			var pos = (graph_node.PositionOffset / GRID_SIZE).Round() * GRID_SIZE;
			graph_node.PositionOffset = pos;
			nodes[idx].AsGodotDictionary()["pos"] = new GArray { pos.X, pos.Y };
		}
	}

	private void _on_connection_drag_started(StringName from_node, long from_port, bool is_output)
	{
		// GraphEdit port indices overlap between sides; remember which side started the drag.
		_drag_origin = (from_node, from_port, is_output);
	}

	private void _on_connection_request(StringName from_node, long from_port, StringName to_node, long to_port)
		=> HandleConnection(from_node, from_port, to_node, to_port, true);

	private void _on_disconnection_request(StringName from_node, long from_port, StringName to_node, long to_port)
		=> HandleConnection(from_node, from_port, to_node, to_port, false);

	private void HandleConnection(StringName from_node, long from_port, StringName to_node, long to_port, bool adding)
	{
		if (!edit_mode)
			return;
		var relation = _resolve_connection(from_node, (int)from_port, to_node, (int)to_port);
		_drag_origin = null;
		if (relation == null)
			return;
		var (source_id, target_id, key) = relation.Value;
		_apply_relation(source_id, target_id, key, adding);
	}

	private void _on_delete_nodes_request(Godot.Collections.Array<StringName> nodes)
	{
		if (!edit_mode || nodes.Count == 0)
			return;

		var removed_ids = nodes.Select(n => _id_for_node_name(n)).Where(id => id != "").ToList();
		if (removed_ids.Count == 0)
			return;

		var categories = DecisionManager.Instance.get_country_categories_editable(Player.country_name);
		var category_nodes = Get(categories, current_category, new GArray()).AsGodotArray();
		foreach (var nodeVariant in category_nodes)
		{
			var node_data = nodeVariant.AsGodotDictionary();
			foreach (var key in new[] { "prereq", "exclusive" })
			{
				var values = _as_array(Get(node_data, key));
				foreach (var removed_id in removed_ids)
					values.Remove(removed_id);
				if (values.Count == 0)
					node_data.Remove(key);
				else
					node_data[key] = values;
			}
		}

		for (var i = category_nodes.Count - 1; i >= 0; i--)
		{
			if (removed_ids.Contains(category_nodes[i].AsGodotDictionary()["id"].ToString()))
				category_nodes.RemoveAt(i);
		}

		_clear_selection();
		_load_category(current_category);
	}

	private int _port_type(StringName node_name, int port_idx, bool is_output)
	{
		if (tree_graph.GetNodeOrNull(new NodePath(node_name.ToString())) is not GraphNode node)
			return -1;
		if (is_output)
		{
			if (port_idx < node.GetOutputPortCount())
				return node.GetOutputPortType(port_idx);
		}
		else if (port_idx < node.GetInputPortCount())
		{
			return node.GetInputPortType(port_idx);
		}
		return -1;
	}

	// Accepts both drag directions: output -> input and input -> output.
	private (string source_id, string target_id, string key)? _resolve_connection(
		StringName from_node, int from_port, StringName to_node, int to_port)
	{
		var from_is_output = true;
		if (_drag_origin is { } origin && origin.node.ToString() == from_node.ToString() && origin.port == from_port)
			from_is_output = origin.is_output;

		var from_type = _port_type(from_node, from_port, from_is_output);
		var to_type = _port_type(to_node, to_port, !from_is_output);
		if (from_type < 0 || to_type < 0)
			return null;

		StringName source_name;
		StringName target_name;
		int port_type;
		if (from_is_output)
		{
			if (to_type != TYPE_INPUT)
				return null;
			source_name = from_node;
			target_name = to_node;
			port_type = from_type;
		}
		else
		{
			if (from_type != TYPE_INPUT)
				return null;
			source_name = to_node;
			target_name = from_node;
			port_type = to_type;
		}

		if (source_name == target_name)
			return null;
		string key;
		if (port_type == TYPE_PREREQ)
			key = "prereq";
		else if (port_type == TYPE_EXCLUSIVE)
			key = "exclusive";
		else
			return null;

		var source_id = _id_for_node_name(source_name);
		var target_id = _id_for_node_name(target_name);
		if (source_id == "" || target_id == "")
			return null;
		return (source_id, target_id, key);
	}

	private static void SetOrErase(GDict data, string key, GArray values)
	{
		if (values.Count == 0)
			data.Remove(key);
		else
			data[key] = values;
	}

	private void _apply_relation(string source_id, string target_id, string key, bool adding)
	{
		var target_data = _data_for_id(target_id);
		var source_data = _data_for_id(source_id);
		if (target_data.Count == 0 || source_data.Count == 0)
			return;

		var values = _as_array(Get(target_data, key));
		if (adding)
		{
			if (!values.Contains(source_id))
				values.Add(source_id);
		}
		else
		{
			values.Remove(source_id);
		}
		SetOrErase(target_data, key, values);

		// A relation is always either a prerequisite or an exclusion, never both.
		var other_key = key == "prereq" ? "exclusive" : "prereq";
		_erase_relation(target_data, other_key, source_id);
		_erase_relation(source_data, other_key, target_id);

		// Keep mutual exclusion symmetrical so both nodes show the lock.
		if (key == "exclusive")
		{
			var reverse = _as_array(Get(source_data, "exclusive"));
			if (adding)
			{
				if (!reverse.Contains(target_id))
					reverse.Add(target_id);
			}
			else
			{
				reverse.Remove(target_id);
			}
			SetOrErase(source_data, "exclusive", reverse);
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
		=> Get(DecisionManager.Instance.get_country_categories(Player.country_name), category, new GArray()).AsGodotArray();

	private GArray _current_nodes() => _category_nodes(current_category);

	private GDict _data_for_node(GraphNode node)
	{
		var nodes = _current_nodes();
		var idx = node.GetMeta("idx", -1).AsInt32();
		return idx >= 0 && idx < nodes.Count ? nodes[idx].AsGodotDictionary() : new GDict();
	}

	private GDict _data_for_id(string id)
	{
		foreach (var node_data in _current_nodes())
		{
			var dict = node_data.AsGodotDictionary();
			if (dict["id"].ToString() == id)
				return dict;
		}
		return new GDict();
	}

	private string _id_for_node_name(StringName node_name)
	{
		var wanted = node_name.ToString();
		foreach (var node in graph_nodes.Values)
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
		var country_cats = DecisionManager.Instance.get_country_categories_editable(player.country_name);

		if (!country_cats.ContainsKey(current_category))
			country_cats[current_category] = new GArray();

		var nodes = country_cats[current_category].AsGodotArray();
		var id = $"{current_category}_{nodes.Count}#{GD.RandRange(0, 100)}";
		var new_decision = new GDict
		{
			{ "id", id },
			{ "title", id },
			{ "pos", new GArray { tree_graph.ScrollOffset.X + 100.0f, tree_graph.ScrollOffset.Y + 150.0f } },
			{ "days", 20.0 },
			{ "cost_pp", 25.0 },
			{ "reqs", new GArray() },
			{ "action", new GArray() },
			{ "desc", "" },
		};
		nodes.Add(new_decision);
		var btn = _create_node(new_decision, nodes.Count - 1, player);

		// Automatically select the new decision
		selected_decision = new_decision;
		selected_node = btn;
		ShowInEditor(new_decision);
		_load_expression_blocks(new_decision);

		_update_connections();
		Callable.From(_refresh_port_colors).CallDeferred();
		_on_save_decisions_pressed();
	}

	private void _load_expression_blocks(GDict data)
	{
		_is_loading_expr = true;
		foreach (var block in new Node[] { reqs_block, finished_block })
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
			reqs_block.AddChild(expr);
			expr.changed += _sync_expressions;
		}

		foreach (var act in _as_array(Get(data, "action")))
		{
			var expr = ExpressionBox.FromDict(act, true);
			finished_block.AddChild(expr);
			expr.changed += _sync_expressions;
		}
		_is_loading_expr = false;
	}

	public void _on_add_req_pressed() => _add_expr_to_block(reqs_block);

	public void _on_add_finished_pressed() => _add_expr_to_block(finished_block);

	private void _add_expr_to_block(Control block)
	{
		if (selected_decision.Count == 0)
			return;
		var expr = ExpressionBox.Instantiate();
		expr.is_child = true;
		block.AddChild(expr);
		expr.changed += _sync_expressions;
		_sync_expressions();
	}

	private void _sync_expressions()
	{
		if (_is_loading_expr || selected_decision.Count == 0 || !GodotObject.IsInstanceValid(selected_node))
			return;
		StoreExpressions();
		DecisionManager.Instance.save_country_decisions(Player.country_name);
	}

	private bool HasSelection => selected_decision.Count > 0 && GodotObject.IsInstanceValid(selected_node);

	public void _on_edit_label_text_changed()
	{
		if (!HasSelection)
			return;
		selected_decision["title"] = edit_label.Text;
		_update_node_visuals(selected_node, selected_decision);
	}

	public void _on_edit_desc_text_changed()
	{
		if (!HasSelection)
			return;
		selected_decision["desc"] = edit_desc.Text;
		_update_node_visuals(selected_node, selected_decision);
	}

	public void _on_edit_days_value_changed(float value)
	{
		if (!HasSelection)
			return;
		selected_decision["days"] = (int)value;
		_update_node_visuals(selected_node, selected_decision);
	}

	public void _on_edit_ppcost_value_changed(float value)
	{
		if (!HasSelection)
			return;
		selected_decision["cost_pp"] = (int)value;
		_update_node_visuals(selected_node, selected_decision);
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

	public void _on_edit_func_toggled(bool toggled_on)
	{
		add_req.Visible = toggled_on;
		add_finished.Visible = toggled_on;
		editor_tab.Visible = toggled_on;
		if (toggled_on && selected_decision.Count > 0)
			_load_expression_blocks(selected_decision);
		else if (!toggled_on)
			_load_expression_blocks(new GDict());
	}
}
