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

	public Godot.Collections.Array<Node> activeDialogs = new();
	public Godot.Collections.Array scheduledEvents = new();
	public Godot.Collections.Array superEvents = new();

	public CanvasLayer uiLayer = new CanvasLayer();

	public override void _Ready()
	{
		uiLayer.Layer = 100;
		AddChild(uiLayer);

		var vp = GetViewport();
		if (vp != null)
			vp.GuiEmbedSubwindows = true;

		WarManager.Instance.WarDeclared += (warer, waree) =>
			ShowAlert(new Godot.Collections.Dictionary
			{
				{ "event", "war" },
				{ "c1", warer },
				{ "c2", waree },
			});
		LoadSuperEvents("res://superevents.json");
	}

	// UNIFIED ENTRY POINT
	public void ShowAlert(Godot.Collections.Dictionary data)
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
		uiLayer.AddChild(dialog);
		activeDialogs.Add(dialog);
		_setup_middle_click_close(dialog);
		dialog.Connect("close_requested", Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(dialog))
				dialog.QueueFree();
		}));
		dialog.Connect("tree_exited", Callable.From(() => activeDialogs.Remove(dialog)));
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
				flags.AddChild(_create_flag(country.Get("countryName").AsString(), country.Get("ideologyName").AsString()));
				var spacer = new Control();
				spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				flags.AddChild(spacer);
			}
			if (IsTruthy(c2))
			{
				if (!IsTruthy(c1))
				{
					var spacerLeft = new Control();
					spacerLeft.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
					flags.AddChild(spacerLeft);
				}
				var country = c2.AsGodotObject();
				flags.AddChild(_create_flag(country.Get("countryName").AsString(), country.Get("ideologyName").AsString()));
			}
			body.AddChild(flags);
		}

		var label = new Label();
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		label.Text = _get_default_text(data);
		body.AddChild(label);

		var okButton = new Button();
		okButton.Text = "Ok";
		okButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		okButton.Connect("pressed", Callable.From(() => dialog.QueueFree()));
		body.AddChild(okButton);

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
		var customText = GetOrDefault(data, "text", "").AsString();
		if (customText != "")
			return customText;

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

		var contentList = new VBoxContainer();
		contentList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		contentList.AddThemeConstantOverride("separation", 10);
		margin.AddChild(contentList);

		if (data.TryGetValue("contents", out var contents) && contents.VariantType == Variant.Type.Array)
		{
			foreach (var element in contents.AsGodotArray())
			{
				var countBefore = contentList.GetChildCount();
				InterpreterManager.Instance.GetElement(element, contentList, CountryManager.Instance.playerCountry);
				var countAfter = contentList.GetChildCount();
				if (countAfter > countBefore)
					_connect_buttons_to_close(contentList.GetChild(countAfter - 1), dialog);
			}
		}

		dialog.contentRoot = contentList;
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

		var backgroundRect = new TextureRect();
		backgroundRect.CustomMinimumSize = new Vector2(0, 200);
		backgroundRect.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		backgroundRect.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		backgroundRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		backgroundRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		if (data.TryGetValue("background", out var background))
		{
			var path = "res://assets/superevents/" + background.AsString() + ".png";
			if (FileAccess.FileExists(path) || ResourceLoader.Exists(path))
				backgroundRect.Texture = GD.Load<Texture2D>(path);
			else
				GD.PushWarning($"SuperEvent: Background image not found at {path}");
		}
		root.AddChild(backgroundRect);

		var descLabel = new Label();
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		descLabel.Text = GetOrDefault(data, "desc", "").AsString();
		root.AddChild(descLabel);

		var button = new Button();
		button.Text = GetOrDefault(data, "button", "Ok").AsString();
		button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		button.Connect("pressed", Callable.From(() => dialog.QueueFree()));
		root.AddChild(button);

		dialog.Connect("close_requested", Callable.From(() => dialog.QueueFree()));

		if (data.TryGetValue("music", out var music))
		{
			var basePath = "res://assets/music/superevents/" + music.AsString();
			if (FileAccess.FileExists(basePath + ".mp3"))
				MusicManager.Instance.PlayCustomFile(basePath + ".mp3");
			else if (FileAccess.FileExists(basePath + ".ogg"))
				MusicManager.Instance.PlayCustomFile(basePath + ".ogg");
		}
		else
		{
			MusicManager.Instance.PlaySfx(SfxPopup);
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
		tr.Texture = TroopManager.Instance.GetFlag(country, ideology);
		tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		tr.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		tr.CustomMinimumSize = new Vector2(50, 25);
		return tr;
	}

	// SUPER EVENT MIGRATED LOGIC
	public void LoadSuperEvents(string path)
	{
		superEvents.Clear();
		var json = MapData.Instance.LoadJSON(path);
		if (json.VariantType == Variant.Type.Dictionary)
		{
			var root = json.AsGodotDictionary();
			if (root.TryGetValue("events", out var events))
			{
				foreach (var eventData in events.AsGodotArray())
				{
					var eventDict = eventData.AsGodotDictionary();
					if (eventDict.TryGetValue("cause", out var cause) && IsNotEmpty(cause))
						superEvents.Add(eventDict);
				}
			}
		}
	}

	public void CheckSuperEvents()
	{
		var remove = new List<int>();
		var indexCur = 0;
		foreach (var superEvent in superEvents)
		{
			var eventDict = superEvent.AsGodotDictionary();
			if (InterpreterManager.Instance.GetFunction(GetOrDefault(eventDict, "cause", new Godot.Collections.Dictionary())).AsBool())
			{
				_add_dialog(_create_super_event_dialog(eventDict));
				remove.Add(indexCur);
			}
			indexCur++;
		}
		remove.Reverse();
		foreach (var index in remove)
			superEvents.RemoveAt(index);
	}

	public void ScheduleEvent(int days, Godot.Collections.Dictionary data)
	{
		var clock = CurrentClock();
		if (clock == null)
			return;
		var targetTime = (int)clock.GetAbsoluteDays() + days;
		scheduledEvents.Add(new Godot.Collections.Dictionary
		{
			{ "trigger_time", targetTime },
			{ "event_data", data },
		});
	}

	public void _on_day_passed()
	{
		var clock = CurrentClock();
		if (clock == null)
			return;
		var current = (int)clock.GetAbsoluteDays();
		var toTrigger = new Godot.Collections.Array();

		for (var i = scheduledEvents.Count - 1; i >= 0; i--)
		{
			var entry = scheduledEvents[i].AsGodotDictionary();
			if (current >= GetOrDefault(entry, "trigger_time", 0).AsInt32())
			{
				toTrigger.Add(GetOrDefault(entry, "event_data"));
				scheduledEvents.RemoveAt(i);
			}
		}

		foreach (var ev in toTrigger)
			ShowAlert(ev.AsGodotDictionary());
	}

	public Godot.Collections.Array SaveEvents()
	{
		return scheduledEvents.Duplicate(true);
	}

	public void LoadEvents(Godot.Collections.Array data)
	{
		scheduledEvents = data.Duplicate(true);
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
		=> country.AsGodotObject().Get("countryName").AsString().Capitalize();

	private static GameClock CurrentClock()
	{
		var world = GameState.Instance.currentWorld;
		if (world == null || !GodotObject.IsInstanceValid(world))
			return null;
		var clock = world.clock;
		return clock != null && GodotObject.IsInstanceValid(clock) ? clock : null;
	}
}

public partial class EventDialog : Window
{
	public VBoxContainer contentRoot;

	public override void _Ready()
	{
		Connect("close_requested", Callable.From(() => QueueFree()));
	}

	public override void _Process(double delta)
	{
		if (contentRoot != null && GodotObject.IsInstanceValid(contentRoot))
			InterpreterManager.Instance.RefreshButtons(contentRoot);
	}
}
