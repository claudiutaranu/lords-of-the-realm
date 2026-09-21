// Wind on a painted camp: R = foliage sway (bushes, pines), G = canvas flutter (tent sheets, pinned at the top).
shader_type canvas_item;

uniform sampler2D ambient_mask : filter_linear, hint_default_black;
uniform float foliage_amplitude_px = 1.6;
uniform float cloth_amplitude_px = 1.8;
uniform float cloth_wavelength_px = 45.0;
uniform float cloth_speed = 0.7;
uniform float fold_shading = 0.07;

void fragment() {
	vec2 weights = texture(ambient_mask, UV).rg;
	vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
	float gust = 0.6 + 0.4 * sin(TIME * 0.31) * sin(TIME * 0.17 + 1.3);
	float wind = sin(TAU * (TIME * 0.22 - pixel.x / 320.0)) + 0.3 * sin(TIME * 1.9 + pixel.x * 0.07);
	float cloth_phase = TAU * (TIME * cloth_speed - pixel.y / cloth_wavelength_px + pixel.x / (cloth_wavelength_px * 3.0));
	vec2 offset = vec2(wind * foliage_amplitude_px * gust * weights.r, 0.0);
	offset += vec2(sin(cloth_phase), 0.35 * cos(cloth_phase * 0.7)) * cloth_amplitude_px * gust * weights.g;
	vec4 color = texture(TEXTURE, UV - offset * TEXTURE_PIXEL_SIZE);
	color.rgb *= 1.0 + cos(cloth_phase) * fold_shading * weights.g * gust;
	COLOR = color;
}
