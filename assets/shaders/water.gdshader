// Sea around the campaign map, with surf along the coast.
//
// There is no depth pass involved: the shader is handed the same height map the terrain is
// displaced by, so it knows the water's depth under any point (shore_depth, in world units,
// negative on land). Colour and foam are both functions of that depth.
//
// The surface itself is two normal maps drifting across each other at different sizes and in
// different directions. That is the whole of what makes a sheet read as water rather than as paint:
// the old surface was a handful of sine ripples, faded out with distance because they aliased into
// a lattice, which left open water as flat bands of colour — the "low poly" look. Real textures,
// mipmapped, do not alias, so they can run all the way to the horizon.
//
// Opaque on purpose. A transparent surface gets no screen-space reflections in this renderer, and
// the island reflected in the sea is worth more than seeing the seabed through it — which the depth
// colour fakes well enough anyway, since shallow water here is only ever turquoise over sand.
shader_type spatial;
render_mode cull_disabled;

uniform sampler2D height_map : filter_linear, repeat_disable;
uniform float height_scale = 20.0;
uniform float sea_level = 3.6;
uniform vec2 map_size = vec2(153.6, 102.4);

// Two drifting wave layers and a noise field to break the surf up. Built by MapWater out of
// Godot's own NoiseTexture2D, seamless and mipmapped, so there is no asset to ship for them.
uniform sampler2D wave_normal_a : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wave_normal_b : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D foam_noise : filter_linear_mipmap, repeat_enable;
// How near the land each point of sea is: 1 at the water's edge, 0 a few dozen pixels out. The surf
// is laid along this — see fragment() for why not along the depth.
uniform sampler2D coast_map : filter_linear, repeat_disable;

// A sea has three tones: turquoise where the light still reaches the bottom, teal over the shelf,
// and open blue past it.
uniform vec3 deep_color : source_color = vec3(0.010, 0.062, 0.190);
uniform vec3 shelf_color : source_color = vec3(0.034, 0.215, 0.330);
uniform vec3 shallow_color : source_color = vec3(0.120, 0.440, 0.480);
uniform vec3 foam_color : source_color = vec3(0.93, 0.96, 0.97);

// Repeats per world unit, and how fast each layer drifts. The two differ in both, so they never
// line up into a pattern the eye can lock on to.
uniform float wave_scale_a = 0.050;
uniform float wave_scale_b = 0.135;
uniform vec2 wave_drift_a = vec2(0.010, 0.006);
uniform vec2 wave_drift_b = vec2(-0.008, 0.013);
uniform float wave_strength = 0.70;

// Where on the coast field the surf starts, and how quickly the washing bands come in behind it.
uniform float surf_line = 0.46;
uniform float wash_frequency = 14.0;
uniform float wash_speed = 1.4;

// A little swell on the mesh itself, near the island only — see vertex().
uniform float swell_height = 0.018;
uniform float swell_length = 0.62;
uniform float swell_speed = 1.35;

// What the sea is like out past the map: how deep open water counts as, and over what distance the
// ground gives way to it, so the map's edge never reads as a line drawn across the water.
uniform float open_sea_depth = 3.2;
uniform float open_sea_reach = 26.0;

// 0 spring, 1 summer, 2 autumn, 3 winter — MapWater.SetSeason. Only winter shows on the sea.
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
	// Past the edge there is no ground to read, and clamping the lookup would extrude the map's last
	// row of seabed outward for ever. So the bottom is let go of, and the sea settles into open water.
	return mix(ground, open_sea_depth, smoothstep(0.0, open_sea_reach, past_edge(position)));
}

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// The plane carries a vertex every twenty-odd units, about the swell's own wavelength, so out in
	// open water a swell turns into a lattice of facets. It is kept to the water near the island,
	// and flattened right at the beach so the sea does not saw through the sand.
	float depth = shore_depth(world_position);
	float open = clamp(depth / 2.5, 0.0, 1.0) * mix(1.0, 0.45, winter_weight())
		* (1.0 - smoothstep(0.0, 40.0, past_edge(world_position)));
	VERTEX.y += (sin(world_position.x * swell_length + TIME * swell_speed)
		+ sin(world_position.z * swell_length * 1.4 - TIME * swell_speed * 0.8)) * swell_height * open;
}

