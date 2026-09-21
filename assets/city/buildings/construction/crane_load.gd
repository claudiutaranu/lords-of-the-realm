class_name CraneLoad
extends Node2D
## A load hanging from a pulley: slow pendulum sway whose strength drifts, like wind on a heavy stone.

@export var sway_degrees: float = 7.0
@export var period_seconds: float = 3.8

var _time: float = 0.0
var _phase: float = 0.0


func _ready() -> void:
	_phase = randf() * TAU


func _process(delta: float) -> void:
	_time += delta
	var strength: float = 0.7 + 0.3 * sin(_time * 0.21 + _phase)
	rotation = deg_to_rad(sway_degrees) * strength * sin(_time * TAU / period_seconds + _phase)
