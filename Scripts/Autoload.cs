using Godot;

namespace Sosr;

// Autoloads are ordinary nodes under /root, not Engine singletons. The C# managers reach
// each other through their typed `Instance` accessors, which use this lookup.
internal static class Autoload
{
	public static Node Get(string name)
	{
		var tree = Engine.GetMainLoop() as SceneTree;
		return tree?.Root?.GetNodeOrNull(name);
	}

	public static T Get<T>(string name) where T : Node => Get(name) as T;
}

// The console addon is a GDScript autoload. The cs_bindings Console wrapper creates its own
// script instance instead of using the autoload, so print through the autoload directly.
internal static class GameConsole
{
	public static Node Node => Autoload.Get("Console");

	public static bool IsVisible() => Node?.Call("is_visible").AsBool() ?? false;

	public static void Line(string text) => Node?.Call("print_line", text);

	public static void Info(string text) => Node?.Call("print_info", text);

	public static void Error(string text) => Node?.Call("print_error", text);
}
