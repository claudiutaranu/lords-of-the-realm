class_name TrackCart
extends Node2D
## A loaded cart and its pusher rolling out of the mine along the rails, parking, and going back in.
## Heavy start/stop easing, a small step bob while moving, and the tunnel's darkness lifting as it
## reaches the torch-lit mouth (a lighting change, not a fade: the cart is always fully there).

enum State { INSIDE, ROLLING_OUT, PARKED, ROLLING_IN }

@export var track_start: Vector2          ## inside the tunnel
@export var track_stop: Vector2           ## parking spot in the yard
@export var speed_px: float = 12.0
@export var min_parked_seconds: float = 5.0
@export var max_parked_seconds: float = 9.0
@export var min_inside_seconds: float = 4.0
@export var max_inside_seconds: float = 8.0
@export_range(0.0, 1.0) var tunnel_brightness: float = 0.3
@export_range(0.0, 1.0) var fully_lit_at: float = 0.3     ## fraction of the run where the torchlight is full
@export var step_rate: float = 1.8
@export var bob_px: float = 0.8

var _state: State = State.INSIDE
var _state_time: float = 0.0
var _state_length: float = 0.0

@onready var _visual: Node2D = $Visual


func _ready() -> void:
	_enter(State.INSIDE, randf_range(0.0, max_inside_seconds))
	_apply(0.0, false)


func _process(delta: float) -> void:
	_state_time += delta
	var progress: float = clampf(_state_time / _state_length, 0.0, 1.0)
	match _state:
		State.ROLLING_OUT:
			_apply(_eased(progress), true)
		State.ROLLING_IN:
			_apply(1.0 - _eased(progress), true)
		State.PARKED:
			_apply(1.0, false)
		State.INSIDE:
			_apply(0.0, false)
	if _state_time >= _state_length:
		_advance()


func _advance() -> void:
	var travel_seconds: float = track_start.distance_to(track_stop) / speed_px
	match _state:
		State.INSIDE:
			_enter(State.ROLLING_OUT, travel_seconds)
		State.ROLLING_OUT:
			_enter(State.PARKED, randf_range(min_parked_seconds, max_parked_seconds))
		State.PARKED:
			_enter(State.ROLLING_IN, travel_seconds)
		State.ROLLING_IN:
			_enter(State.INSIDE, randf_range(min_inside_seconds, max_inside_seconds))


func _apply(run_fraction: float, is_moving: bool) -> void:
	position = track_start.lerp(track_stop, run_fraction)
	var light: float = lerpf(tunnel_brightness, 1.0, clampf(run_fraction / fully_lit_at, 0.0, 1.0))
	_visual.modulate = Color(light, light, light, 1.0)
	_visual.position.y = -absf(sin(_state_time * step_rate * PI)) * bob_px if is_moving else 0.0


## Smoothstep: a loaded cart takes a moment to get going and to come to rest.
func _eased(progress: float) -> float:
	return progress * progress * (3.0 - 2.0 * progress)


func _enter(next_state: State, length: float) -> void:
	_state = next_state
	_state_time = 0.0
	_state_length = maxf(length, 0.001)
