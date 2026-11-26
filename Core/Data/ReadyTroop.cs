namespace Sosr;

public sealed class ReadyTroop
{
	public DivisionData? division;
	public int count;

	public static ReadyTroop FromValues(DivisionData division, int count) => new()
	{
		division = division,
		count = count,
	};
}
