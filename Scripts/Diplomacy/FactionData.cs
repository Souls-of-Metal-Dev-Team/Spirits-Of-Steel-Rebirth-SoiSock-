using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class FactionData : RefCounted
{
	public string name = "";
	public Color color = Colors.White;
	public Godot.Collections.Array<FactionMember> members = new Godot.Collections.Array<FactionMember>();

	public static FactionData FromValues(string aName, Color aColor, Godot.Collections.Array<FactionMember> aMembers = null)
	{
		var faction = new FactionData();

		faction.name = aName;
		faction.color = aColor;
		faction.members = aMembers ?? new Godot.Collections.Array<FactionMember>();

		return faction;
	}

	public static FactionData FromDict(Godot.Collections.Dictionary aData)
	{
		var faction = new FactionData();

		faction.name = aData["name"].AsString();
		faction.color = aData.ContainsKey("color") ? Color.FromHtml(aData["color"].AsString()) : Color.FromHtml("#FFFFFF");
		foreach (var factionMemberData in aData["members"].AsGodotArray())
			faction.members.Add(FactionMember.FromDict(factionMemberData.AsGodotDictionary()));

		return faction;
	}

	public Godot.Collections.Dictionary ToDict()
	{
		var data = new Godot.Collections.Dictionary
		{
			{ "name", name },
			{ "color", "#" + color.ToHtml(false).ToUpperInvariant() },
			{ "members", new Godot.Collections.Array() },
		};

		foreach (var factionMember in members)
			data["members"].AsGodotArray().Add(factionMember.ToDict());

		return data;
	}

	public void UpdateMemberStatus(string _aMember, int _aIndex)
	{
	}

	public void SetMemberStatus(string aMember, string aStatus)
	{
		foreach (var factionMember in members)
		{
			if (factionMember.polity == aMember)
				factionMember.status = aStatus;
		}
		members.Add(FactionMember.FromValues(aMember, aStatus));
	}

	public string GetMemberStatus(string aMember)
	{
		foreach (var factionMember in members)
		{
			if (factionMember.polity == aMember)
				return factionMember.status;
		}
		return "";
	}

	public void KickMember(FactionMember member)
	{
		members.Remove(member);
	}

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
