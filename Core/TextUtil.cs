using System;

namespace Sosr;

public static class TextUtil
{
	public static string Capitalize(string value) =>
		string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
