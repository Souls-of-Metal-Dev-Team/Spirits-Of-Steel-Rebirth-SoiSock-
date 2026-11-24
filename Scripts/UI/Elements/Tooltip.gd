class_name Tooltip extends PanelContainer

func _enter_tree() -> void:
	GameState.tooltip = self
	visible = false

@export var offset: Vector2 = Vector2.ZERO

@export var tabs: TabContainer
@export var resourceTemplate: HBoxContainer
@export var resourcesList: VBoxContainer
@export var provinceName: Label
@export var provinceDescription: RichTextLabel
@export var provinceInfrastructure: Label
@export var provinceConstructionIcon: TextureRect
@export var provinceOccupiedIcon: TextureRect
@export var provinceDestroyedIcon: TextureRect
@export var provinceClaims: Label

var shouldBeVisible: bool = false:
	set(value):
		shouldBeVisible = value
		visible = value

func _process(_delta: float) -> void:
	if !visible:
		return
	_clamp_to_viewport(get_global_mouse_position() + offset)

func _clamp_to_viewport(target: Vector2) -> void:
	var vp_size := get_viewport_rect().size
	position = Vector2(
		clamp(target.x, 0.0, maxf(0.0, vp_size.x - size.x)),
		clamp(target.y, 0.0, maxf(0.0, vp_size.y - size.y)),
	)

func SwitchTooltip(a_mode: int) -> void:
	match a_mode:
		-1:
			shouldBeVisible = false
		0:
			tabs.current_tab = 0
			for resource: HBoxContainer in resourcesList.get_children():
				resource.queue_free()
			var prov: Province = MapManager.province_objects[MapManager.current_hovered_pid]
			for resource: ResourceNode in prov.resources:
				var resourceBop: HBoxContainer = resourceTemplate.duplicate()
				resourceBop.get_node("TextureRect").texture = MapManager.GetResourceIcon(resource.type)
				resourceBop.get_node("Label").text = "%s - %d (%.2f%%)" % [resource.type, resource.amount, 100 * resource.quality]
				resourceBop.visible = true
				resourcesList.add_child(resourceBop)
			_ShowAfterLayout()
		1:
			tabs.current_tab = 1
			var prov: Province = MapManager.province_objects[MapManager.current_hovered_pid]
			provinceName.text = prov.name
			provinceDescription.text = prov.city
			provinceInfrastructure.text = "%d/%d" % [prov.infrastructure, prov.maxInfrastructure]
			provinceConstructionIcon.visible = MapManager.current_hovered_pid in EconomyManager.construction_queue
			provinceOccupiedIcon.visible = !prov.occupier.is_empty()
			provinceDestroyedIcon.visible = false
			var fmt_claims: String = ""
			for claim in prov.claims:
				fmt_claims += claim + "\n"
			provinceClaims.text = fmt_claims
			_ShowAfterLayout()

func _ShowAfterLayout() -> void:
	reset_size()
	shouldBeVisible = true
	_clamp_to_viewport(get_global_mouse_position() + offset)
