using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

public partial class FactionManager : Node
{
	public static FactionManager Instance => Autoload.Get<FactionManager>("FactionManager");

	public Dictionary<string, FactionData> factions = new();

	public void Initialize(Godot.Collections.Array aFactionData)
	{
		factions.Clear();
		foreach (var factionDataVariant in aFactionData)
		{
			var factionData = factionDataVariant.AsGodotDictionary();
			var name = factionData["name"].AsString();
			factions[name] = FactionData.FromDict(factionData.AsState());
			foreach (var member in factions[name].members)
			{
				var country = GetCountry(member.polity);
				if (country != null)
					country.Get("factions").AsGodotArray().Add(name);
			}
		}
	}

	public Godot.Collections.Array SaveFactions()
	{
		var factionsArray = new Godot.Collections.Array();
		foreach (var faction in factions.Keys)
			factionsArray.Add(VariantState.ToVariant(factions[faction].ToDict()));
		return factionsArray;
	}

	public void CreateFaction(string aLeader, string aName, Color aColor)
	{
		var members = new List<FactionMember> { FactionMember.FromValues(aLeader, "Leader") };
		factions[aName] = FactionData.FromValues(aName, aColor.ToHex(), members);
		CountryManager.Instance.GetCountry(aLeader).factions.Add(aName);
	}

	public void ClearFaction(GodotObject country)
	{
		var countryName = country.Get("countryName").AsString();
		var countryFactions = country.Get("factions").AsGodotArray();
		var countryFactionsCopy = countryFactions.Duplicate();
		foreach (var factionVariant in countryFactionsCopy)
		{
			var factionName = factionVariant.AsString();
			if (!factions.ContainsKey(factionName))
				continue;

			var faction = factions[factionName];
			var index = FindMemberIndex(faction.members, countryName);
			if (index != -1)
			{
				faction.members.RemoveAt(index);
				countryFactions.Remove(factionName);
			}
		}
	}

	public void InviteFaction(GodotObject inviter, GodotObject invitee)
	{
		MapManager.Instance.AllowPids(inviter, invitee);
		MapManager.Instance.AllowPids(invitee, inviter);
		// NOTE(soi): we should add an option to choose which faction to invite to
		var inviterName = inviter.Get("countryName").AsString();
		var inviteeName = invitee.Get("countryName").AsString();
		var inviterFactionsCopy = inviter.Get("factions").AsGodotArray().Duplicate();
		var inviteeFactions = invitee.Get("factions").AsGodotArray();
		foreach (var factionVariant in inviterFactionsCopy)
		{
			var factionName = factionVariant.AsString();
			if (!factions.ContainsKey(factionName))
				continue;

			var factionData = factions[factionName];
			var leaderIndex = FindMemberIndex(factionData.members, inviterName);
			if (leaderIndex != -1 && factionData.members[leaderIndex].status == "Leader")
			{
				factionData.members.Add(FactionMember.FromValues(inviteeName, "Member"));
				if (!inviteeFactions.Contains(factionName))
					inviteeFactions.Add(factionName);
				GD.Print($"Invited {inviteeName} to {factionName}");
			}
		}
	}

	public bool InFaction(GodotObject inviter, GodotObject invitee)
	{
		var inviterFactions = inviter.Get("factions").AsGodotArray();
		foreach (var faction in invitee.Get("factions").AsGodotArray())
		{
			if (inviterFactions.Contains(faction))
				return true;
		}
		return false;
	}

	public void KickFaction(GodotObject kicker, GodotObject kickee)
	{
		MapManager.Instance.UnallowPids(kicker, kickee);
		var kickerName = kicker.Get("countryName").AsString();
		var kickeeName = kickee.Get("countryName").AsString();
		var kickeeFactions = kickee.Get("factions").AsGodotArray();
		foreach (var factionVariant in kicker.Get("factions").AsGodotArray())
		{
			var factionName = factionVariant.AsString();
			var factionData = factions[factionName];
			var kickerIndex = FindMemberIndex(factionData.members, kickerName);

			if (kickerIndex != -1 && factionData.members[kickerIndex].status == "Leader")
			{
				var kickeeIndex = FindMemberIndex(factionData.members, kickeeName);
				if (kickeeIndex != -1)
				{
					var memberToKick = factionData.members[kickeeIndex];
					if (memberToKick.status == "Leader" && factionData.members.Count > 1)
					{
						for (var i = 0; i < factionData.members.Count; i++)
						{
							if (i != kickeeIndex)
							{
								factionData.members[i].status = "Leader";
								break;
							}
						}
					}
					factionData.members.RemoveAt(kickeeIndex);
					kickeeFactions.Remove(factionName);
				}
			}
		}
	}

	public void DissolveFaction(GodotObject dissolver, string factionName)
	{
		if (!factions.ContainsKey(factionName))
			return;

		var factionData = factions[factionName];
		var dissolverName = dissolver.Get("countryName").AsString();
		var dissolverIndex = FindMemberIndex(factionData.members, dissolverName);

		if (dissolverIndex != -1 && factionData.members[dissolverIndex].status == "Leader")
		{
			foreach (var member in factionData.members)
			{
				var memberCountry = GetCountry(member.polity);
				if (memberCountry != null)
					memberCountry.Get("factions").AsGodotArray().Remove(factionName);
			}
			factions.Remove(factionName);
		}
	}

	public FactionMember GetFactionMember(string countryName)
	{
		var country = GetCountry(countryName);
		if (country == null)
			return null;

		var countryFactions = country.Get("factions").AsGodotArray();
		if (countryFactions.Count == 0)
			return null;

		foreach (var factionVariant in countryFactions)
		{
			var factionName = factionVariant.AsString();
			if (!factions.ContainsKey(factionName))
				continue;

			var factionData = factions[factionName];
			var index = FindMemberIndex(factionData.members, countryName);
			if (index != -1)
				return factionData.members[index];
		}
		return null;
	}

	public void UpdateMemberStatus(string factionName, string memberName, int status)
	{
		if (!factions.ContainsKey(factionName))
			return;

		var factionData = factions[factionName];
		var index = FindMemberIndex(factionData.members, memberName);
		if (index != -1)
			factionData.members[index].status = FactionMember.GetString(status);
	}

	private static GodotObject GetCountry(string countryName)
	{
		return CountryManager.Instance.GetCountry(countryName);
	}

	private static int FindMemberIndex(List<FactionMember> members, string polity)
	{
		for (var i = 0; i < members.Count; i++)
		{
			if (members[i].polity == polity)
				return i;
		}
		return -1;
	}
}
