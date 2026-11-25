using Godot;
using Sosr;

namespace Sosr;

public partial class FactionManager : Node
{
	public Godot.Collections.Dictionary<string, FactionData> factions = new Godot.Collections.Dictionary<string, FactionData>();

	public void Initialize(Godot.Collections.Array a_factionData)
	{
		factions.Clear();
		foreach (var factionDataVariant in a_factionData)
		{
			var factionData = factionDataVariant.AsGodotDictionary();
			var name = factionData["name"].AsString();
			factions[name] = FactionData.FromDict(factionData);
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
			factionsArray.Add(factions[faction].ToDict());
		return factionsArray;
	}

	public void create_faction(string a_leader, string a_name, Color a_color)
	{
		var members = new Godot.Collections.Array<FactionMember> { FactionMember.FromValues(a_leader, "Leader") };
		factions[a_name] = FactionData.FromValues(a_name, a_color, members);
		GdBridge.Country(a_leader).Get("factions").AsGodotArray().Add(a_name);
	}

	public void clear_faction(GodotObject country)
	{
		var countryName = country.Get("country_name").AsString();
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

	public void invite_faction(GodotObject inviter, GodotObject invitee)
	{
		GdBridge.Call("MapManager", "AllowPids", inviter, invitee);
		GdBridge.Call("MapManager", "AllowPids", invitee, inviter);
		// NOTE(soi): we should add an option to choose which faction to invite to
		var inviterName = inviter.Get("country_name").AsString();
		var inviteeName = invitee.Get("country_name").AsString();
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

	public bool in_faction(GodotObject inviter, GodotObject invitee)
	{
		var inviterFactions = inviter.Get("factions").AsGodotArray();
		foreach (var faction in invitee.Get("factions").AsGodotArray())
		{
			if (inviterFactions.Contains(faction))
				return true;
		}
		return false;
	}

	public void kick_faction(GodotObject kicker, GodotObject kickee)
	{
		GdBridge.Call("MapManager", "UnallowPids", kicker, kickee);
		var kickerName = kicker.Get("country_name").AsString();
		var kickeeName = kickee.Get("country_name").AsString();
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

	public void dissolve_faction(GodotObject dissolver, string faction_name)
	{
		if (!factions.ContainsKey(faction_name))
			return;

		var factionData = factions[faction_name];
		var dissolverName = dissolver.Get("country_name").AsString();
		var dissolverIndex = FindMemberIndex(factionData.members, dissolverName);

		if (dissolverIndex != -1 && factionData.members[dissolverIndex].status == "Leader")
		{
			foreach (var member in factionData.members)
			{
				var memberCountry = GetCountry(member.polity);
				if (memberCountry != null)
					memberCountry.Get("factions").AsGodotArray().Remove(faction_name);
			}
			factions.Remove(faction_name);
		}
	}

	public FactionMember get_faction_member(string country_name)
	{
		var country = GetCountry(country_name);
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
			var index = FindMemberIndex(factionData.members, country_name);
			if (index != -1)
				return factionData.members[index];
		}
		return null;
	}

	public void update_member_status(string faction_name, string member_name, int status)
	{
		if (!factions.ContainsKey(faction_name))
			return;

		var factionData = factions[faction_name];
		var index = FindMemberIndex(factionData.members, member_name);
		if (index != -1)
			factionData.members[index].status = FactionMember.GetString(status);
	}

	private static GodotObject GetCountry(string countryName)
	{
		var countries = GdBridge.Countries();
		return countries.TryGetValue(countryName, out var value) ? value.AsGodotObject() : null;
	}

	private static int FindMemberIndex(Godot.Collections.Array<FactionMember> members, string polity)
	{
		for (var i = 0; i < members.Count; i++)
		{
			if (members[i].polity == polity)
				return i;
		}
		return -1;
	}
}
