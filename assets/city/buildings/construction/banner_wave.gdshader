// Waves a painted banner in place: horizontal ripple travelling down the cloth, pinned at the bar (mask = 0),
// strongest at the tip (mask = 1), with fold shading so it reads as cloth, not as a wobbling image.
shader_type canvas_item;

uniform sampler2D wave_mask : filter_linear, hint_default_black;
uniform float amplitude_px = 2.6;
uniform float wavelength_px = 60.0;
uniform float waves_per_second = 0.8;
uniform float fold_shading = 0.09;

void fragment() {
	float weight = texture(wave_mask, UV).r;
	vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
	float gust = 0.7 + 0.3 * sin(TIME * 0.43);
	float phase = TAU * (TIME * waves_per_second - pixel.y / wavelength_px);
	float offset = sin(phase) * amplitude_px * weight * gust;
	vec4 color = texture(TEXTURE, UV - vec2(offset, 0.0) * TEXTURE_PIXEL_SIZE);
	color.rgb *= 1.0 + cos(phase) * fold_shading * weight * gust;
	COLOR = color;
}
