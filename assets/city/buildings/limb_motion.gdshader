// Moves a cut-out limb group (arms + tool): lift by offset_px and tilt by angle about pivot_px,
// each pixel scaled by its weight (R): 0 at the shoulders, 1 at forearms/tool -> upper arms stretch smoothly.
shader_type canvas_item;

uniform sampler2D motion_weights : filter_linear, hint_default_black;
uniform vec2 pivot_px = vec2(0.0);
uniform float angle = 0.0;
uniform vec2 offset_px = vec2(0.0);

void fragment() {
	vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
	float weight = texture(motion_weights, UV).r;
	pixel -= offset_px * weight;
	float rotation = -angle * weight;
	vec2 local = pixel - pivot_px;
	vec2 source = pivot_px + vec2(cos(rotation) * local.x - sin(rotation) * local.y,
	                              sin(rotation) * local.x + cos(rotation) * local.y);
	COLOR = texture(TEXTURE, source * TEXTURE_PIXEL_SIZE);
}
