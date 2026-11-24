extends RefCounted
class_name DivisionTemplate

var name: String
var hp: float
var manpower: int
var cost: int
var days: int
var softAttack: float
var hardAttack: float
var staticDefense: float
var offensiveDefense: float
var cohesion: float
var resiliance: float
var organization: float
var speed: float

static func FromDict(a_data: Dictionary) -> DivisionTemplate:
	var divisionTemplate: DivisionTemplate = DivisionTemplate.new()
	
	divisionTemplate.name             = a_data["name"]
	divisionTemplate.hp               = a_data["hp"]
	divisionTemplate.manpower         = a_data["manpower"]
	divisionTemplate.cost             = a_data["cost"]
	divisionTemplate.days             = a_data["days"]
	divisionTemplate.softAttack       = a_data["softAttack"]
	divisionTemplate.hardAttack       = a_data["hardAttack"]
	divisionTemplate.staticDefense    = a_data["staticDefense"]
	divisionTemplate.offensiveDefense = a_data["offensiveDefense"]
	divisionTemplate.cohesion         = a_data["cohesion"]
	divisionTemplate.resiliance       = a_data["resiliance"]
	divisionTemplate.organization     = a_data["organization"]
	divisionTemplate.speed            = a_data["speed"]

	return divisionTemplate
