using Godot;

namespace Sosr;

public partial class CountryManager : Node
{
    public Godot.Collections.Dictionary<string, RefCounted> countries = new();
    public string[] countryNames = [];
    public RefCounted player_country = new();

    // var Releasables: Array[ReleasableData] = []
    // var dead_countries: Dictionary[String, CountryData] = {}
}
