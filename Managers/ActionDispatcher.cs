using Godot;
using Sosr;

namespace Sosr;

// The single door for state changes. Controllers (player, AI, console, UI) submit actions;
// this validates them and calls the existing manager methods. Network join-in can hook here.
public partial class ActionDispatcher : Node
{
	public static ActionDispatcher Instance => Autoload.Get<ActionDispatcher>("ActionDispatcher");

	// Result of the last action for callers that need the created object.
	public object LastResult { get; private set; }

	public bool Submit(GameAction action)
	{
		LastResult = null;
		if (action == null)
			return false;

		if (string.IsNullOrEmpty(action.Issuer))
			action.Issuer = CountryData.NameOf(CountryManager.Instance.playerCountry);

		switch (action)
		{
			case DeclareWarAction a:
				if (!HasCountry(a.Warer) || !HasCountry(a.Waree) || a.Warer == a.Waree)
					return Reject(a, "war needs two distinct countries");
				WarManager.Instance.DeclareWar(CountryManager.Instance.GetCountry(a.Warer),
					CountryManager.Instance.GetCountry(a.Waree), a.Silent);
				return true;

			case CallToArmsAction a:
				if (!HasCountry(a.Caller) || !HasCountry(a.Target))
					return Reject(a, "unknown country");
				WarManager.Instance.CallToArms(CountryManager.Instance.GetCountry(a.Caller),
					CountryManager.Instance.GetCountry(a.Target));
				return true;

			case AnnexAction a:
				if (!HasCountry(a.Annexer) || !HasCountry(a.Annexee))
					return Reject(a, "unknown country");
				ProvinceManager.Instance.AnnexCountry(a.Annexer, a.Annexee);
				return true;

			case TransferProvinceAction a:
				if (!HasProvince(a.ProvinceId) || !HasCountry(a.NewOwner))
					return Reject(a, "unknown province or owner");
				ProvinceManager.Instance.TransferOwnership(a.ProvinceId, a.NewOwner);
				return true;

			case OccupyProvinceAction a:
				if (!HasProvince(a.ProvinceId))
					return Reject(a, "unknown province");
				ProvinceManager.Instance.OccupyProvince(a.ProvinceId, a.Occupier);
				return true;

			case BuildAction a:
				if (!HasProvince(a.ProvinceId) || !OwnsProvince(action.Issuer, a.ProvinceId))
					return Reject(a, "cannot build in foreign territory");
				MapManager.Instance.BuildAt(a.ProvinceId);
				return true;

			case BuildIndustryAction a:
				// AI picks from country_to_provinces, which includes occupied provinces.
				// Functional owner can differ; membership is the caller's actual contract.
				if (!HasProvince(a.ProvinceId) || !HasCountry(a.Country) || a.Country != action.Issuer ||
					!HasProvinceInCountry(a.Country, a.ProvinceId))
					return Reject(a, "cannot build in foreign territory");
				MapManager.Instance._ProvinceBuildIndustry(a.ProvinceId, a.Country, a.Type);
				return true;

			case DeployAction a:
				if (!HasProvince(a.ProvinceId) || !OwnsProvince(action.Issuer, a.ProvinceId))
					return Reject(a, "cannot deploy in foreign territory");
				MapManager.Instance.DeployAt(a.ProvinceId);
				return true;

			case SelectCountryAction a:
				if (!HasProvince(a.ProvinceId))
					return Reject(a, "unknown province");
				MapManager.Instance.SelectCountryAt(a.ProvinceId);
				return true;

			case MoveTroopsAction a:
				TroopManager.Instance.CommandMoveAssigned(a.Assignments);
				return true;

			case SplitTroopAction a:
				if (a.Original == null)
					return Reject(a, "no troop to split");
				LastResult = TroopManager.Instance._create_new_split_troop(a.Original, a.Divisions);
				return true;

			case RemoveTroopAction a:
				if (a.Troop == null)
					return Reject(a, "no troop to remove");
				TroopManager.Instance.RemoveTroop(a.Troop);
				return true;
		}

		return Reject(action, "unknown action");
	}

	private static bool HasCountry(string name) =>
		!string.IsNullOrEmpty(name) && CountryManager.Instance.countries.ContainsKey(name);

	private static bool HasProvince(int pid) =>
		MapManager.Instance.provinceObjects.ContainsKey(pid);

	private static bool OwnsProvince(string country, int pid) =>
		MapManager.Instance.provinceObjects.TryGetValue(pid, out var province) &&
		province.GetFunctionalOwner() == country;

	private static bool HasProvinceInCountry(string country, int pid) =>
		ProvinceManager.Instance.CountryToProvinces.TryGetValue(country, out var pids) &&
		pids.Contains(pid);

	private static bool Reject(GameAction action, string reason)
	{
		GD.PushWarning($"Action {action.Kind} rejected: {reason}");
		return false;
	}
}
