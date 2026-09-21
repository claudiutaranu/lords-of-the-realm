// Wind through painted conifers: crowns sway (mask = 0 at trunks/ground, 1 at the treetops), trunks stay put.
// One long wave crosses the whole stand so neighbouring trees move together, plus a small per-tree flutter.
shader_type canvas_item;

uniform sampler2D sway_mask : filter_linear, hint_default_black;
uniform float amplitude_px = 1.7;
uniform float wind_wavelength_px = 320.0;
uniform float wind_speed = 0.22;
uniform float flutter_amount = 0.3;

void fragment() {
	float weight = texture(sway_mask, UV).r;
	vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
	float gust = 0.6 + 0.4 * sin(TIME * 0.31) * sin(TIME * 0.17 + 1.3);
	float wind = sin(TAU * (TIME * wind_speed - pixel.x / wind_wavelength_px));
	float flutter = sin(TIME * 1.9 + pixel.x * 0.07) * flutter_amount;
	float offset = (wind + flutter) * amplitude_px * gust * weight;
	COLOR = texture(TEXTURE, UV - vec2(offset, 0.0) * TEXTURE_PIXEL_SIZE);
}
