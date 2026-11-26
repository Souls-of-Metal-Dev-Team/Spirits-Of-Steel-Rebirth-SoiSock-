using System.Collections.Generic;

namespace Sosr;

public sealed class FactionMember
{
	public string polity = "";
	public string status = "";

	public static FactionMember FromValues(string aPolity, string aStatus) => new()
	{
		polity = aPolity,
		status = aStatus,
	};

	public static FactionMember FromDict(Dictionary<string, object?> aData) => new()
	{
		polity = aData.Get("polity").Str(),
		status = aData.Get("status").Str(),
	};

	public Dictionary<string, object?> ToDict() => new()
	{
		{ "polity", polity },
		{ "status", status },
	};

	public static int GetIndex(string aStatus) => aStatus switch
	{
		"Leader" => 0,
		"Member" => 1,
		_ => -1,
	};

	public static string GetString(int aStatus) => aStatus switch
	{
		0 => "Leader",
		1 => "Member",
		_ => "",
	};
}
