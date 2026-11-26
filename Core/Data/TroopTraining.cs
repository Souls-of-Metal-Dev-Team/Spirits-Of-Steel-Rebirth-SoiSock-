namespace Sosr;

public sealed class TroopTraining
{
	public int divisionsCount;
	public string divisionType = "";
	public int daysLeft;
	public double dailyCost;

	public static TroopTraining FromValues(int divisionsCount, string divisionType, int daysLeft, double dailyCost) => new()
	{
		divisionsCount = divisionsCount,
		divisionType = divisionType,
		daysLeft = daysLeft,
		dailyCost = dailyCost,
	};
}
