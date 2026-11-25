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

	public static DivisionTemplate FromDict(Godot.Collections.Dictionary a_data)
	{
		var divisionTemplate = new DivisionTemplate();

		divisionTemplate.name = a_data["name"].AsString();
		divisionTemplate.hp = a_data["hp"].AsSingle();
		divisionTemplate.manpower = a_data["manpower"].AsInt32();
		divisionTemplate.cost = a_data["cost"].AsInt32();
		divisionTemplate.days = a_data["days"].AsInt32();
		divisionTemplate.softAttack = a_data["softAttack"].AsSingle();
		divisionTemplate.hardAttack = a_data["hardAttack"].AsSingle();
		divisionTemplate.staticDefense = a_data["staticDefense"].AsSingle();
		divisionTemplate.offensiveDefense = a_data["offensiveDefense"].AsSingle();
		divisionTemplate.cohesion = a_data["cohesion"].AsSingle();
		divisionTemplate.resiliance = a_data["resiliance"].AsSingle();
		divisionTemplate.organization = a_data["organization"].AsSingle();
		divisionTemplate.speed = a_data["speed"].AsSingle();

		return divisionTemplate;
	}
}
