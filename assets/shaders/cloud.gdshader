// One drifting cloud: a flat sheet high over the map whose shape is noise, not a texture, so the
// clouds cost no art and no two look alike (each instance gets its own seed).
shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_draw_never, shadows_disabled;

uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);
uniform float opacity = 0.5;   // driven from the season, and by the cloud's own fade in and out
uniform float seed = 0.0;
uniform float drift = 0.012;   // internal churn, separate from the cloud's movement across the map

varying vec2 sheet_uv;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 p) {
	vec2 cell = floor(p);
	vec2 offset = fract(p);
	offset = offset * offset * (3.0 - 2.0 * offset);
	return mix(
		mix(hash(cell), hash(cell + vec2(1.0, 0.0)), offset.x),
		mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0, 1.0)), offset.x),
		offset.y);
}

float fbm(vec2 p) {
	float total = 0.0;
	float amplitude = 0.5;
	for (int i = 0; i < 3; i++) {
		total += value_noise(p) * amplitude;
		p *= 2.03;
		amplitude *= 0.5;
	}
	return total;
}

void vertex() {
	sheet_uv = UV;
}

void fragment() {
	vec2 p = sheet_uv * 3.4 + vec2(seed * 17.3, seed * 9.1) + TIME * drift;
	float body = fbm(p);

	// Radial falloff, so a cloud fades out at its own edges instead of ending on the quad's border.
	float edge = smoothstep(0.5, 0.12, length(sheet_uv - vec2(0.5)));

	ALBEDO = tint;
	ALPHA = clamp(smoothstep(0.42, 0.72, body) * edge * opacity, 0.0, 1.0);
}
