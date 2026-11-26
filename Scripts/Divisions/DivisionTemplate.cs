using Godot;
using Sosr;

namespace Sosr;

[GlobalClass]
public partial class DivisionTemplate : RefCounted
{
	public string name = "";
	public float hp;
	public int manpower;
	public int cost;
	public int days;
	public float softAttack;
	public float hardAttack;
	public float staticDefense;
	public float offensiveDefense;
	public float cohesion;
	public float resiliance;
	public float organization;
	public float speed;

	public static DivisionTemplate FromDict(Godot.Collections.Dictionary aData)
	{
		var divisionTemplate = new DivisionTemplate();

		divisionTemplate.name = aData["name"].AsString();
		divisionTemplate.hp = aData["hp"].AsSingle();
		divisionTemplate.manpower = aData["manpower"].AsInt32();
		divisionTemplate.cost = aData["cost"].AsInt32();
		divisionTemplate.days = aData["days"].AsInt32();
		divisionTemplate.softAttack = aData["softAttack"].AsSingle();
		divisionTemplate.hardAttack = aData["hardAttack"].AsSingle();
		divisionTemplate.staticDefense = aData["staticDefense"].AsSingle();
		divisionTemplate.offensiveDefense = aData["offensiveDefense"].AsSingle();
		divisionTemplate.cohesion = aData["cohesion"].AsSingle();
		divisionTemplate.resiliance = aData["resiliance"].AsSingle();
		divisionTemplate.organization = aData["organization"].AsSingle();
		divisionTemplate.speed = aData["speed"].AsSingle();

		return divisionTemplate;
	}
}
