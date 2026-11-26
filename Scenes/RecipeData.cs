using Godot;
using System;

[GlobalClass] public partial class RecipeData : Node
{
	[Export] public string resourceProduced;
	[Export] public string[] resourcesRequired;

	public static RecipeData FromDict(Godot.Collections.Dictionary<string, Godot.Variant> aData) {
		RecipeData recipe = new RecipeData();
		recipe.resourceProduced  = (string)aData["resource_produced"];  //, out "");
		recipe.resourcesRequired = (string[])aData["resources_required"];//, out new string[] );
		return recipe;
	}

	public Godot.Collections.Dictionary<string, Godot.Variant> ToDict() {
		return new Godot.Collections.Dictionary<string, Godot.Variant>(){
			{"resource_produced", resourceProduced},
			{"resources_required", resourcesRequired},
		};
	}
}
