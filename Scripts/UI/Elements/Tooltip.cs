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
		var vp_size = GetViewportRect().Size;
		Position = new Vector2(
			Mathf.Clamp(target.X, 0.0f, Mathf.Max(0.0f, vp_size.X - Size.X)),
			Mathf.Clamp(target.Y, 0.0f, Mathf.Max(0.0f, vp_size.Y - Size.Y)));
	}

	public void SwitchTooltip(int a_mode)
	{
		var map = MapManager.Instance;
		switch (a_mode)
		{
			case -1:
				shouldBeVisible = false;
				break;
			case 0:
			{
				tabs.CurrentTab = 0;
				foreach (var resource in resourcesList.GetChildren())
					resource.QueueFree();
				var prov = map.province_objects[map.current_hovered_pid];
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
				var prov = map.province_objects[map.current_hovered_pid];
				provinceName.Text = prov.name;
				provinceDescription.Text = prov.city;
				provinceInfrastructure.Text = $"{prov.infrastructure}/{prov.maxInfrastructure}";
				provinceConstructionIcon.Visible = EconomyManager.Instance.construction_queue.ContainsKey(map.current_hovered_pid);
				provinceOccupiedIcon.Visible = !string.IsNullOrEmpty(prov.occupier);
				provinceDestroyedIcon.Visible = false;
				var fmt_claims = "";
				foreach (var claim in prov.claims)
					fmt_claims += claim + "\n";
				provinceClaims.Text = fmt_claims;
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
