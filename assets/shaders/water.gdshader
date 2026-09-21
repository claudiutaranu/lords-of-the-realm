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
// A short, quick chop rather than a long ocean swell. At a wavelength of thirty units and a period
// of eight seconds the sea read as six enormous waves crossing the whole map in slow motion, which
// is what an ocean does and not what the water around a county does.
uniform float swell_height = 0.022;
uniform float swell_length = 0.62;
uniform float swell_speed = 1.35;

// What the sea is like out past the map: how deep open water counts as, and over what distance the
// ground gives way to it. The height map itself never gets deeper than a couple of units, so these
// two are what keep the boundary from reading as a line drawn across the water.
uniform float open_sea_depth = 3.2;
uniform float open_sea_reach = 26.0;

// 0 spring, 1 summer, 2 autumn, 3 winter — MapWater.SetSeason, from the same turn change the
// terrain hears. Only winter shows on the sea: a cold, flat, slate-grey version of it.
uniform float season = 1.0;

varying vec3 world_position;

float winter_weight() {
	return clamp(1.0 - abs(season - 3.0), 0.0, 1.0);
}

// How far out past the map's edge a point lies, in world units, and zero anywhere over it.
float past_edge(vec3 position) {
	vec2 uv = position.xz / map_size + 0.5;
	vec2 inside = clamp(uv, vec2(0.0), vec2(1.0));
	return length((uv - inside) * map_size);
}

// Water depth in world units: positive at sea, negative over land, open water out past the map.
float shore_depth(vec3 position) {
	vec2 uv = position.xz / map_size + 0.5;
	vec2 inside = clamp(uv, vec2(0.0), vec2(1.0));
	float ground = sea_level - texture(height_map, inside).r * height_scale;

	// Past the edge there is no ground to read. Clamping the lookup does not give open water, it
	// extrudes the map's last row of seabed outwards for ever: every shelf and islet sitting on that
	// edge is drawn as a stripe running off to the horizon. So the bottom is let go of instead, and
	// the sea settles into open water over a stretch long enough that nothing reads as a boundary.
	return mix(ground, open_sea_depth, smoothstep(0.0, open_sea_reach, past_edge(position)));
}

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Open water swells; near the beach it flattens, or the sea would saw through the sand.
	float depth = shore_depth(world_position);
	// The plane is fourteen times the map across and carries a vertex every twenty-odd units, which
	// is about the swell's own wavelength: out in open water it stops reading as a wave and becomes
	// a lattice of facets. So the swell is let go where the mesh runs out of vertices to carry it.
	float open = clamp(depth / 2.5, 0.0, 1.0) * mix(1.0, 0.45, winter_weight())
		* (1.0 - smoothstep(0.0, 40.0, past_edge(world_position)));
	VERTEX.y += sin(world_position.x * swell_length + TIME * swell_speed) * swell_height * open
		+ sin(world_position.z * swell_length * 1.4 - TIME * swell_speed * 0.8) * swell_height * open;
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

	// Surf belongs to a coast, and there is no coast out past the map: without this, the stretches
	// where the land runs to the border grow a line of breakers on open water a good way offshore.
	float foam = clamp(max(rolling, breaking), 0.0, 1.0) * step(0.0, depth)
		* (1.0 - smoothstep(0.0, 2.0, past_edge(world_position)));

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

	// Fine chop, so the surface catches the sun instead of lying flat as glass. It is drawn per
	// pixel off world position, and out towards the horizon a single pixel covers twenty units of
	// it: the ripple folds over on itself and comes out as a regular lattice of glints laid across
	// the sea. So it is let go with distance, where there are not pixels enough to carry it.
	float close = 1.0 - smoothstep(90.0, 220.0, length(world_position - CAMERA_POSITION_WORLD));
	vec2 ripple = world_position.xz;
	vec3 normal = normalize(vec3(
		(cos(ripple.x * 0.9 + TIME * 1.1) * 0.05 + cos(ripple.x * 2.3 - TIME * 0.7) * 0.02) * close,
		1.0,
		(cos(ripple.y * 1.1 - TIME * 0.9) * 0.05 + cos(ripple.y * 2.7 + TIME * 0.8) * 0.02) * close));
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
