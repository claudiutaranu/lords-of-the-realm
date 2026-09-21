class_name IdleAnimal
extends Node2D
## Premium idle life for a painted animal that never leaves its spot: irregular tail swishes that settle
## naturally, small head motion (grass tugs or slow looking around) and barely visible breathing.
## Everything is continuous math evaluated every frame — smooth at any frame rate, no sprite sheets.

const TEXTURE_TO_WORLD_SCALE: float = 0.5   # textures are authored at 2x for clean sub-pixel bending

@export var pose: RiggedPose
@export_group("Tail")
@export var tail_swish_degrees: float = 13.0
@export var tail_swish_seconds: float = 1.6
@export var min_seconds_between_swishes: float = 3.5
@export var max_seconds_between_swishes: float = 9.0
@export var tail_rest_sway_degrees: float = 1.5
@export_group("Head")
@export var graze_tug_degrees: float = 3.0
@export var graze_tug_seconds: float = 0.7
@export var min_seconds_between_tugs: float = 1.8
@export var max_seconds_between_tugs: float = 4.5
@export var look_around_degrees: float = 3.5
@export_group("Breathing")
@export var breath_seconds: float = 3.6
@export var breath_amount: float = 0.006

var _time: float = 0.0
var _phase_offset: float = 0.0
var _swish_start: float = -100.0
var _swish_direction: float = 1.0
var _next_swish_at: float = 0.0
var _tug_start: float = -100.0
var _next_tug_at: float = 0.0
var _body_material: ShaderMaterial
var _tail_material: ShaderMaterial

@onready var _visual: Node2D = $Visual
@onready var _body: Sprite2D = $Visual/Body
@onready var _tail: Sprite2D = $Visual/Tail


func _ready() -> void:
	assert(pose != null, "IdleAnimal '%s' needs a RiggedPose" % name)
	_phase_offset = randf() * 100.0
	_next_swish_at = randf_range(0.5, max_seconds_between_swishes)
	_next_tug_at = randf_range(0.0, max_seconds_between_tugs)
	_visual.scale = Vector2.ONE * TEXTURE_TO_WORLD_SCALE
	_body_material = _setup_layer(_body, pose.texture, pose.rig_weights)
	_tail.visible = pose.tail_texture != null
	if _tail.visible:
		_tail_material = _setup_layer(_tail, pose.tail_texture, pose.tail_rig_weights)


func _setup_layer(sprite: Sprite2D, texture: Texture2D, weights: Texture2D) -> ShaderMaterial:
	sprite.texture = texture
	sprite.offset = -pose.foot_point
	# Every animal animates independently, so each layer needs its own copy of the material's parameters.
	var material: ShaderMaterial = (sprite.material as ShaderMaterial).duplicate()
	sprite.material = material
	material.set_shader_parameter("rig_weights", weights)
	material.set_shader_parameter("tail_pivot_px", pose.tail_pivot)
	material.set_shader_parameter("head_pivot_px", pose.head_pivot)
	return material


func _process(delta: float) -> void:
	_time += delta
	_schedule_events()
	_body_material.set_shader_parameter("head_angle", _head_angle())
	if _tail_material != null:
		_tail_material.set_shader_parameter("tail_angle", _tail_angle())
	var breath: float = sin((_time + _phase_offset) * TAU / breath_seconds) * breath_amount
	_visual.scale = Vector2(1.0, 1.0 + breath) * TEXTURE_TO_WORLD_SCALE


func _schedule_events() -> void:
	if _time >= _next_swish_at:
		_swish_start = _time
		_swish_direction = -_swish_direction if randf() < 0.7 else _swish_direction
		_next_swish_at = _time + tail_swish_seconds + randf_range(min_seconds_between_swishes, max_seconds_between_swishes)
	if pose.is_grazing and _time >= _next_tug_at:
		_tug_start = _time
		_next_tug_at = _time + graze_tug_seconds + randf_range(min_seconds_between_tugs, max_seconds_between_tugs)


func _tail_angle() -> float:
	var rest: float = sin((_time + _phase_offset) * 0.9) * deg_to_rad(tail_rest_sway_degrees)
	var since_swish: float = _time - _swish_start
	if since_swish > tail_swish_seconds:
		return rest
	# One flick that overshoots back and settles: a damped swing, eased in so it never starts abruptly.
	var progress: float = since_swish / tail_swish_seconds
	var envelope: float = sin(progress * PI) * (1.0 - progress)
	var swing: float = sin(progress * TAU * 1.5)
	return rest + _swish_direction * deg_to_rad(tail_swish_degrees) * 1.8 * envelope * swing


func _head_angle() -> float:
	if not pose.is_grazing:
		var slow: float = sin((_time + _phase_offset) * 0.37) * 0.7 + sin((_time + _phase_offset) * 0.61) * 0.3
		return slow * deg_to_rad(look_around_degrees)
	var chew: float = sin((_time + _phase_offset) * 5.0) * deg_to_rad(0.35)
	var since_tug: float = _time - _tug_start
	if since_tug > graze_tug_seconds:
		return chew
	# A tug: head dips a little further into the grass, then eases back.
	var progress: float = since_tug / graze_tug_seconds
	var dip_direction: float = 1.0 if pose.faces_right else -1.0
	return chew + dip_direction * sin(progress * PI) * sin(progress * PI) * deg_to_rad(graze_tug_degrees)
