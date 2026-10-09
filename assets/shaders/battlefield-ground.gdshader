// The field a battle is fought on, painted the way a real summer meadow looks from a lord's height:
// lush grass and drier grass in broad drifts a field across, worn ground where it is thin, and bare
// earth where the field is trodden — the bare map, which also carries the cart track and which the
// grass (BattlefieldGrass) is sown by. Photographed ground (assets/terrain/battle, ambientCG CC0) with
// its relief, sampled at two sizes and blended by noise so no tile is ever seen twice in a row.
shader_type spatial;

uniform sampler2D meadow : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D meadow_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D worn : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D worn_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D patches : filter_linear, repeat_enable;
uniform sampler2D bare_map : filter_linear;

// How many times the ground repeats across the field (a tile is a few metres), and the colour it is
// graded to: the photographs are a spring green, the field a high summer one.
uniform float tiles = 80.0;
uniform vec3 lush_tint : source_color = vec3(0.50, 0.62, 0.36);
uniform vec3 dry_tint : source_color = vec3(0.70, 0.68, 0.44);
uniform vec3 dirt_tint : source_color = vec3(0.62, 0.52, 0.40);

// How much of the field dries, how much is worn thin, and where the bare map turns to earth.
uniform float dry_from = 0.52;
uniform float worn_from = 0.58;
uniform float bare_from = 0.64;
uniform float bare_to = 0.74;
uniform float relief = 0.9;

// A tile sampled twice, the second a different size and turned, mixed by noise: the eye finds no
// repeat across a field five hundred metres wide.
// Sampled a mip softer than the screen asks for: the photographs' single blades, finer than a pixel
// from the lord's height, sparkled like fleas as the eye moved. The grass in it is the 3D grass's work.
uniform float soften = 0.0;
uniform float grain_far = 0.5;

vec4 ground(sampler2D tex, vec2 uv, float mixer) {
	vec2 turned = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * uv * 0.61 + vec2(0.37, 0.71);
	return mix(texture(tex, uv, soften), texture(tex, turned, soften), smoothstep(0.35, 0.65, mixer));
}

// The photograph's colour with its fine grain only partly kept: its average, from its smallest mip,
// and so much of the grain over it as is wanted — less the further off, where it only sparkles.
vec3 calm(sampler2D tex, vec2 uv, float mixer, float grain) {
	vec3 whole = ground(tex, uv, mixer).rgb;
	vec3 mean = textureLod(tex, vec2(0.5), 10.0).rgb;
	return mean + (whole - mean) * grain;
}

// The fog of war round the field: from edge_from metres out of its middle the ground darkens, and by
// edge_to there is nothing left of it to see (the user's call).
uniform float edge_from = 150.0;
uniform float edge_to = 255.0;
varying float edge;

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	edge = smoothstep(edge_from, edge_to, length(world.xz));
}

void fragment() {
	vec2 uv = UV * tiles;
	float mixer = texture(patches, UV * 23.0).r;
	float drift = texture(patches, UV * 2.6).r;
	float thin = texture(patches, UV * 7.0 + vec2(0.3, 0.1)).r;
	float swathe = texture(patches, UV * 11.0).r;
	float bare = smoothstep(bare_from, bare_to, texture(bare_map, UV).r);

	float near = 1.0 - smoothstep(15.0, 80.0, length(VERTEX));
	float grain = mix(grain_far, 1.0, near);
	vec3 grass = calm(meadow, uv, mixer, grain);
	float dry = smoothstep(dry_from - 0.25, dry_from + 0.4, drift) * 0.55;
	grass *= mix(lush_tint, dry_tint, dry) * (0.8 + 0.22 * swathe);
	float wornness = max(smoothstep(worn_from - 0.1, worn_from + 0.35, thin) * 0.6, bare * 0.6);
	vec3 ground_worn = calm(worn, uv * 0.8, mixer, grain) * mix(lush_tint, dry_tint, dry);
	vec3 earth = calm(dirt, uv * 0.9, mixer, grain) * dirt_tint;

	vec3 colour = mix(grass, ground_worn, wornness * 0.75);
	colour = mix(colour, earth, bare);
	ALBEDO = colour * (1.0 - edge);

	vec3 n = mix(ground(meadow_normal, uv, mixer).rgb, ground(worn_normal, uv * 0.8, mixer).rgb, wornness * 0.75);
	n = mix(n, ground(dirt_normal, uv * 0.9, mixer).rgb, bare);
	NORMAL_MAP = n;
	// The relief fades with distance too: a normal map's speckle is the worst of the shimmer.
	NORMAL_MAP_DEPTH = relief * (1.0 - smoothstep(20.0, 60.0, length(VERTEX)));
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
