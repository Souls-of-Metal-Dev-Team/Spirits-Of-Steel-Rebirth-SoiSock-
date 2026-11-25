extends RefCounted

const TEMPLATES := {
	"infantry": {
		"hp": 100.0, "manpower": 10000, "cost": 500,
		"days": 9, "attack": 1, "defense": 1, "speed": 1.0,
	},
	"tank": {
		"hp": 280.0, "manpower": 20000, "cost": 10000,
		"days": 30, "attack": 5, "defense": 7, "speed": 2.5,
	},
	"artillery": {
		"hp": 50.0, "manpower": 1000, "cost": 10000,
		"days": 15, "attack": 5, "defense": 0.3, "speed": 0.8,
	},
}
