using Godot;

namespace Sosr;

[GlobalClass]
public partial class Tooltip : PanelContainer
{
	public override void _EnterTree()
	{
		GameState.Instance.tooltip = this;
		Visible = false;
	}

	[Export] public Vector2 offset = Vector2.Zero;

	[Export] public TabContainer tabs;
	[Export] public HBoxContainer resourceTemplate;
	[Export] public VBoxContainer resourcesList;
	[Export] public Label provinceName;
	[Export] public RichTextLabel provinceDescription;
	[Export] public Label provinceInfrastructure;
	[Export] public TextureRect provinceConstructionIcon;
	[Export] public TextureRect provinceOccupiedIcon;
	[Export] public TextureRect provinceDestroyedIcon;
	[Export] public Label provinceClaims;

	private bool _shouldBeVisible = false;
	public bool shouldBeVisible
	{
		get => _shouldBeVisible;
		set
		{
			_shouldBeVisible = value;
			Visible = value;
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		_clamp_to_viewport(GetGlobalMousePosition() + offset);
	}

	private void _clamp_to_viewport(Vector2 target)
	{
		var vpSize = GetViewportRect().Size;
		Position = new Vector2(
			Mathf.Clamp(target.X, 0.0f, Mathf.Max(0.0f, vpSize.X - Size.X)),
			Mathf.Clamp(target.Y, 0.0f, Mathf.Max(0.0f, vpSize.Y - Size.Y)));
	}

	public void SwitchTooltip(int aMode)
	{
		var map = MapManager.Instance;
		switch (aMode)
		{
			case -1:
				shouldBeVisible = false;
				break;
			case 0:
			{
				tabs.CurrentTab = 0;
				foreach (var resource in resourcesList.GetChildren())
					resource.QueueFree();
				var prov = map.provinceObjects[map.currentHoveredPid];
				foreach (var resource in prov.resources)
				{
					var resourceBop = (HBoxContainer)resourceTemplate.Duplicate();
					resourceBop.GetNode<TextureRect>("TextureRect").Texture = map.GetResourceIcon(resource.type);
					resourceBop.GetNode<Label>("Label").Text =
						$"{resource.type} - {resource.amount} ({100 * resource.quality:F2}%)";
					resourceBop.Visible = true;
					resourcesList.AddChild(resourceBop);
				}
				_ShowAfterLayout();
				break;
			}
			case 1:
			{
				tabs.CurrentTab = 1;
				var prov = map.provinceObjects[map.currentHoveredPid];
				provinceName.Text = prov.name;
				provinceDescription.Text = prov.city;
				provinceInfrastructure.Text = $"{prov.infrastructure}/{prov.maxInfrastructure}";
				provinceConstructionIcon.Visible = EconomyManager.Instance.constructionQueue.ContainsKey(map.currentHoveredPid);
				provinceOccupiedIcon.Visible = !string.IsNullOrEmpty(prov.occupier);
				provinceDestroyedIcon.Visible = false;
				var fmtClaims = "";
				foreach (var claim in prov.claims)
					fmtClaims += claim + "\n";
				provinceClaims.Text = fmtClaims;
				_ShowAfterLayout();
				break;
			}
		}
	}

	private void _ShowAfterLayout()
	{
		ResetSize();
		shouldBeVisible = true;
		_clamp_to_viewport(GetGlobalMousePosition() + offset);
	}
}
