class_name LanternGlow
extends Sprite2D
## Warm additive glow over a painted lantern: layered flicker (slow breathing + quick licks) like a real flame.

@export var base_energy: float = 0.75
@export var flicker_amount: float = 0.22
@export var base_scale: float = 1.0

var _time: float = 0.0
var _seed: float = 0.0


func _ready() -> void:
	_seed = randf() * 100.0
	var additive := CanvasItemMaterial.new()
	additive.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	material = additive


func _process(delta: float) -> void:
	_time += delta
	var t: float = _time + _seed
	var flicker: float = sin(t * 2.1) * 0.45 + sin(t * 7.3) * 0.35 + sin(t * 13.7 + 1.1) * 0.2
	var energy: float = base_energy * (1.0 + flicker * flicker_amount)
	modulate.a = clampf(energy, 0.0, 1.0)
	scale = Vector2.ONE * base_scale * (1.0 + flicker * 0.04)