void fragment() {
	float depth = shore_depth(world_position);
	vec2 at = world_position.xz;
	float winter = winter_weight();

	// Two wave layers, blended in tangent space rather than averaged as colours: averaging two normal
	// maps flattens both, which is the opposite of the point. This keeps the slopes of each.
	vec3 wave_a = texture(wave_normal_a, at * wave_scale_a + TIME * wave_drift_a).rgb * 2.0 - 1.0;
	vec3 wave_b = texture(wave_normal_b, at * wave_scale_b + TIME * wave_drift_b).rgb * 2.0 - 1.0;
	vec3 waves = normalize(vec3(wave_a.xy + wave_b.xy, wave_a.z * wave_b.z));
	NORMAL_MAP = waves * 0.5 + 0.5;
	// Calmer in the shallows and flatter in winter, when the sea goes still and grey.
	NORMAL_MAP_DEPTH = wave_strength * mix(0.78, 1.0, smoothstep(0.0, 1.5, depth)) * mix(1.0, 0.5, winter);

	// Colour by depth, on smooth curves: turquoise hugging the shore, teal across the shelf, deep blue
	// past it. The noise lays a faint unevenness over it, the way a real shelf is never one depth.
	float grain = texture(foam_noise, at * 0.018).r - 0.5;
	float to_shelf = smoothstep(0.0, 1.35, depth + grain * 0.35);
	float to_deep = smoothstep(0.9, 4.2, depth + grain * 0.6);
	vec3 water = mix(shallow_color, shelf_color, to_shelf);
	water = mix(water, deep_color, to_deep);
	water = mix(water, mix(vec3(0.46, 0.53, 0.57), vec3(0.06, 0.10, 0.15), to_deep), winter);

	// The surf, laid along the distance from the land rather than along the depth. The shelf round
	// this island is wide and shallow and has a crest out on it, so surf keyed to "shallow" came out
	// as a thick white band standing well offshore, with open water between it and the cliffs. It
	// belongs where the sea meets the rock: a thin broken line at the edge, and fainter bands washing
	// in behind it, all of it torn up by noise so it is white water and not a painted rim.
	vec2 uv = at / map_size + 0.5;
	float near = texture(coast_map, clamp(uv, vec2(0.0), vec2(1.0))).r;
	float noise = texture(foam_noise, at * 0.11 + vec2(TIME * 0.015, -TIME * 0.01)).r;
	float fine = texture(foam_noise, at * 0.41 - vec2(TIME * 0.02, TIME * 0.012)).r;
	float broken = noise * 0.55 + fine * 0.45;

	float edge = smoothstep(surf_line, 1.0, near + (broken - 0.5) * 0.18);
	float wash = smoothstep(0.62, 0.95, sin(near * wash_frequency - TIME * wash_speed) * 0.5 + 0.5)
		* smoothstep(0.30, surf_line, near) * (1.0 - edge);
	// The line at the edge is broken up but never let go of: a noise that can blank the surf out
	// entirely left long stretches of cliff standing in still water, which reads as the land being
	// pasted onto the sea. It thins and thickens instead.
	float foam = edge * mix(0.45, 1.0, smoothstep(0.25, 0.6, broken))
		+ wash * smoothstep(0.50, 0.78, broken) * 0.6;
	// Surf belongs to a coast, and there is none out past the map.
	foam = clamp(foam, 0.0, 1.0) * (1.0 - smoothstep(0.0, 2.0, past_edge(world_position)));

	ALBEDO = mix(water, mix(foam_color, vec3(0.80, 0.85, 0.88), winter), foam);
	// Water is glass; foam is not.
	ROUGHNESS = mix(0.035, 0.55, foam);
	METALLIC = 0.0;
	SPECULAR = 0.5;
}
