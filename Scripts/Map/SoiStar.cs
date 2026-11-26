using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class SoiStar : AStar2D
{
	// NOTE(soi): I AM A SUPERSTAR

	public HashSet<string> contextAllowedCountries = new();

	public override float _ComputeCost(long fromId, long toId)
	{
		return GetPointPosition(fromId).DistanceTo(GetPointPosition(toId));
	}

	public override float _EstimateCost(long fromId, long toId)
	{
		return GetPointPosition(fromId).DistanceTo(GetPointPosition(toId));
	}

	public override bool _FilterNeighbor(
		long fromId,
		long toId
	)
	{
		var provinceObjects = MapManager.Instance.provinceObjects;
		if (!provinceObjects.TryGetValue((int)fromId, out var fromProv)
			|| !provinceObjects.TryGetValue((int)toId, out var toProv))
			return true;

		if (fromProv == null || toProv == null)
			return true;

		var fromOwner = fromProv.GetFunctionalOwner();
		var toOwner = toProv.GetFunctionalOwner();

		// --- 1. Naval Restrictions (Hard requirement) ---
		// if from_prov.type == Province.LAND and to_prov.type == Province.SEA:
		// 	var has_port = false
		// 	for b in from_prov.buildings:
		// 		if b.type == "Port":
		// 			has_port = true
		// 			break
		// 	if !has_port: return true
		//
		// if from_prov.type == Province.SEA and to_prov.type == Province.LAND:
		// 	var has_port = false
		// 	for b in to_prov.buildings:
		// 		if b.type == "Port":
		// 			has_port = true
		// 			break
		// 	if !has_port: return true

		// --- 2. Political Access Context-aware ---
		if (contextAllowedCountries.Count > 0)
		{
			// Destination is allowed -> Go ahead
			// ESCAPE RULE: Starting province is disallowed?
			// Allow one-step movement to find a way out.
			if (contextAllowedCountries.Contains(toOwner)
				|| !contextAllowedCountries.Contains(fromOwner))
			{
				return false;
			}

			// Restricted: Path tries to enter territory we don't have access to
			return true;
		}

		// Default: Allow if no context (used during graph initialization)
		return false;
	}
}
