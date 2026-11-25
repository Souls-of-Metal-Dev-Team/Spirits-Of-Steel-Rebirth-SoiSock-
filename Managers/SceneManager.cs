using Godot;
using Sosr;

namespace Sosr;

public partial class SceneManager : Node
{
	public static SceneManager Instance => Autoload.Get<SceneManager>("SceneSwitcher");

	public const int TypeMenu = 0;
	public const int TypeWorld = 1;
	public const int TypeEditor = 2;
	public const int TypeSelectCountry = 3;

	public Godot.Collections.Dictionary SCENE_MAP = new()
	{
		{ TypeMenu, "res://Scenes/main_menu.tscn" },
		{ TypeWorld, "res://Scenes/world.tscn" },
		{ TypeEditor, "res://Scenes/MapEditor.tscn" },
		{ TypeSelectCountry, "res://Scenes/select_country.tscn" },
	};

	public Node _world_cache = null; // We only care about saving this one
	public int _current_type = -1;
	public Node _loading_screen = null;

	public override void _Ready()
	{
		// Instantiate loading screen and add it as a persistent sibling of CurrentScene.
		var ls_scene = GD.Load<PackedScene>("res://Scenes/LoadingScreen.tscn");
		if (ls_scene != null)
		{
			_loading_screen = ls_scene.Instantiate();
			AddChild(_loading_screen);
		}
	}

	public bool has_active_world() => GodotObject.IsInstanceValid(_world_cache);

	public bool is_world_active() => _current_type == TypeWorld;

	public void switch_to(int scene_type) => switch_to(scene_type, new Callable());

	public async void switch_to(int scene_type, Callable init_callback)
	{
		if (scene_type == _current_type)
			return;

		if (_loading_screen != null)
		{
			_loading_screen.Call("show_screen");
			_loading_screen.Call("set_progress", 0.0f);
		}

		// Small delay to ensure loading screen is visible
		await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);

		var main = GetTree().CurrentScene;
		var container = main.GetNode("CurrentScene");

		// 1. Handle the scene we are LEAVING
		if (container.GetChildCount() > 0)
		{
			var old_scene = container.GetChild(0);
			if (_current_type == TypeWorld)
				container.RemoveChild(old_scene);
			else
				old_scene.QueueFree();
		}

		// 2. Handle the scene we are ENTERING
		Node next_scene;
		var progress = new Godot.Collections.Array { 0.0f };
		var progress_lock = new object();

		if (scene_type == TypeWorld && _world_cache != null)
		{
			next_scene = _world_cache;
			if (_loading_screen != null)
				_loading_screen.Call("set_progress", 1.0f);
		}
		else
		{
			if (!SCENE_MAP.ContainsKey(scene_type))
			{
				GD.PushError("Failed to load scene: unknown scene type " + scene_type);
				return;
			}
			var path = SCENE_MAP[scene_type].AsString();
			ResourceLoader.LoadThreadedRequest(path);

			var done = false;
			while (!done)
			{
				switch (ResourceLoader.LoadThreadedGetStatus(path, progress))
				{
					case ResourceLoader.ThreadLoadStatus.Loaded:
						done = true;
						break;
					case ResourceLoader.ThreadLoadStatus.InProgress:
						lock (progress_lock)
						{
							if (_loading_screen != null && progress.Count > 0)
								_loading_screen.Call("set_progress", progress[0].AsSingle());
						}
						break;
					default:
						GD.PushError("Failed to load scene: " + path);
						done = true;
						break;
				}
				if (!done)
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			var packed_scene = ResourceLoader.LoadThreadedGet(path) as PackedScene;
			next_scene = packed_scene?.Instantiate();
			if (scene_type == TypeWorld)
				_world_cache = next_scene;
		}

		// 2.5 Execute initialization callback if provided
		if (init_callback.Target != null && init_callback.Method.ToString() != "")
		{
			lock (progress_lock)
			{
				progress.Clear();
				progress.Add(0.0f);
			}
			var callback_progress = progress;
			var bongo = new GodotThread();
			bongo.Start(Callable.From(() => init_callback.Call(callback_progress)));

			while (bongo.IsAlive())
			{
				lock (progress_lock)
				{
					if (_loading_screen != null && progress.Count > 0 && progress[0].AsSingle() != 0.0f)
						_loading_screen.Call("set_progress", progress[0].AsSingle());
				}
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			bongo.WaitToFinish();
		}

		// 3. Add to tree
		if (next_scene != null)
		{
			container.AddChild(next_scene);
			if (_current_type == TypeMenu && scene_type == TypeWorld)
			{
				// DoSetup touches the scene tree; run it on the main thread.
				var setup_progress = new Godot.Collections.Array { 0 };
				next_scene.Call("DoSetup", setup_progress);
			}
			else if (scene_type == TypeEditor)
			{
				GameState.Instance.current_world?.clock?.pause();
			}
			else if (_current_type == TypeWorld)
			{
				var tooltip = GameState.Instance.tooltip;
				if (tooltip != null && GodotObject.IsInstanceValid(tooltip))
					tooltip.shouldBeVisible = false;
			}
			_current_type = scene_type;
		}

		if (_loading_screen != null)
		{
			_loading_screen.Call("set_progress", 1.0f);
			await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout); // Stay at 100% briefly
			_loading_screen.Call("hide_screen");
		}
	}

	public Node _instantiate_by_type(int type)
	{
		if (!SCENE_MAP.ContainsKey(type))
			return null;
		var packed_scene = GD.Load<PackedScene>(SCENE_MAP[type].AsString());
		return packed_scene?.Instantiate();
	}
}
