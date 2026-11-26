using Godot;

namespace Sosr;

public partial class GovernmentUI : CanvasLayer
{
	private class GovernmentPosition
	{
		public string sigFig;
		public string position;
		public Label sigPos;
		public RichTextLabel sigName;
		public TextureRect sigPortrait;
		public TextureRect sigBlood;
		public OptionButton sigOpt;
		public RichTextLabel sigDesc;
	}

	private const string SKILLS_FORMAT =
		"[center][font_size=24]Traits[/font_size][/center][code][ul bullet=]\n[/ul][/code][hr]\n[center][font_size=24]Skills[/font_size][/center][code][ul bullet=]{0}\n[/ul][/code]";

	private CountryData country;
	[Export] public PanelContainer template;
	[Export] public HBoxContainer listOfPositions;
	private readonly System.Collections.Generic.Dictionary<string, GovernmentPosition> positions = new();

	private bool mWasPaused = false;

	public void MOnBackPressed()
	{
		SetGameUiVisible(true);
		if (!mWasPaused)
			GameState.Instance.currentWorld.clock.resume();

		Hide();

		country = null;
		positions.Clear();
		foreach (var child in listOfPositions.GetChildren())
			child.QueueFree();
	}

	private void SetGameUiVisible(bool aVisible)
	{
		if (GetTree().Root.FindChild("ui_game", true, false) is CanvasLayer gameUi)
			gameUi.Visible = aVisible;
	}

	private static void ShowFigure(GovernmentPosition govPos, ImportantFigure figure)
	{
		govPos.sigName.Text = figure.name;
		govPos.sigPortrait.Texture = (Texture2D)ImportantFigureHost.GetPortrait(figure);
		govPos.sigBlood.Visible = figure.status == ImportantFigure.Status.WOUNDED;
		govPos.sigDesc.Text = string.Format(SKILLS_FORMAT, figure.GetDisplaySkills());
	}

	public void OpenMenu(CountryData aCountry)
	{
		Show();
		country = aCountry;
		var figures = FigureManager.Instance.figures;

		foreach (var (position, holders) in aCountry.governmentPositions)
		{
			foreach (var iFigure in holders)
			{
				var govPos = new GovernmentPosition { sigFig = iFigure, position = position };
				var leader = (PanelContainer)template.Duplicate();
				govPos.sigPos = leader.GetNode<Label>("VBoxContainer/Label");
				govPos.sigPos.Text = position;
				govPos.sigName = leader.GetNode<RichTextLabel>("VBoxContainer/Header/ScrollContainer/Label");
				govPos.sigPortrait = leader.GetNode<TextureRect>("VBoxContainer/Picture/TextureRect");
				govPos.sigBlood = leader.GetNode<TextureRect>("VBoxContainer/Picture/TextureRect/Blood");
				govPos.sigOpt = leader.GetNode<OptionButton>("VBoxContainer/PanelContainer3/OptionButton2");
				foreach (var sigFig in aCountry.figures)
					govPos.sigOpt.AddItem(sigFig);
				govPos.sigDesc = leader.GetNode<RichTextLabel>("VBoxContainer/Display/RichTextLabel");
				if (holders.Count > 0)
				{
					var figure = figures[iFigure];
					ShowFigure(govPos, figure);
					for (var i = 0; i < govPos.sigOpt.ItemCount; i++)
					{
						if (govPos.sigOpt.GetItemText(i) == figure.name)
							govPos.sigOpt.Select(i);
					}
				}
				govPos.sigOpt.ItemSelected += aIndex =>
				{
					var figureName = govPos.sigOpt.GetItemText((int)aIndex);
					var figure = figures[figureName];
					ShowFigure(govPos, figure);
					country.SetGovernmentPosition(govPos.position, figure);
				};
				listOfPositions.AddChild(leader);
				leader.Visible = true;
				positions[position] = govPos;
			}
		}

		SetGameUiVisible(false);
		var clock = GameState.Instance.currentWorld.clock;
		mWasPaused = clock.paused;
		clock.pause();
	}
}
