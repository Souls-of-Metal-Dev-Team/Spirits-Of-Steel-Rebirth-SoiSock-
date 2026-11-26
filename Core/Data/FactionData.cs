using System.Collections.Generic;

namespace Sosr;

public sealed class FactionData
{
	public string name = "";
	public string color = "#FFFFFF";
	public List<FactionMember> members = new();

	public static FactionData FromValues(string aName, string aColor, List<FactionMember>? aMembers = null) => new()
	{
		name = aName,
		color = aColor.Hex(),
		members = aMembers ?? new List<FactionMember>(),
	};

	public static FactionData FromDict(Dictionary<string, object?> aData)
	{
		var faction = new FactionData
		{
			name = aData.Get("name").Str(),
			color = aData.Get("color").Hex(),
		};
		foreach (var memberData in aData.Get("members").List())
			faction.members.Add(FactionMember.FromDict(memberData.Dict()));
		return faction;
	}

	public Dictionary<string, object?> ToDict()
	{
		var list = new List<object?>();
		foreach (var member in members)
			list.Add(member.ToDict());
		return new Dictionary<string, object?>
		{
			{ "name", name },
			{ "color", color },
			{ "members", list },
		};
	}

	public void UpdateMemberStatus(string aMember, int aIndex) { }

	public void SetMemberStatus(string aMember, string aStatus)
	{
		foreach (var member in members)
			if (member.polity == aMember)
				member.status = aStatus;
		members.Add(FactionMember.FromValues(aMember, aStatus));
	}

	public string GetMemberStatus(string aMember)
	{
		foreach (var member in members)
			if (member.polity == aMember)
				return member.status;
		return "";
	}

	public void KickMember(FactionMember member) => members.Remove(member);

	public void KickMemberByName(string aMember)
	{
		for (var i = 0; i < members.Count; i++)
		{
			if (members[i].polity == aMember)
			{
				members.RemoveAt(i);
				break;
			}
		}
	}
}
