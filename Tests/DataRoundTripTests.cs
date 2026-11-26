using System.Collections.Generic;
using Xunit;

namespace Sosr.Tests;

public class DataRoundTripTests
{
	private static Dictionary<string, object?> D(string json) =>
		(Dictionary<string, object?>)StateJson.Parse(json)!;

	private static void RoundTrip(string json, System.Func<Dictionary<string, object?>, Dictionary<string, object?>> parse)
	{
		var source = D(json);
		var result = parse(source);
		Assert.True(StateValue.Equal(source, result), StateJson.Format(result));
	}

	[Fact]
	public void LeafDataRoundTrips()
	{
		RoundTrip("""{"name":"X","color":"#FF0000"}""", d => BiomeData.FromDict(d).ToDict());
		RoundTrip("""{"name":"R","color":"#00FF00","icon":"i","tags":{"fuel":2}}""", d => ResourceData.FromDict(d).ToDict());
		RoundTrip("""{"type":"f","state":1,"durability":0.5}""", d => BuildingData.FromDict(d).ToDict());
		RoundTrip("""{"ethnicity":"e","culture":"c","amount":7}""", d => PopulationData.FromDict(d).ToDict());
		RoundTrip("""{"type":"oil","amount":3,"quality":1.5}""", d => ResourceNode.FromDict(d).ToDict());
		RoundTrip("""{"data":{"name":"R"},"claims":[1,2]}""", d => ReleasableData.FromDict(d).ToDict());
		RoundTrip("""{"polity":"C","status":"Leader"}""", d => FactionMember.FromDict(d).ToDict());
		RoundTrip("""{"final_position":[1,2],"drift_amount":0.5}""", d => IdeologyDriftTarget.FromDict(d).ToDict());
	}

	[Fact]
	public void BuildingTemplateRoundTrips()
	{
		var source = D("""{"name":"t","power":2,"functionalities":[],"assigned_inputs":{}}""");
		var result = BuildingTemplate.FromDict(source).ToDict();
		Assert.Equal("t", result["name"]);
		Assert.Equal(2.0, result["power"]);
	}

	[Fact]
	public void ProvinceOwnClaimCollapses()
	{
		var source = D("""{"type":1,"name":"P","polity":"C","resources":[],"buildings":[],"populations":[]}""");
		var province = Province.FromDict(source);
		Assert.Empty(province.ToDict()["claims"].List());

		province.claims = new[] { "C" };
		Assert.Empty(province.ToDict()["claims"].List());

		province.claims = new[] { "C", "D" };
		Assert.Equal(2, province.ToDict()["claims"].List().Count);
	}

	[Fact]
	public void FactionAndFigureRoundTrip()
	{
		var faction = FactionData.FromDict(D(
			"""{"name":"F","color":"#0000FF","members":[{"polity":"C","status":"Leader"}]}"""));
		Assert.Equal("#0000FF", faction.color);
		Assert.Single(faction.members);
		Assert.True(StateValue.Equal(faction.ToDict()["members"], StateJson.Parse("""[{"polity":"C","status":"Leader"}]""")));

		var figure = ImportantFigure.FromDict(D(
			"""{"name":"A","skills":{},"traits":[],"ideology":[1,2],"allegiance":"C","occupation":"x","legitimacy":90,"portrait_path":"","status":1}"""));
		var dict = figure.ToDict();
		Assert.True(StateValue.Equal(dict["ideology"], StateJson.Parse("""[1,2]""")));
		Assert.Equal(1.0, dict["status"]);
	}

	[Fact]
	public void TroopAndDivisionRoundTrip()
	{
		var division = DivisionData.FromDict(D("""{"name":"Infantry Division","type":"tank","hp":280}"""));
		Assert.Equal(280f, division.hp);
		Assert.Equal("tank", division.ToDict()["type"]);

		var troop = TroopData.FromDict(D("""{"country_name":"C","divisions":[],"is_moving":false,"path":[],"target_position":[0,0],"progress":0}"""));
		Assert.Equal("C", troop.ToDict()["country_name"]);
		Assert.False((bool)troop.ToDict()["is_moving"]!);
	}
}
