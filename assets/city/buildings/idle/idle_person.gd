class_name IdlePerson
extends Node2D
## Someone taking a break. Most people simply stay as painted; the ones given a role come alive:
## TALK  - a slow weight shift and an expressive hand, in bursts of speech with pauses in between;
## NOD   - listens: a slow weight shift and a double nod whenever the partner stops to let a point land,
##         plus the odd nod of its own.

signal finished_speaking

enum Role { STILL, TALK, NOD }

const FADE_SECONDS: float = 0.35
const NOD_SECONDS: float = 0.55

@export var role: Role = Role.STILL
@export_group("Sway")
@export var sway_degrees: float = 1.2
@export var sway_period_seconds: float = 6.0
@export_group("Talking")
@export var gesture_degrees: float = 16.0
@export var min_talk_seconds: float = 2.5
@export var max_talk_seconds: float = 5.0
@export var min_pause_seconds: float = 1.2
@export var max_pause_seconds: float = 3.0
@export_group("Nodding")
@export var nod_degrees: float = 7.0
@export var partner: NodePath             ## the speaker this listener reacts to
@export var min_seconds_between_own_nods: float = 5.0
@export var max_seconds_between_own_nods: float = 11.0

var _time: float = 0.0
var _phase: float = 0.0
var _is_speaking: bool = false
var _segment_time: float = 0.0
var _segment_length: float = 0.0
var _nod_started_at: float = -100.0
var _nods_queued: int = 0
var _next_own_nod_at: float = 0.0
var _materials: Array[ShaderMaterial] = []

@onready var _visual: Node2D = $Visual


func _ready() -> void:
	set_process(role != Role.STILL)
	if role == Role.STILL:
		return
	_phase = randf() * TAU
	for child in _visual.get_children():
		var sprite := child as Sprite2D
		if sprite != null and sprite.material is ShaderMaterial:
			sprite.material = (sprite.material as ShaderMaterial).duplicate()   # every person moves independently
			_materials.append(sprite.material)
	_start_segment(false)
	_next_own_nod_at = randf_range(min_seconds_between_own_nods, max_seconds_between_own_nods)
	if role == Role.NOD and not partner.is_empty():
		var speaker := get_node_or_null(partner) as IdlePerson
		if speaker != null:
			speaker.finished_speaking.connect(_on_partner_finished_speaking)


func _process(delta: float) -> void:
	_time += delta
	var drift: float = 0.75 + 0.25 * sin(_time * 0.23 + _phase)
	var sway: float = deg_to_rad(sway_degrees) * drift * sin(_time * TAU / sway_period_seconds + _phase)
	var limb: float = _talk_angle(delta) if role == Role.TALK else _nod_angle()
	for material in _materials:
		material.set_shader_parameter("sway_angle", sway)
		material.set_shader_parameter("gesture_angle", limb)


func _talk_angle(delta: float) -> float:
	_segment_time += delta
	if _segment_time >= _segment_length:
		if _is_speaking:
			finished_speaking.emit()
		_start_segment(not _is_speaking)
	if not _is_speaking:
		return 0.0
	var envelope: float = minf(clampf(_segment_time / FADE_SECONDS, 0.0, 1.0), clampf((_segment_length - _segment_time) / FADE_SECONDS, 0.0, 1.0))
	# Two out-of-step rhythms: emphasis beats on top of a slower open-hand sweep, like real conversation.
	var beats: float = 0.55 * sin(_time * 4.2) + 0.45 * sin(_time * 2.3 + 1.0)
	return deg_to_rad(gesture_degrees) * envelope * beats


func _nod_angle() -> float:
	if _time >= _next_own_nod_at:
		_queue_nods(1)
		_next_own_nod_at = _time + randf_range(min_seconds_between_own_nods, max_seconds_between_own_nods)
	var since: float = _time - _nod_started_at
	if since > NOD_SECONDS:
		if _nods_queued <= 0:
			return 0.0
		_nods_queued -= 1
		_nod_started_at = _time
		since = 0.0
	var progress: float = since / NOD_SECONDS
	return -deg_to_rad(nod_degrees) * pow(sin(progress * PI), 2.0)   # chin down and back up, eased at both ends


func _queue_nods(count: int) -> void:
	_nods_queued = maxi(_nods_queued, count)


func _on_partner_finished_speaking() -> void:
	_queue_nods(2)


func _start_segment(speaking: bool) -> void:
	_is_speaking = speaking
	_segment_time = 0.0
	_segment_length = randf_range(min_talk_seconds, max_talk_seconds) if speaking else randf_range(min_pause_seconds, max_pause_seconds)
