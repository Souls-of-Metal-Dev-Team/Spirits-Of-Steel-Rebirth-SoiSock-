extends SceneTree

# Run: godot-mono --headless --path . --script res://scratch/test_port_parity.gd
#
# Superseded by the headless xUnit suite. The data classes now live in Core/
# as engine-free POCOs, so GDScript can no longer load them by script path.
# Run: dotnet test Tests/Sosr.Tests.csproj


func _init() -> void:
	print("Data round-trip checks moved to Tests/ (dotnet test Tests/Sosr.Tests.csproj)")
	quit(0)
