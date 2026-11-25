using Godot;
using Sosr;

namespace Sosr;

// Port of the CountryData.ReadyTroop inner class.
// C# constructors with arguments are not exposed to GDScript's `.new()`, so
// GDScript call sites use ReadyTroop.FromValues(...) instead.
[GlobalClass]
public partial class ReadyTroop : RefCounted
{
    public DivisionData division;
    public int count;

    public ReadyTroop() { }

    public static ReadyTroop FromValues(DivisionData division, int count)
    {
        return new ReadyTroop
        {
            division = division,
            count = count,
        };
    }
}
