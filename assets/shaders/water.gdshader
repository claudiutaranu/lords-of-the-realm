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

uniform float foam_reach = 0.65;     // how far out from the waterline surf is visible
uniform float wave_frequency = 4.5;  // bands per world unit of depth
uniform float wave_speed = 1.5;
uniform float swell_height = 0.07;

// 0 spring, 1 summer, 2 autumn, 3 winter — MapWater.SetSeason, from the same turn change the
// terrain hears. Only winter shows on the sea: a cold, flat, slate-grey version of it.
uniform float season = 1.0;

varying vec3 world_position;

float winter_weight() {
	return clamp(1.0 - abs(season - 3.0), 0.0, 1.0);
}

// Water depth in world units: positive at sea, negative over land, large out past the map.
float shore_depth(vec3 position) {
	vec2 uv = position.xz / map_size + 0.5;
	// Past the map's edge the sea simply keeps getting deeper, rather than jumping to open ocean:
	// the hard step used here drew a straight line across the water where the height map stopped.
	vec2 inside = clamp(uv, vec2(0.0), vec2(1.0));
	float beyond = length((uv - inside) * map_size);
	return sea_level - texture(height_map, inside).r * height_scale + beyond;
}

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Open water swells; near the beach it flattens, or the sea would saw through the sand.
	float depth = shore_depth(world_position);
	float open = clamp(depth / 2.5, 0.0, 1.0) * mix(1.0, 0.45, winter_weight());
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
	// Surf is a line along the shore, not a field: on the gentle shelf around an islet a wide band
	// turned into a grey slab wherever the island's shadow fell across it.
	float breaking = smoothstep(0.22, 0.0, depth) * (0.65 + 0.35 * sin(TIME * 1.7 + world_position.x * 0.8));

	float foam = clamp(max(rolling, breaking), 0.0, 1.0) * step(0.0, depth);

	// Winter: the shallows go over to grey ice, the deep water to slate, and the surf loses the
	// summer glare that comes off it.
	float winter = winter_weight();
	vec3 water = mix(
		mix(shallow_color, vec3(0.50, 0.57, 0.60), winter),
		mix(deep_color, vec3(0.06, 0.10, 0.15), winter),
		clamp(depth / 3.0, 0.0, 1.0));
	ALBEDO = mix(water, mix(foam_color, vec3(0.80, 0.85, 0.88), winter), foam);
	// Foam keeps its brightness in shadow: unlit white water reads as grey stone otherwise.
	EMISSION = foam_color * foam * 0.35;

	// Fine chop, so the surface catches the sun instead of lying flat as glass.
	vec2 ripple = world_position.xz;
	vec3 normal = normalize(vec3(
		cos(ripple.x * 0.9 + TIME * 1.1) * 0.05 + cos(ripple.x * 2.3 - TIME * 0.7) * 0.02,
		1.0,
		cos(ripple.y * 1.1 - TIME * 0.9) * 0.05 + cos(ripple.y * 2.7 + TIME * 0.8) * 0.02));
	NORMAL = normalize((VIEW_MATRIX * vec4(normal, 0.0)).xyz);

	// Shallow water shows the sand through it; foam is opaque.
	ALPHA = clamp(max(0.74 + depth * 0.7, foam), 0.0, 1.0);
	// Water is water at any depth. Roughening the shallows turned every shelf into a flat sheet of
	// grey — a rough surface lit only by the sky has no highlight and no reflection to give it away
	// as water, so islets looked like they had slabs of stone lying beside them.
	ROUGHNESS = 0.1;
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
