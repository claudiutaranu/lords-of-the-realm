// Sea around the campaign map, with surf along the coast.
//
// There is no depth pass involved: the shader is handed the same height map the terrain is
// displaced by, so it knows the water's depth under any point (shore_depth, in world units,
// negative on land). Foam is then just a function of that depth and TIME — bands that travel
// shoreward and break where the ground comes up.
shader_type spatial;
render_mode cull_disabled, blend_mix;

uniform sampler2D height_map : filter_linear, repeat_disable;
uniform float height_scale = 20.0;
uniform float sea_level = 3.6;
uniform vec2 map_size = vec2(153.6, 102.4);

uniform vec3 deep_color : source_color = vec3(0.027, 0.086, 0.180);
uniform vec3 shallow_color : source_color = vec3(0.106, 0.333, 0.400);
uniform vec3 foam_color : source_color = vec3(0.92, 0.95, 0.96);

uniform float foam_reach = 1.5;      // how far out from the waterline surf is visible
uniform float wave_frequency = 4.5;  // bands per world unit of depth
uniform float wave_speed = 1.5;
uniform float swell_height = 0.07;

varying vec3 world_position;

// Water depth in world units: positive at sea, negative over land, large out past the map.
float shore_depth(vec3 position) {
	vec2 uv = position.xz / map_size + 0.5;
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
		return 100.0;
	}

	return sea_level - texture(height_map, uv).r * height_scale;
}

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Open water swells; near the beach it flattens, or the sea would saw through the sand.
	float depth = shore_depth(world_position);
	float open = clamp(depth / 2.5, 0.0, 1.0);
	VERTEX.y += sin(world_position.x * 0.22 + TIME * 0.8) * swell_height * open
		+ sin(world_position.z * 0.31 - TIME * 0.6) * swell_height * open;
}

void fragment() {
	float depth = shore_depth(world_position);

	// Bands keyed to depth rather than to position: they follow the coastline whatever shape it
	// takes, and travel inshore as TIME advances.
	float phase = depth * wave_frequency - TIME * wave_speed;
	float band = smoothstep(0.45, 0.95, sin(phase) * 0.5 + 0.5);
	float near_shore = 1.0 - clamp(depth / foam_reach, 0.0, 1.0);
	float rolling = band * near_shore * near_shore;

	// The waterline itself always has a little surf on it, breathing rather than static.
	float breaking = smoothstep(0.55, 0.0, depth) * (0.65 + 0.35 * sin(TIME * 1.7 + world_position.x * 0.8));

	float foam = clamp(max(rolling, breaking), 0.0, 1.0) * step(0.0, depth);

	vec3 water = mix(shallow_color, deep_color, clamp(depth / 3.0, 0.0, 1.0));
	ALBEDO = mix(water, foam_color, foam);

	// Fine chop, so the surface catches the sun instead of lying flat as glass.
	vec2 ripple = world_position.xz;
	vec3 normal = normalize(vec3(
		cos(ripple.x * 0.9 + TIME * 1.1) * 0.05 + cos(ripple.x * 2.3 - TIME * 0.7) * 0.02,
		1.0,
		cos(ripple.y * 1.1 - TIME * 0.9) * 0.05 + cos(ripple.y * 2.7 + TIME * 0.8) * 0.02));
	NORMAL = normalize((VIEW_MATRIX * vec4(normal, 0.0)).xyz);

	// Shallow water shows the sand through it; foam is opaque.
	ALPHA = clamp(max(0.5 + depth * 0.9, foam), 0.0, 1.0);
	ROUGHNESS = mix(0.72, 0.12, clamp(depth / 2.0, 0.0, 1.0));
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
