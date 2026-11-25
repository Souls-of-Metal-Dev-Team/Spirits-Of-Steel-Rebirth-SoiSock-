using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class SoiStar : AStar2D
{
	// NOTE(soi): I AM A SUPERSTAR

	public HashSet<string> context_allowed_countries = new();

	public override float _ComputeCost(long from_id, long to_id)
	{
		return GetPointPosition(from_id).DistanceTo(GetPointPosition(to_id));
	}

	public override float _EstimateCost(long from_id, long to_id)
	{
		return GetPointPosition(from_id).DistanceTo(GetPointPosition(to_id));
	}

	public override bool _FilterNeighbor(
		long from_id,
		long to_id
	)
	{
		var provinceObjects = MapManager.Instance.province_objects;
		if (!provinceObjects.TryGetValue((int)from_id, out var fromProv)
			|| !provinceObjects.TryGetValue((int)to_id, out var toProv))
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
		if (context_allowed_countries.Count > 0)
		{
			// Destination is allowed -> Go ahead
			// ESCAPE RULE: Starting province is disallowed?
			// Allow one-step movement to find a way out.
			if (context_allowed_countries.Contains(toOwner)
				|| !context_allowed_countries.Contains(fromOwner))
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
