using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

// Plain-data commands. Controllers submit these to ActionDispatcher instead of calling
// managers directly, so one day a network peer can submit the same record over the wire.
public abstract class GameAction
{
	public string Issuer = "";
	public abstract string Kind { get; }
}

public sealed class DeclareWarAction : GameAction
{
	public string Warer = "";
	public string Waree = "";
	public bool Silent;
	public override string Kind => "declare_war";
}

public sealed class CallToArmsAction : GameAction
{
	public string Caller = "";
	public string Target = "";
	public override string Kind => "call_to_arms";
}

public sealed class AnnexAction : GameAction
{
	public string Annexer = "";
	public string Annexee = "";
	public override string Kind => "annex";
}

public sealed class TransferProvinceAction : GameAction
{
	public int ProvinceId;
	public string NewOwner = "";
	public override string Kind => "transfer_province";
}

public sealed class OccupyProvinceAction : GameAction
{
	public int ProvinceId;
	public string Occupier = "";
	public override string Kind => "occupy_province";
}

public sealed class BuildAction : GameAction
{
	public int ProvinceId;
	public override string Kind => "build";
}

public sealed class BuildIndustryAction : GameAction
{
	public int ProvinceId;
	public string Country = "";
	public int Type;
	public override string Kind => "build_industry";
}

public sealed class DeployAction : GameAction
{
	public int ProvinceId;
	public override string Kind => "deploy";
}

// shortcut: ReadyTroop is a live object; replace with stable ids for network play.
public sealed class PlaceTroopAction : GameAction
{
	public string Country = "";
	public int ProvinceId;
	public ReadyTroop Batch;
	public override string Kind => "place_troop";
}

public sealed class QueueTrainingAction : GameAction
{
	public string Country = "";
	public int Count;
	public string Type = "infantry";
	public override string Kind => "queue_training";
}

public sealed class SelectCountryAction : GameAction
{
	public int ProvinceId;
	public override string Kind => "select_country";
}

public sealed class MoveTroopsAction : GameAction
{
	public List<Dictionary<string, object?>> Assignments = new();
	public override string Kind => "move_troops";
}

// shortcut: carries live TroopData refs because troop ownership is object-based today;
// replace with stable ids when actions cross the network.
public sealed class SplitTroopAction : GameAction
{
	public TroopData Original;
	public List<DivisionData> Divisions = new();
	public override string Kind => "split_troop";
}

public sealed class RemoveTroopAction : GameAction
{
	public TroopData Troop;
	public override string Kind => "remove_troop";
}
