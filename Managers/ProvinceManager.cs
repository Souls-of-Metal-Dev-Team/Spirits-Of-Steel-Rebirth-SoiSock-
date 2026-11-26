using System.Collections.Generic;
using Godot;
using Sosr;

namespace Sosr;

// Owns province simulation state. MapManager keeps rendering and the existing
// mutation systems; it reaches this state through forwarding properties.
public partial class ProvinceManager : Node
{
	public static ProvinceManager Instance => GameState.Instance.provinceManager;

	public Dictionary<int, Province> Provinces = new();
	public Dictionary<string, List<int>> CountryToProvinces = new();
	public Dictionary<string, List<int>> CountryToOwnedProvinces = new();
	public Dictionary<string, List<int>> CountryToOccupiedProvinces = new();
	public Dictionary<string, List<int>> CountryToRightfulProvinces = new();
	public Dictionary<string, List<int>> CountryToCities = new();
	public Dictionary<string, List<int>> AllowedPids = new();
	public Dictionary<string, List<int>> GlobalClaimsRegistry = new();

	// Facade methods used by the action layer. Systems still live in MapManager.
	public void TransferOwnership(int pid, string newOwner) => MapManager.Instance.TransferOwnership(pid, newOwner);

	public void OccupyProvince(int pid, string occupier) => MapManager.Instance.OccupyProvince(pid, occupier);

	public void AnnexCountry(string annexer, string annexee) => MapManager.Instance.AnnexCountry(annexer, annexee);
}
