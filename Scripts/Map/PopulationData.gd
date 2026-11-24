extends RefCounted
class_name PopulationData

var ethnicity: String
var culture: String
var amount: int

static func FromDict(a_data: Dictionary) -> PopulationData:
	var population: PopulationData = PopulationData.new()
	
	population.ethnicity = a_data.get("ethnicity", "unknown")
	population.culture = a_data.get("culture", "unknown")
	population.amount = a_data["amount"]
	
	return population

func ToDict() -> Dictionary:
	return {
		"ethnicity": self.ethnicity,
		"culture": self.culture,
		"amount": self.amount
	}
