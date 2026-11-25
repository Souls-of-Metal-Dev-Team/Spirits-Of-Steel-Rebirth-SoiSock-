using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

public partial class EventManager : Node
{
	public static EventManager Instance => Autoload.Get<EventManager>("EventManager");

	// MusicManager.SFX enum order (POPUP = 8). GDScript enum constants are not
	// object properties, so use the numeric value as EconomyManager does.
	private const int SfxPopup = 8;

	public Godot.Collections.Array<Node> active_dialogs = new();
	public Godot.Collections.Array scheduled_events = new();
	public Godot.Collections.Array super_events = new();

	public CanvasLayer ui_layer = new CanvasLayer();

	public override void _Ready()
	{
		ui_layer.Layer = 100;
		AddChild(ui_layer);

		var vp = GetViewport();
		if (vp != null)
			vp.GuiEmbedSubwindows = true;

		WarManager.Instance.WarDeclared += (warer, waree) =>
			show_alert(new Godot.Collections.Dictionary
			{
				{ "event", "war" },
				{ "c1", warer },
				{ "c2", waree },
			});
		load_super_events("res://superevents.json");
	}

	// UNIFIED ENTRY POINT
	public void show_alert(Godot.Collections.Dictionary data)
	{
		Window dialog;
		switch (GetOrDefault(data, "event", "default").AsString())
		{
			case "super":
				dialog = _create_super_event_dialog(data);
				break;
			case "custom":
				dialog = _create_custom_event_dialog(data);
				break;
			default:
				dialog = _create_default_alert_dialog(data);
				break;
		}
		_add_dialog(dialog);
	}

