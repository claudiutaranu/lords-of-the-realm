// Bends a painted animal's tail and head/neck around their joints, driven by a weight map (R = tail, G = head).
// Each output pixel samples the texture rotated back by (angle * weight): 0 at the joint, full at the tip,
// so the part bends continuously instead of snapping as a rigid cut-out.
shader_type canvas_item;

uniform sampler2D rig_weights : filter_linear, hint_default_black;
uniform vec2 tail_pivot_px = vec2(0.0);
uniform vec2 head_pivot_px = vec2(0.0);
uniform float tail_angle = 0.0;
uniform float head_angle = 0.0;

vec2 rotate_around(vec2 point, vec2 pivot, float angle) {
	vec2 offset = point - pivot;
	float cos_a = cos(angle);
	float sin_a = sin(angle);
	return pivot + vec2(cos_a * offset.x - sin_a * offset.y, sin_a * offset.x + cos_a * offset.y);
}

void fragment() {
	vec2 texture_size = 1.0 / TEXTURE_PIXEL_SIZE;
	vec2 pixel = UV * texture_size;
	vec2 weights = texture(rig_weights, UV).rg;
	pixel = rotate_around(pixel, tail_pivot_px, -tail_angle * weights.r);
	pixel = rotate_around(pixel, head_pivot_px, -head_angle * weights.g);
	COLOR = texture(TEXTURE, pixel * TEXTURE_PIXEL_SIZE);
}
