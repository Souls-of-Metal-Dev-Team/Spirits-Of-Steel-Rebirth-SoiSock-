using Godot;
using Sosr;

namespace Sosr;

// Both human players and AI controllers issue actions through this interface.
public interface IGameController
{
	string CountryName { get; }
	bool Issue(GameAction action);
}

// Local human controller. Holds the session state that used to live in GameState.
public partial class PlayerController : Node, IGameController
{
	public static PlayerController Instance => GameState.Instance.playerController;

	public GodotObject Country = CountryData.FromDict(new Godot.Collections.Dictionary { { "name", "Sea" } });

	public string CountryName => CountryData.NameOf(Country);

	public GameState.InteractionType InteractionMode = GameState.InteractionType.SELECT_COUNTRY;
	public GameState.IndustryType Industry = GameState.IndustryType.FACTORY;

	public CountryData SelectedCountry;

	public bool Issue(GameAction action) => ActionDispatcher.Instance.Submit(action);
}