	public void _add_dialog(Window dialog)
	{
		ui_layer.AddChild(dialog);
		active_dialogs.Add(dialog);
		_setup_middle_click_close(dialog);
		dialog.Connect("close_requested", Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(dialog))
				dialog.QueueFree();
		}));
		dialog.Connect("tree_exited", Callable.From(() => active_dialogs.Remove(dialog)));
		dialog.PopupCentered();
	}

	public void _setup_middle_click_close(Window dialog)
	{
		dialog.Connect("window_input", Callable.From<InputEvent>((inputEvent) =>
		{
			if (inputEvent is InputEventMouseButton button && button.ButtonIndex == MouseButton.Middle && button.Pressed)
			{
				if (GodotObject.IsInstanceValid(dialog))
					dialog.QueueFree();
			}
		}));
		dialog.SetProcessInput(true);
		var timer = new Timer { WaitTime = 0.05, Autostart = true };
		timer.Connect("timeout", Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(dialog))
				return;
			if (dialog.Visible && Input.IsMouseButtonPressed(MouseButton.Middle))
			{
				var mp = dialog.GetMousePosition();
				if (new Rect2(Vector2.Zero, new Vector2(dialog.Size.X, dialog.Size.Y)).HasPoint(mp))
					dialog.QueueFree();
			}
		}));
		dialog.AddChild(timer);
	}

	public Window _create_default_alert_dialog(Godot.Collections.Dictionary data)
	{
		var dialog = new Window();
		dialog.Title = _get_default_title(data);
		dialog.MinSize = new Vector2I(320, 140);
		dialog.Size = new Vector2I(340, 160);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		dialog.AddChild(margin);

		var body = new VBoxContainer();
		body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		body.AddThemeConstantOverride("separation", 8);
		margin.AddChild(body);

		var c1 = GetOrDefault(data, "c1");
		var c2 = GetOrDefault(data, "c2");
		if (IsTruthy(c1) || IsTruthy(c2))
		{
			var flags = new HBoxContainer();
			flags.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			if (IsTruthy(c1))
			{
				var country = c1.AsGodotObject();
				flags.AddChild(_create_flag(country.Get("country_name").AsString(), country.Get("ideology_name").AsString()));
				var spacer = new Control();
				spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				flags.AddChild(spacer);
			}
			if (IsTruthy(c2))
			{
				if (!IsTruthy(c1))
				{
					var spacer_left = new Control();
					spacer_left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
					flags.AddChild(spacer_left);
				}
				var country = c2.AsGodotObject();
				flags.AddChild(_create_flag(country.Get("country_name").AsString(), country.Get("ideology_name").AsString()));
			}
			body.AddChild(flags);
		}

		var label = new Label();
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		label.Text = _get_default_text(data);
		body.AddChild(label);

		var ok_button = new Button();
		ok_button.Text = "Ok";
		ok_button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		ok_button.Connect("pressed", Callable.From(() => dialog.QueueFree()));
		body.AddChild(ok_button);

		dialog.Connect("close_requested", Callable.From(() => dialog.QueueFree()));
		return dialog;
	}

	public string _get_default_title(Godot.Collections.Dictionary data)
	{
		switch (GetOrDefault(data, "event", "event").AsString())
		{
			case "war":
				return "War Declared";
			case "capitulated":
				return "Capitulation";
			case "game_over":
				return "Game Over";
			default:
				return "Alert";
		}
	}

	public string _get_default_text(Godot.Collections.Dictionary data)
	{
		var c1 = GetOrDefault(data, "c1");
		var c2 = GetOrDefault(data, "c2");
		var custom_text = GetOrDefault(data, "text", "").AsString();
		if (custom_text != "")
			return custom_text;

		switch (GetOrDefault(data, "event", "event").AsString())
		{
			case "war":
				if (IsTruthy(c1) && IsTruthy(c2))
					return $"{CountryLabel(c1)} has declared war on {CountryLabel(c2)}!";
				break;
			case "capitulated":
				if (IsTruthy(c1))
					return $"{CountryLabel(c1)} has capitulated.";
				break;
			case "game_over":
				return "Game Over";
			default:
				return GetOrDefault(data, "event", "event").AsString();
		}
		return "";
	}

	public Window _create_custom_event_dialog(Godot.Collections.Dictionary data)
	{
		var dialog = new EventDialog();
		dialog.Title = GetOrDefault(data, "title", "Event").AsString();
		dialog.MinSize = new Vector2I(420, 0);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		dialog.AddChild(margin);

		var content_list = new VBoxContainer();
		content_list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		content_list.AddThemeConstantOverride("separation", 10);
		margin.AddChild(content_list);

		if (data.TryGetValue("contents", out var contents) && contents.VariantType == Variant.Type.Array)
		{
			foreach (var element in contents.AsGodotArray())
			{
				var count_before = content_list.GetChildCount();
				InterpreterManager.Instance.get_element(element, content_list, CountryManager.Instance.player_country);
				var count_after = content_list.GetChildCount();
				if (count_after > count_before)
					_connect_buttons_to_close(content_list.GetChild(count_after - 1), dialog);
			}
		}

		dialog.content_root = content_list;
		return dialog;
	}

	public Window _create_super_event_dialog(Godot.Collections.Dictionary data)
	{
		var dialog = new Window();
		dialog.Title = "Super Event";
		dialog.Size = new Vector2I(440, 320);
		dialog.MinSize = new Vector2I(440, 320);
		dialog.Unresizable = false;

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		dialog.AddChild(panel);

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		panel.AddChild(root);

		var background_rect = new TextureRect();
		background_rect.CustomMinimumSize = new Vector2(0, 200);
		background_rect.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		background_rect.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		background_rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		background_rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		if (data.TryGetValue("background", out var background))
		{
			var path = "res://assets/superevents/" + background.AsString() + ".png";
			if (FileAccess.FileExists(path) || ResourceLoader.Exists(path))
				background_rect.Texture = GD.Load<Texture2D>(path);
			else
				GD.PushWarning($"SuperEvent: Background image not found at {path}");
		}
		root.AddChild(background_rect);

		var desc_label = new Label();
		desc_label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		desc_label.HorizontalAlignment = HorizontalAlignment.Center;
		desc_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		desc_label.Text = GetOrDefault(data, "desc", "").AsString();
		root.AddChild(desc_label);

		var button = new Button();
		button.Text = GetOrDefault(data, "button", "Ok").AsString();
		button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		button.Connect("pressed", Callable.From(() => dialog.QueueFree()));
		root.AddChild(button);

		dialog.Connect("close_requested", Callable.From(() => dialog.QueueFree()));

		if (data.TryGetValue("music", out var music))
		{
			var base_path = "res://assets/music/superevents/" + music.AsString();
			if (FileAccess.FileExists(base_path + ".mp3"))
				MusicManager.Instance.play_custom_file(base_path + ".mp3");
			else if (FileAccess.FileExists(base_path + ".ogg"))
				MusicManager.Instance.play_custom_file(base_path + ".ogg");
		}
		else
		{
			MusicManager.Instance.play_sfx(SfxPopup);
		}

		return dialog;
	}

	public void _connect_buttons_to_close(Node node, Window dialog)
	{
		if (node is Button)
			node.Connect("pressed", Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(dialog))
					dialog.QueueFree();
			}));
		foreach (var child in node.GetChildren())
			_connect_buttons_to_close(child, dialog);
	}

	public TextureRect _create_flag(string country, string ideology = "")
	{
		var tr = new TextureRect();
		tr.Texture = TroopManager.Instance.get_flag(country, ideology);
		tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		tr.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		tr.CustomMinimumSize = new Vector2(50, 25);
		return tr;
	}

	// SUPER EVENT MIGRATED LOGIC
	public void load_super_events(string path)
	{
		super_events.Clear();
		var json = MapData.Instance.LoadJSON(path);
		if (json.VariantType == Variant.Type.Dictionary)
		{
			var root = json.AsGodotDictionary();
			if (root.TryGetValue("events", out var events))
			{
				foreach (var event_data in events.AsGodotArray())
				{
					var event_dict = event_data.AsGodotDictionary();
					if (event_dict.TryGetValue("cause", out var cause) && IsNotEmpty(cause))
						super_events.Add(event_dict);
				}
			}
		}
	}

	public void check_super_events()
	{
		var remove = new List<int>();
		var index_cur = 0;
		foreach (var superEvent in super_events)
		{
			var event_dict = superEvent.AsGodotDictionary();
			if (InterpreterManager.Instance.get_function(GetOrDefault(event_dict, "cause", new Godot.Collections.Dictionary())).AsBool())
			{
				_add_dialog(_create_super_event_dialog(event_dict));
				remove.Add(index_cur);
			}
			index_cur++;
		}
		remove.Reverse();
		foreach (var index in remove)
			super_events.RemoveAt(index);
	}

	public void schedule_event(int days, Godot.Collections.Dictionary data)
	{
		var clock = CurrentClock();
		if (clock == null)
			return;
		var target_time = (int)clock.get_absolute_days() + days;
		scheduled_events.Add(new Godot.Collections.Dictionary
		{
			{ "trigger_time", target_time },
			{ "event_data", data },
		});
	}

	public void _on_day_passed()
	{
		var clock = CurrentClock();
		if (clock == null)
			return;
		var current = (int)clock.get_absolute_days();
		var to_trigger = new Godot.Collections.Array();

		for (var i = scheduled_events.Count - 1; i >= 0; i--)
		{
			var entry = scheduled_events[i].AsGodotDictionary();
			if (current >= GetOrDefault(entry, "trigger_time", 0).AsInt32())
			{
				to_trigger.Add(GetOrDefault(entry, "event_data"));
				scheduled_events.RemoveAt(i);
			}
		}

		foreach (var ev in to_trigger)
			show_alert(ev.AsGodotDictionary());
	}

	public Godot.Collections.Array save_events()
	{
		return scheduled_events.Duplicate(true);
	}

	public void load_events(Godot.Collections.Array data)
	{
		scheduled_events = data.Duplicate(true);
	}

	private static Variant GetOrDefault(Godot.Collections.Dictionary dict, Variant key, Variant fallback = default)
	{
		return dict.TryGetValue(key, out var value) ? value : fallback;
	}

	// GDScript truthiness for the values stored in event dictionaries.
	private static bool IsTruthy(Variant value)
	{
		if (value.VariantType == Variant.Type.Nil)
			return false;
		if (value.VariantType == Variant.Type.Bool)
			return value.AsBool();
		if (value.VariantType == Variant.Type.Object)
		{
			var obj = value.AsGodotObject();
			return obj != null && GodotObject.IsInstanceValid(obj);
		}
		return true;
	}

	// GDScript is_empty() on the "cause" value (usually an expression dictionary).
	private static bool IsNotEmpty(Variant value)
	{
		switch (value.VariantType)
		{
			case Variant.Type.Nil:
				return false;
			case Variant.Type.Dictionary:
				return value.AsGodotDictionary().Count > 0;
			case Variant.Type.Array:
				return value.AsGodotArray().Count > 0;
			case Variant.Type.String:
			case Variant.Type.StringName:
				return !string.IsNullOrEmpty(value.AsString());
			default:
				return true;
		}
	}

	private static string CountryLabel(Variant country)
		=> country.AsGodotObject().Get("country_name").AsString().Capitalize();

	private static GameClock CurrentClock()
	{
		var world = GameState.Instance.current_world;
		if (world == null || !GodotObject.IsInstanceValid(world))
			return null;
		var clock = world.clock;
		return clock != null && GodotObject.IsInstanceValid(clock) ? clock : null;
	}
}

public partial class EventDialog : Window
{
	public VBoxContainer content_root;

	public override void _Ready()
	{
		Connect("close_requested", Callable.From(() => QueueFree()));
	}

	public override void _Process(double delta)
	{
		if (content_root != null && GodotObject.IsInstanceValid(content_root))
			InterpreterManager.Instance.refresh_buttons(content_root);
	}
}
