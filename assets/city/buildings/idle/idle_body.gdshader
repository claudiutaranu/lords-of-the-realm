// Idle life on a painted person: R = weight-shift sway about the waist, G = a limb gesturing about its joint.
// The limb layer carries both weights, so a gesturing hand also follows the body's sway.
shader_type canvas_item;

uniform sampler2D motion_weights : filter_linear, hint_default_black;
uniform vec2 sway_pivot_px = vec2(0.0);
uniform float sway_angle = 0.0;
uniform vec2 gesture_pivot_px = vec2(0.0);
uniform float gesture_angle = 0.0;

vec2 rotate_about(vec2 point, vec2 pivot, float angle) {
	vec2 local = point - pivot;
	return pivot + vec2(cos(angle) * local.x - sin(angle) * local.y, sin(angle) * local.x + cos(angle) * local.y);
}

void fragment() {
	vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
	float sway_weight = texture(motion_weights, UV).r;
	pixel = rotate_about(pixel, sway_pivot_px, -sway_angle * sway_weight);
	float gesture_weight = texture(motion_weights, pixel * TEXTURE_PIXEL_SIZE).g;
	pixel = rotate_about(pixel, gesture_pivot_px, -gesture_angle * gesture_weight);
	COLOR = texture(TEXTURE, pixel * TEXTURE_PIXEL_SIZE);
}
