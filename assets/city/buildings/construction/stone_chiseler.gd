class_name StoneChiseler
extends Node2D
## A mason working a block: wind-up, quick strike on the chisel, chips and dust on impact,
## a few strikes in a row, then a short breather. Timing is eased so it reads as effort, not as a loop.

enum Phase { REST, WIND_UP, STRIKE, HOLD, RECOVER }

@export var wind_up_degrees: float = -8.0
@export var strike_degrees: float = 6.0
@export var wind_up_seconds: float = 0.5
@export var strike_seconds: float = 0.09
@export var hold_seconds: float = 0.08
@export var recover_seconds: float = 0.35
@export var min_strikes_in_a_row: int = 3
@export var max_strikes_in_a_row: int = 7
@export var min_rest_seconds: float = 1.2
@export var max_rest_seconds: float = 3.0

var _phase: Phase = Phase.REST
var _phase_time: float = 0.0
var _phase_length: float = 0.0
var _strikes_left: int = 0
var _arm_material: ShaderMaterial

@onready var _arm: Sprite2D = $Arm
@onready var _chips: CPUParticles2D = $Chips
@onready var _dust: CPUParticles2D = $Dust


func _ready() -> void:
	_arm_material = (_arm.material as ShaderMaterial).duplicate()
	_arm.material = _arm_material
	_enter(Phase.REST, randf_range(0.0, max_rest_seconds))


func _process(delta: float) -> void:
	_phase_time += delta
	var progress: float = clampf(_phase_time / _phase_length, 0.0, 1.0)
	_arm_material.set_shader_parameter("angle", deg_to_rad(_arm_degrees(progress)))
	if _phase_time >= _phase_length:
		_advance()


func _arm_degrees(progress: float) -> float:
	match _phase:
		Phase.WIND_UP:
			return lerpf(0.0, wind_up_degrees, ease(progress, -2.0))
		Phase.STRIKE:
			return lerpf(wind_up_degrees, strike_degrees, ease(progress, 2.4))
		Phase.HOLD:
			return strike_degrees
		Phase.RECOVER:
			return lerpf(strike_degrees, 0.0, ease(progress, -1.8))
	return 0.0


func _advance() -> void:
	match _phase:
		Phase.REST:
			_strikes_left = randi_range(min_strikes_in_a_row, max_strikes_in_a_row)
			_enter(Phase.WIND_UP, wind_up_seconds)
		Phase.WIND_UP:
			_enter(Phase.STRIKE, strike_seconds)
		Phase.STRIKE:
			_chips.restart()
			_dust.restart()
			_enter(Phase.HOLD, hold_seconds)
		Phase.HOLD:
			_enter(Phase.RECOVER, recover_seconds)
		Phase.RECOVER:
			_strikes_left -= 1
			if _strikes_left > 0:
				_enter(Phase.WIND_UP, wind_up_seconds * randf_range(0.9, 1.15))
			else:
				_enter(Phase.REST, randf_range(min_rest_seconds, max_rest_seconds))


func _enter(next_phase: Phase, length: float) -> void:
	_phase = next_phase
	_phase_time = 0.0
	_phase_length = maxf(length, 0.001)
