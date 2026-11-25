using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class NameGenerator : RefCounted
{
	public static readonly Godot.Collections.Dictionary<string, float> SHARED_CONSONANTS = new()
	{
		{ "b", 10.00f }, { "bl",  1.00f },
		{ "c", 10.00f }, { "ch",  1.00f },
		{ "d", 10.00f }, { "dh",  1.00f },
		{ "f", 10.00f }, { "fl",  1.00f },
		{ "g", 10.00f }, { "gh",  1.00f }, { "gr",  1.00f },
		{ "h", 10.00f },
		{ "j", 10.00f },
		{ "k", 10.00f },
		{ "l", 10.00f }, { "ll",  1.00f },
		{ "m", 10.00f },
		{ "n", 10.00f },
		{ "p", 10.00f }, { "ph",  1.00f }, { "ps",  1.00f }, { "pt",  1.00f },
		{ "q", 10.00f },
		{ "r", 10.00f },
		{ "s", 10.00f }, { "sh",  1.00f }, { "sm",  1.00f }, { "sn",  1.00f }, { "sp",  1.00f }, { "st",  1.00f },
		{ "t", 10.00f }, { "th",  1.00f },
		{ "v", 10.00f },
		{ "w", 10.00f },
		{ "x",  2.00f },
		{ "y", 10.00f },
		{ "z",  2.00f },
	};

	public static readonly Godot.Collections.Dictionary<string, float> START_CONSONANTS = new()
	{
		{ "br",  1.00f },
		{ "dr",  1.00f },
		{ "fr",  1.00f },
		{ "gl",  1.00f },
		{ "kn",  1.00f }, { "kr",  1.00f },
		{ "pl",  1.00f }, { "pr",  1.00f },
		{ "qu",  1.00f },
		{ "sc",  1.00f }, { "sk",  1.00f }, { "sl",  1.00f }, { "sw",  1.00f }, { "sch",  1.00f }, { "str",  1.00f }, { "spl",  1.00f }, { "spr",  1.00f },
		{ "tr",  1.00f }, { "tw",  1.00f }, { "thr",  1.00f },
		{ "wh",  1.00f }, { "wr",  1.00f },
	};

	public static readonly Godot.Collections.Dictionary<string, float> MIDDLE_CONSONANTS = new()
	{
		{ "bb",  1.00f }, { "br",  1.00f },
		{ "cc",  1.00f }, { "ck",  1.00f },
		{ "dd",  1.00f }, { "dr",  1.00f }, { "dw",  1.00f },
		{ "ff",  1.00f }, { "fr",  1.00f },
		{ "gg",  1.00f }, { "gl",  1.00f }, { "ght",  1.00f },
		{ "kk",  1.00f }, { "kn",  1.00f }, { "kr",  1.00f },
		{ "ld",  1.00f }, { "lg",  1.00f }, { "lk",  1.00f }, { "lm",  1.00f }, { "lp",  1.00f }, { "lt",  1.00f }, { "lv",  1.00f },
		{ "mm",  1.00f }, { "mp",  1.00f },
		{ "ng",  1.00f }, { "nk",  1.00f }, { "nn",  1.00f }, { "nt",  1.00f },
		{ "pl",  1.00f }, { "pp",  1.00f }, { "pr",  1.00f },
		{ "qu",  1.00f },
		{ "rd",  1.00f }, { "rg",  1.00f }, { "rk",  1.00f }, { "rl",  1.00f }, { "rm",  1.00f }, { "rp",  1.00f }, { "rr",  1.00f }, { "rt",  1.00f },
		{ "sc",  1.00f }, { "sk",  1.00f }, { "sl",  1.00f }, { "ss",  1.00f }, { "sw",  1.00f }, { "sch",  1.00f }, { "str",  1.00f }, { "spl",  1.00f }, { "spr",  1.00f },
		{ "tr",  1.00f }, { "tt",  1.00f }, { "tw",  1.00f }, { "tch",  1.00f }, { "thr",  1.00f },
		{ "wh",  1.00f }, { "wr",  1.00f },
		{ "xt",  1.00f },
		{ "zz",  1.00f },
	};

	public static readonly Godot.Collections.Dictionary<string, float> END_CONSONANTS = new()
	{
		{ "cc",  1.00f }, { "ck",  1.00f },
		{ "dd",  1.00f },
		{ "ff",  1.00f },
		{ "gg",  1.00f }, { "gl",  1.00f }, { "ght",  1.00f },
		{ "kk",  1.00f }, { "kn",  1.00f }, { "kr",  1.00f },
		{ "ld",  1.00f }, { "lg",  1.00f }, { "lk",  1.00f }, { "lm",  1.00f }, { "lp",  1.00f }, { "lt",  1.00f }, { "lv",  1.00f },
		{ "mm",  1.00f }, { "mp",  1.00f },
		{ "ng",  1.00f }, { "nk",  1.00f }, { "nn",  1.00f }, { "nt",  1.00f },
		{ "pp",  1.00f },
		{ "qu",  1.00f },
		{ "rd",  1.00f }, { "rg",  1.00f }, { "rk",  1.00f }, { "rl",  1.00f }, { "rm",  1.00f }, { "rp",  1.00f }, { "rr",  1.00f }, { "rt",  1.00f },
		{ "sc",  1.00f }, { "sk",  1.00f }, { "ss",  1.00f }, { "sch",  1.00f },
		{ "tt",  1.00f }, { "tch",  1.00f },
		{ "xt",  1.00f },
		{ "zz",  1.00f },
	};

	public static readonly Godot.Collections.Dictionary<string, float> ALL_CONSONANTS = new()
	{
		{ "b",  1.00f }, { "bb",  1.00f }, { "bl",  1.00f }, { "br",  1.00f },
		{ "c",  1.00f }, { "cc",  1.00f }, { "ch",  1.00f }, { "ck",  1.00f },
		{ "d",  1.00f }, { "dd",  1.00f }, { "dh",  1.00f }, { "dr",  1.00f }, { "dw",  1.00f },
		{ "f",  1.00f }, { "ff",  1.00f }, { "fl",  1.00f }, { "fr",  1.00f },
		{ "g",  1.00f }, { "gg",  1.00f }, { "gh",  1.00f }, { "gl",  1.00f }, { "gr",  1.00f }, { "ght",  1.00f },
		{ "h",  1.00f },
		{ "j",  1.00f },
		{ "k",  1.00f }, { "kk",  1.00f }, { "kn",  1.00f }, { "kr",  1.00f },
		{ "l",  1.00f }, { "ld",  1.00f }, { "lg",  1.00f }, { "lk",  1.00f }, { "ll",  1.00f }, { "lm",  1.00f }, { "lp",  1.00f }, { "lt",  1.00f }, { "lv",  1.00f },
		{ "m",  1.00f }, { "mm",  1.00f }, { "mp",  1.00f },
		{ "n",  1.00f }, { "ng",  1.00f }, { "nk",  1.00f }, { "nn",  1.00f }, { "nt",  1.00f },
		{ "p",  1.00f }, { "ph",  1.00f }, { "pl",  1.00f }, { "pp",  1.00f }, { "pr",  1.00f }, { "ps",  1.00f }, { "pt",  1.00f },
		{ "q",  1.00f }, { "qu",  1.00f },
		{ "r",  1.00f }, { "rd",  1.00f }, { "rg",  1.00f }, { "rk",  1.00f }, { "rl",  1.00f }, { "rm",  1.00f }, { "rp",  1.00f }, { "rr",  1.00f }, { "rt",  1.00f },
		{ "s",  1.00f }, { "sc",  1.00f }, { "sh",  1.00f }, { "sk",  1.00f }, { "sl",  1.00f }, { "sm",  1.00f }, { "sn",  1.00f }, { "sp",  1.00f }, { "ss",  1.00f }, { "st",  1.00f }, { "sw",  1.00f }, { "sch",  1.00f }, { "str",  1.00f }, { "spl",  1.00f }, { "spr",  1.00f },
		{ "t",  1.00f }, { "th",  1.00f }, { "tr",  1.00f }, { "tt",  1.00f }, { "tw",  1.00f }, { "tch",  1.00f }, { "thr",  1.00f },
		{ "v",  1.00f },
		{ "w",  1.00f }, { "wh",  1.00f }, { "wr",  1.00f },
		{ "x",  1.00f }, { "xt",  1.00f },
		{ "y",  1.00f },
		{ "z",  1.00f }, { "zz",  1.00f },
	};

	public static readonly Godot.Collections.Dictionary<string, float> VOWELS = new()
	{
		{ "a", 35.00f },              { "ae",  7.50f }, { "ai",  1.00f }, { "ao",  1.00f }, { "au", 12.50f }, { "ay",  1.00f },
		{ "e", 55.00f }, { "ea",  1.00f }, { "ee", 10.00f }, { "ei",  1.00f }, { "eo",  1.00f }, { "eu",  1.00f }, { "ey",  1.00f }, { "eau",  1.00f },
		{ "i", 35.00f }, { "ia",  1.00f }, { "ie",  1.00f },              { "io",  1.00f }, { "iu",  1.00f },              { "iou",  1.00f },
		{ "o", 35.00f }, { "oa",  1.00f }, { "oe",  1.00f }, { "oi",  1.00f }, { "oo",  6.00f }, { "ou",  5.00f }, { "oy",  1.00f },
		{ "u", 35.00f }, { "ua",  1.00f }, { "ue",  1.00f }, { "ui",  1.00f }, { "uo",  1.00f },              { "uy",  1.00f }, { "uou",  1.00f },
		{ "y", 25.00f },
	};

	public static string GenerateName(bool aConsonant, int aSize)
	{
		var usedMiddleConsonants = Merge(MIDDLE_CONSONANTS, SHARED_CONSONANTS);
		var toReturn = WeightedRandoms(aConsonant ? Merge(START_CONSONANTS, SHARED_CONSONANTS) : VOWELS);
		aConsonant = !aConsonant;
		for (var i = 0; i < aSize - 2; i++)
		{
			toReturn += WeightedRandoms(aConsonant ? usedMiddleConsonants : VOWELS);
			aConsonant = !aConsonant;
		}
		toReturn += WeightedRandoms(aConsonant ? Merge(END_CONSONANTS, SHARED_CONSONANTS) : VOWELS);
		return toReturn;
	}

	public static string WeightedRandoms(Godot.Collections.Dictionary<string, float> aOptions)
	{
		var total = 0f;
		foreach (var weight in aOptions.Values)
			total += weight;
		var choice = (float)GD.RandRange(0.0, total);
		foreach (var option in aOptions)
		{
			choice -= option.Value;
			if (choice <= 0)
				return option.Key;
		}
		return "";
	}

	// GDScript Dictionary.merged() with its default overwrite = false.
	private static Godot.Collections.Dictionary<string, float> Merge(
		Godot.Collections.Dictionary<string, float> aFirst,
		Godot.Collections.Dictionary<string, float> aSecond)
	{
		var result = new Godot.Collections.Dictionary<string, float>();
		foreach (var entry in aFirst)
			result[entry.Key] = entry.Value;
		foreach (var entry in aSecond)
		{
			if (!result.ContainsKey(entry.Key))
				result[entry.Key] = entry.Value;
		}
		return result;
	}
}
