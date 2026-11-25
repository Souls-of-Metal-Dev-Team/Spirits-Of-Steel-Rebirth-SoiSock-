using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class TagMatcher : RefCounted
{
	public Expression expression = new Expression();

	public Godot.Collections.Dictionary local_pool = new();

	public Godot.Collections.Array slotted_resources = new();
	public ResourceData evaluated_resource = null; // Used for evaluating a single resource's validity

	// Godot instantiates classes with a parameterless constructor; the (Dictionary)
	// overload mirrors GDScript's _init(local).
	public TagMatcher()
	{
	}

	public TagMatcher(Godot.Collections.Dictionary local)
	{
		local_pool = local;
	}

	public static TagMatcher Create(Godot.Collections.Dictionary local)
		=> new TagMatcher(local);

	public void set_context(Godot.Collections.Array resources)
	{
		slotted_resources = resources;
	}

	public void set_eval_resource(ResourceData res)
	{
		evaluated_resource = res;
	}

	// -----------------
	// For validating an OptionButton choice:
	public bool has_tag(string tag_name)
	{
		if (evaluated_resource != null)
			return evaluated_resource.tags.ContainsKey(tag_name);
		return false;
	}

	public float tag_val(string tag_name)
	{
		if (evaluated_resource != null && evaluated_resource.tags.ContainsKey(tag_name))
			return evaluated_resource.tags[tag_name].AsSingle();
		return 0.0f;
	}
	// -----------------

	// -----------------
	// For evaluating the functionality math formulas:
	public float input_amount(int index)
	{
		if (index >= 0 && index < slotted_resources.Count)
		{
			var slot = slotted_resources[index].AsGodotDictionary();
			return slot.ContainsKey("amount") ? slot["amount"].AsSingle() : 0.0f;
		}
		return 0.0f;
	}

	public bool input_has_tag(int index, string tag_name)
	{
		if (index >= 0 && index < slotted_resources.Count)
		{
			var slot = slotted_resources[index].AsGodotDictionary();
			if (slot.ContainsKey("resource"))
			{
				var tags = TagsOf(slot["resource"].AsGodotObject());
				if (tags != null)
					return tags.ContainsKey(tag_name);
			}
		}
		return false;
	}

	public float input_tag_val(int index, string tag_name)
	{
		if (index >= 0 && index < slotted_resources.Count)
		{
			var slot = slotted_resources[index].AsGodotDictionary();
			if (slot.ContainsKey("resource"))
			{
				var tags = TagsOf(slot["resource"].AsGodotObject());
				if (tags != null && tags.ContainsKey(tag_name))
					return tags[tag_name].AsSingle();
			}
		}
		return 0.0f;
	}

	public float local_pool_amount(string tag_name)
	{
		return local_pool.ContainsKey(tag_name) ? local_pool[tag_name].AsSingle() : 0.0f;
	}
	// -----------------

	private static Godot.Collections.Dictionary TagsOf(GodotObject resource)
	{
		if (resource == null || !GodotObject.IsInstanceValid(resource))
			return null;
		var tags = resource.Get("tags");
		return tags.VariantType == Variant.Type.Dictionary ? tags.AsGodotDictionary() : null;
	}

	public Variant evaluate(string expr_string)
	{
		if (expr_string == "" || expr_string == "true")
			return true;

		var error = expression.Parse(expr_string);
		if (error != Error.Ok)
		{
			GD.PushError("TagMatcher Parse Error: " + expression.GetErrorText());
			return false;
		}

		var result = expression.Execute(new Godot.Collections.Array(), this);
		if (expression.HasExecuteFailed())
		{
			GD.PushError("TagMatcher Execute Error in: " + expr_string);
			return false;
		}
		return result;
	}
}
