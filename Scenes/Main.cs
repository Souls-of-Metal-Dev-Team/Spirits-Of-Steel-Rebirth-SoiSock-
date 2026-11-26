using Godot;
using Sosr;

namespace Sosr;

public partial class Main : Node2D
{
	public override void _Ready()
	{
		//for i in range(100):
		//	print("%3d) %s %s" % [i + 1, NameGenerator.GenerateName(i % 2 == 0, randi_range(3, 7)).capitalize(), NameGenerator.GenerateName(i % 3 == 0, randi_range(3, 8)).capitalize()])
		// SceneSwitcher.Type.MENU == 0
		SceneManager.Instance.SwitchTo(0);

		// var b := RecipeData.FromDict({"resource_produced": "Hi", "resources_required": ["meta", "aluminum"]})
		// print(b.resource_produced)
		// print(b.resources_required)
		// var c = b.ToDict() # Type inference doesnt work
		// print(c)
	}

	public override void _ExitTree()
	{
		// Run pending .NET finalizers while the engine is still alive. Left for engine
		// teardown, they crash the .NET finalizer thread (SIGSEGV on exit), e.g. after the
		// settings menu has parsed a large save file.
		System.GC.Collect();
		System.GC.WaitForPendingFinalizers();
	}
}
