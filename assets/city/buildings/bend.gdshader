// Bends a cut-out limb about its joint: each pixel follows the rotation by its weight (R), 0 at the joint.
shader_type canvas_item;

uniform sampler2D bend_weights : filter_linear, hint_default_black;
uniform vec2 pivot_px = vec2(0.0);
uniform float angle = 0.0;

void fragment() {
	vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
	float rotation = -angle * texture(bend_weights, UV).r;
	vec2 offset = pixel - pivot_px;
	vec2 source = pivot_px + vec2(cos(rotation) * offset.x - sin(rotation) * offset.y,
	                              sin(rotation) * offset.x + cos(rotation) * offset.y);
	COLOR = texture(TEXTURE, source * TEXTURE_PIXEL_SIZE);
}
