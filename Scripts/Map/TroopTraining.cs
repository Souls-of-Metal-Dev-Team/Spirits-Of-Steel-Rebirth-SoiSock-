using Godot;
using Sosr;

namespace Sosr;

// Port of the CountryData.TroopTraining inner class.
// C# constructors with arguments are not exposed to GDScript's `.new()`, so
// GDScript call sites use TroopTraining.FromValues(...) instead.
[GlobalClass]
public partial class TroopTraining : RefCounted
{
    public int divisions_count;
    public string division_type = "";
    public int days_left;
    public double daily_cost;

    public TroopTraining() { }

    public static TroopTraining FromValues(int divisionsCount, string divisionType, int daysLeft, double dailyCost)
    {
        return new TroopTraining
        {
            divisions_count = divisionsCount,
            division_type = divisionType,
            days_left = daysLeft,
            daily_cost = dailyCost,
        };
    }
}
