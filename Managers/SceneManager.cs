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
		var lsScene = GD.Load<PackedScene>("res://Scenes/LoadingScreen.tscn");
		if (lsScene != null)
		{
			_loading_screen = lsScene.Instantiate();
			AddChild(_loading_screen);
		}
	}

	public bool HasActiveWorld() => GodotObject.IsInstanceValid(_world_cache);

	public bool IsWorldActive() => _current_type == TypeWorld;

	public void SwitchTo(int sceneType) => SwitchTo(sceneType, new Callable());

	public async void SwitchTo(int sceneType, Callable initCallback)
	{
		if (sceneType == _current_type)
			return;

		if (_loading_screen != null)
		{
			_loading_screen.Call("ShowScreen");
			_loading_screen.Call("SetProgress", 0.0f);
		}

		// Small delay to ensure loading screen is visible
		await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);

		var main = GetTree().CurrentScene;
		var container = main.GetNode("CurrentScene");

		// 1. Handle the scene we are LEAVING
		if (container.GetChildCount() > 0)
		{
			var oldScene = container.GetChild(0);
			if (_current_type == TypeWorld)
				container.RemoveChild(oldScene);
			else
				oldScene.QueueFree();
		}

		// 2. Handle the scene we are ENTERING
		Node nextScene;
		var progress = new Godot.Collections.Array { 0.0f };
		var progressLock = new object();

		if (sceneType == TypeWorld && _world_cache != null)
		{
			nextScene = _world_cache;
			if (_loading_screen != null)
				_loading_screen.Call("SetProgress", 1.0f);
		}
		else
		{
			if (!SCENE_MAP.ContainsKey(sceneType))
			{
				GD.PushError("Failed to load scene: unknown scene type " + sceneType);
				return;
			}
			var path = SCENE_MAP[sceneType].AsString();
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
						lock (progressLock)
						{
							if (_loading_screen != null && progress.Count > 0)
								_loading_screen.Call("SetProgress", progress[0].AsSingle());
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

			var packedScene = ResourceLoader.LoadThreadedGet(path) as PackedScene;
			nextScene = packedScene?.Instantiate();
			if (sceneType == TypeWorld)
				_world_cache = nextScene;
		}

		// 2.5 Execute initialization callback if provided
		if (initCallback.Target != null && initCallback.Method.ToString() != "")
		{
			lock (progressLock)
			{
				progress.Clear();
				progress.Add(0.0f);
			}
			var callbackProgress = progress;
			var bongo = new GodotThread();
			bongo.Start(Callable.From(() => initCallback.Call(callbackProgress)));

			while (bongo.IsAlive())
			{
				lock (progressLock)
				{
					if (_loading_screen != null && progress.Count > 0 && progress[0].AsSingle() != 0.0f)
						_loading_screen.Call("SetProgress", progress[0].AsSingle());
				}
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			bongo.WaitToFinish();
		}

		// 3. Add to tree
		if (nextScene != null)
		{
			container.AddChild(nextScene);
			if (_current_type == TypeMenu && sceneType == TypeWorld)
			{
				// DoSetup touches the scene tree; run it on the main thread.
				var setupProgress = new Godot.Collections.Array { 0 };
				nextScene.Call("DoSetup", setupProgress);
			}
			else if (sceneType == TypeEditor)
			{
				GameState.Instance.currentWorld?.clock?.pause();
			}
			else if (_current_type == TypeWorld)
			{
				var tooltip = GameState.Instance.tooltip;
				if (tooltip != null && GodotObject.IsInstanceValid(tooltip))
					tooltip.shouldBeVisible = false;
			}
			_current_type = sceneType;
		}

		if (_loading_screen != null)
		{
			_loading_screen.Call("SetProgress", 1.0f);
			await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout); // Stay at 100% briefly
			_loading_screen.Call("HideScreen");
		}
	}

	public Node _instantiate_by_type(int type)
	{
		if (!SCENE_MAP.ContainsKey(type))
			return null;
		var packedScene = GD.Load<PackedScene>(SCENE_MAP[type].AsString());
		return packedScene?.Instantiate();
	}
}
