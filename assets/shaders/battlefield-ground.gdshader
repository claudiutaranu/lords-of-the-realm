// The field a battle is fought on: meadow grass, lighter and darker in broad swathes, broken by bare
// earth where the ground is worn. One noise picks out both, at two sizes, so the field does not
// read as a carpet laid in tiles.
shader_type spatial;

uniform sampler2D grass : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D earth : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D patches : filter_linear, repeat_enable;
// Where the earth shows, once across the whole field: the grass (BattlefieldGrass) reads the same
// map, so no tuft grows on bare ground.
uniform sampler2D bare_map : filter_linear;

// How many times the textures repeat across the field, and how the grass is coloured: the
// texture is a dry summer ground, and the field wants the green of the map's meadows.
uniform float tiles = 112.0;
uniform vec3 grass_tint : source_color = vec3(0.52, 0.62, 0.40);
uniform vec3 earth_tint : source_color = vec3(0.58, 0.54, 0.44);

// How much of the field is bare, and how soft its edges are.
uniform float bare_from = 0.64;
uniform float bare_to = 0.74;

void fragment() {
	vec2 uv = UV * tiles;
	float swathe = texture(patches, UV * 11.0).r;
	float bare = smoothstep(bare_from, bare_to, texture(bare_map, UV).r);
	vec3 meadow = texture(grass, uv).rgb * grass_tint * (0.8 + 0.4 * swathe);
	vec3 worn = texture(earth, uv * 0.7).rgb * earth_tint;
	ALBEDO = mix(meadow, worn, bare);
	ROUGHNESS = 0.95;
}
