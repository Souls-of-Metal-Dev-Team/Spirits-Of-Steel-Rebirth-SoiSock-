using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class FactionMember : RefCounted
{
	public string polity = "";
	public string status = "";

	public static FactionMember FromValues(string aPolity, string aStatus)
	{
		var member = new FactionMember();

		member.polity = aPolity;
		member.status = aStatus;

		return member;
	}

	public static FactionMember FromDict(Godot.Collections.Dictionary aData)
	{
		var member = new FactionMember();

		member.polity = aData["polity"].AsString();
		member.status = aData["status"].AsString();

		return member;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			{ "polity", polity },
			{ "status", status },
		};
	}

	public static int GetIndex(string aStatus)
	{
		return aStatus switch
		{
			"Leader" => 0,
			"Member" => 1,
			_ => -1,
		};
	}

	public static string GetString(int aStatus)
	{
		return aStatus switch
		{
			0 => "Leader",
			1 => "Member",
			_ => "",
		};
	}
}
