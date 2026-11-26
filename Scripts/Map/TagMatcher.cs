using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class TagMatcher : RefCounted
{
	public Expression expression = new Expression();

	public Godot.Collections.Dictionary localPool = new();

	public Godot.Collections.Array slottedResources = new();
	public ResourceData evaluatedResource = null; // Used for evaluating a single resource's validity

	// Godot instantiates classes with a parameterless constructor; the (Dictionary)
	// overload mirrors GDScript's _init(local).
	public TagMatcher()
	{
	}

	public TagMatcher(Godot.Collections.Dictionary local)
	{
		localPool = local;
	}

	public static TagMatcher Create(Godot.Collections.Dictionary local)
		=> new TagMatcher(local);

	public void SetContext(Godot.Collections.Array resources)
	{
		slottedResources = resources;
	}

	public void SetEvalResource(ResourceData res)
	{
		evaluatedResource = res;
	}

	// -----------------
	// For validating an OptionButton choice:
	public bool HasTag(string tagName)
	{
		if (evaluatedResource != null)
			return evaluatedResource.tags.ContainsKey(tagName);
		return false;
	}

	public float TagVal(string tagName)
	{
		if (evaluatedResource != null && evaluatedResource.tags.ContainsKey(tagName))
			return evaluatedResource.tags[tagName].Single();
		return 0.0f;
	}
	// -----------------

	// -----------------
	// For evaluating the functionality math formulas:
	public float InputAmount(int index)
	{
		if (index >= 0 && index < slottedResources.Count)
		{
			var slot = slottedResources[index].AsGodotDictionary();
			return slot.ContainsKey("amount") ? slot["amount"].AsSingle() : 0.0f;
		}
		return 0.0f;
	}

	public bool InputHasTag(int index, string tagName)
	{
		if (index >= 0 && index < slottedResources.Count)
		{
			var slot = slottedResources[index].AsGodotDictionary();
			if (slot.ContainsKey("resource"))
			{
				var tags = TagsOf(slot["resource"].AsGodotObject());
				if (tags != null)
					return tags.ContainsKey(tagName);
			}
		}
		return false;
	}

	public float InputTagVal(int index, string tagName)
	{
		if (index >= 0 && index < slottedResources.Count)
		{
			var slot = slottedResources[index].AsGodotDictionary();
			if (slot.ContainsKey("resource"))
			{
				var tags = TagsOf(slot["resource"].AsGodotObject());
				if (tags != null && tags.ContainsKey(tagName))
					return tags[tagName].AsSingle();
			}
		}
		return 0.0f;
	}

	public float LocalPoolAmount(string tagName)
	{
		return localPool.ContainsKey(tagName) ? localPool[tagName].AsSingle() : 0.0f;
	}
	// -----------------

	private static Godot.Collections.Dictionary TagsOf(GodotObject resource)
	{
		if (resource == null || !GodotObject.IsInstanceValid(resource))
			return null;
		var tags = resource.Get("tags");
		return tags.VariantType == Variant.Type.Dictionary ? tags.AsGodotDictionary() : null;
	}

	public Variant evaluate(string exprString)
	{
		if (exprString == "" || exprString == "true")
			return true;

		var error = expression.Parse(exprString);
		if (error != Error.Ok)
		{
			GD.PushError("TagMatcher Parse Error: " + expression.GetErrorText());
			return false;
		}

		var result = expression.Execute(new Godot.Collections.Array(), this);
		if (expression.HasExecuteFailed())
		{
			GD.PushError("TagMatcher Execute Error in: " + exprString);
			return false;
		}
		return result;
	}
}
