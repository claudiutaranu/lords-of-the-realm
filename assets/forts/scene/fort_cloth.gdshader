// Wind on painted flags and banners, applied to the original building sprite (the texture is untouched).
// cloth_mask R = flags on poles (0 at the pole -> 1 at the fly end), G = hanging banners (0 at the rod -> 1 at the tip),
// B = pixels handed over to separate moving layers (crane loads), A = torch/brazier flames.
shader_type canvas_item;

uniform sampler2D cloth_mask : filter_linear, hint_default_black;
uniform float flag_direction = 1.0;          // fallback only: each flag's direction is read from its own mask ramp
uniform float flag_amplitude_px = 7.0;
uniform float flag_wavelength_px = 70.0;
uniform float flag_speed = 1.1;
uniform float banner_amplitude_px = 3.5;
uniform float banner_wavelength_px = 90.0;
uniform float banner_speed = 0.6;
uniform float fold_shading = 0.13;

// The wall this runs on is dissolved into the next one by tweening the node's modulate, and the only
// place that reaches a canvas shader is the vertex colour — so it is caught there and carried down.
varying vec4 tint;

void vertex() {
	tint = COLOR;
}
uniform float flame_lick_px = 1.8;
uniform float flame_flicker = 0.28;

void fragment() {
	vec4 mask = texture(cloth_mask, UV);
	vec2 weights = mask.rg;
	vec2 pixel = UV / TEXTURE_PIXEL_SIZE;
	float gust = 0.65 + 0.35 * sin(TIME * 0.37) * sin(TIME * 0.19 + 0.8);

	// Flag: ripples run from the pole to the fly end; the cloth moves mostly up/down and bunches slightly.
	// The flag weight grows away from the pole, so its horizontal slope says which way this flag flies.
	vec2 step_x = vec2(3.0 * TEXTURE_PIXEL_SIZE.x, 0.0);
	float slope = texture(cloth_mask, UV + step_x).r - texture(cloth_mask, UV - step_x).r;
	float direction = abs(slope) > 0.002 ? sign(slope) : flag_direction;
	float flag_phase = TAU * (TIME * flag_speed - direction * pixel.x / flag_wavelength_px);
	vec2 flag_offset = vec2(0.3 * cos(flag_phase), sin(flag_phase)) * flag_amplitude_px * gust * weights.r;

	// Banner: a gentle sideways ripple travelling down from the rod.
	float banner_phase = TAU * (TIME * banner_speed - pixel.y / banner_wavelength_px);
	vec2 banner_offset = vec2(sin(banner_phase), 0.0) * banner_amplitude_px * gust * weights.g;

	// Flames: tongues lick sideways and upward, brightness flickers on two fast, out-of-step rhythms.
	float lick = sin(TIME * 9.0 + pixel.y * 0.25) * 0.6 + sin(TIME * 14.3 + pixel.x * 0.3) * 0.4;
	vec2 flame_offset = vec2(lick, -abs(sin(TIME * 6.0 + pixel.x * 0.2))) * flame_lick_px * mask.a;

	vec4 color = texture(TEXTURE, UV - (flag_offset + banner_offset + flame_offset) * TEXTURE_PIXEL_SIZE);
	float shade = cos(flag_phase) * weights.r + cos(banner_phase) * weights.g * 0.7;
	color.rgb *= 1.0 + shade * fold_shading * gust;
	color.rgb *= 1.0 + mask.a * flame_flicker * (0.6 * sin(TIME * 11.0) + 0.4 * sin(TIME * 17.0 + pixel.y * 0.1));
	color.a *= 1.0 - mask.b;

	COLOR = color * tint;
}
