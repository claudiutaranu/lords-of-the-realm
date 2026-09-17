// Campaign terrain: one displaced plane. The mesh is flat geometry; every bump comes from
// campaign-map-height.png, which the click raycast reads too (CampaignMap3D.SampleHeight),
// so what you see and what you click are the same surface.
shader_type spatial;
render_mode cull_back;

uniform sampler2D height_map : filter_linear, repeat_disable;
uniform sampler2D albedo_map : filter_linear, repeat_disable;
// Province indices must never be blended: filter_nearest keeps a pixel's realm its own.
uniform sampler2D id_map : filter_nearest, repeat_disable;

uniform float height_scale = 40.0;
uniform vec2 world_texel = vec2(0.1, 0.1); // world units per height texel, for the normals
uniform float selected_index = 0.0;        // province index + 1, matching the ID map's red channel
uniform float hovered_index = 0.0;
uniform vec3 selected_tint : source_color = vec3(1.0, 0.86, 0.45);
// Muted on purpose: a frontier is a line drawn on the ground, not a neon strip. Saturated colour
// belongs to the selection highlight, which has to beat these for attention.
uniform vec3 realm_one_color : source_color = vec3(0.46, 0.20, 0.17);  // The Royal Crown
uniform vec3 realm_two_color : source_color = vec3(0.22, 0.30, 0.45);  // The Northern Watch
uniform vec3 neutral_claim_color : source_color = vec3(0.30, 0.28, 0.24); // unclaimed ground
uniform vec3 border_color : source_color = vec3(0.16, 0.14, 0.11);

// Ground detail. The generated albedo carries the large-scale colour of the realm; these carry the
// surface itself, tiled far tighter than the map, which is what stops the terrain reading as clay.
uniform sampler2D grass_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_normal : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_normal : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D snow_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D snow_normal : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_normal : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float detail_tiling = 90.0;
uniform float map_size_x = 153.6; // world units across, so side projections tile at the same rate
uniform float detail_normal_strength = 1.1;
uniform float sea_level_normalized = 0.18; // where the waterline sits in the height map's 0..1

// Season of the current turn: 0 spring, 1 summer, 2 autumn, 3 winter, set by
// CampaignMap3D.SetSeason each time the turn advances (behind the turn curtain, so the change is
// never seen happening). It moves the snow line, tints the grass and drains the colour out of
// winter; the trees, the sea and the light are seasoned by their own code.
uniform float season = 1.0;

varying vec2 map_uv;
varying vec3 world_vertex;

float sample_height(vec2 uv) {
	return textureLod(height_map, uv, 0.0).r * height_scale;
}

void vertex() {
	map_uv = UV;
	VERTEX.y += sample_height(UV);
	world_vertex = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// Normals come from the heightmap rather than the mesh: the plane's own normals are all up,
	// and re-deriving here costs four taps instead of a second vertex attribute pass.
	vec2 texel = 1.0 / vec2(textureSize(height_map, 0));
	float left = sample_height(map_uv - vec2(texel.x, 0.0));
	float right = sample_height(map_uv + vec2(texel.x, 0.0));
	float up = sample_height(map_uv - vec2(0.0, texel.y));
	float down = sample_height(map_uv + vec2(0.0, texel.y));
	vec3 normal = normalize(vec3(
		(left - right) / (2.0 * world_texel.x),
		1.0,
		(up - down) / (2.0 * world_texel.y)));

	// Two tilings of the same texture, one of them rotated-ish by a different scale, averaged: the
	// repeat is what gives tiled ground away from above, and this costs one extra tap per material.
	vec2 detail_uv = map_uv * detail_tiling;
	vec2 detail_uv2 = map_uv * detail_tiling * 0.37 + vec2(0.37, 0.61);

	float steepness = clamp(1.0 - normal.y, 0.0, 1.0);
	float terrain_height = textureLod(height_map, map_uv, 0.0).r;
	// Exactly one of these is 1.0 while season holds a whole number; they cross-fade if it is ever
	// tweened between two.
	float spring = clamp(1.0 - abs(season), 0.0, 1.0);
	float summer = clamp(1.0 - abs(season - 1.0), 0.0, 1.0);
	float autumn = clamp(1.0 - abs(season - 2.0), 0.0, 1.0);
	float winter = clamp(1.0 - abs(season - 3.0), 0.0, 1.0);
	float snow_start = mix(0.52, 0.24, winter); // in winter the snow comes a long way down the slopes
	float rock_mix = smoothstep(0.34, 0.66, steepness);
	float snow_mix = smoothstep(snow_start, snow_start + 0.14, terrain_height) * (1.0 - rock_mix * 0.35);
	// Beach is a band just above the waterline, wherever that is, not a fixed height.
	float sand_mix = smoothstep(sea_level_normalized + 0.05, sea_level_normalized + 0.002, terrain_height);
	// Winter takes the lowland too, not only the peaks: everywhere but the cliffs, which are too
	// steep to hold it, and the beach, which the surf keeps clear.
	float lowland_snow = winter * 0.8 * (1.0 - rock_mix * 0.55) * (1.0 - sand_mix)
		* smoothstep(sea_level_normalized + 0.01, sea_level_normalized + 0.07, terrain_height);
	snow_mix = clamp(max(snow_mix, lowland_snow), 0.0, 1.0);

	// New growth, deep summer, turned leaf, dead ground: the season lives on the grass, so it stops
	// at the rock and snow mixed in below instead of washing the whole map one colour.
	vec3 season_grass = spring * vec3(0.98, 1.14, 0.78)
		+ summer * vec3(1.0, 1.0, 1.0)
		+ autumn * vec3(1.22, 0.94, 0.58)
		+ winter * vec3(0.88, 0.84, 0.80);

	vec3 detail = mix(texture(grass_texture, detail_uv).rgb, texture(grass_texture, detail_uv2).rgb, 0.5) * season_grass;
	vec3 detail_n = mix(texture(grass_normal, detail_uv).rgb, texture(grass_normal, detail_uv2).rgb, 0.5);
	// Cliffs are sampled from the side, not from above: a flat-projected texture on a vertical face
	// smears into vertical streaks, which is exactly what sea stacks and coastal cliffs showed.
	float world_tiling = detail_tiling / map_size_x;
	vec2 side_uv = abs(normal.x) > abs(normal.z)
		? world_vertex.zy * world_tiling
		: world_vertex.xy * world_tiling;
	float from_side = smoothstep(0.25, 0.6, steepness);
	vec2 rock_uv = detail_uv * 0.45;
	vec2 rock_side_uv = side_uv * 0.45;
	// Two scales averaged, so a cliff face does not show the same crack every few metres.
	vec3 rock_color = mix(
		mix(texture(rock_texture, rock_uv).rgb, texture(rock_texture, rock_uv * 0.31 + vec2(0.17, 0.43)).rgb, 0.45),
		texture(rock_texture, rock_side_uv).rgb, from_side);
	vec3 rock_bump = mix(texture(rock_normal, rock_uv).rgb, texture(rock_normal, rock_side_uv).rgb, from_side);
	detail = mix(detail, rock_color, rock_mix);
	detail_n = mix(detail_n, rock_bump, rock_mix);
	detail = mix(detail, texture(snow_texture, detail_uv).rgb, snow_mix);
	detail_n = mix(detail_n, texture(snow_normal, detail_uv).rgb, snow_mix);
	detail = mix(detail, texture(sand_texture, detail_uv).rgb, sand_mix);
	detail_n = mix(detail_n, texture(sand_normal, detail_uv).rgb, sand_mix);

	// The plane's tangents belong to the flat mesh, not the displaced surface, so the detail normal
	// is folded into the world normal directly instead of going through NORMAL_MAP.
	vec3 bump = detail_n * 2.0 - 1.0;
	normal = normalize(normal + vec3(bump.x, 0.0, bump.y) * detail_normal_strength);
	NORMAL = normalize((VIEW_MATRIX * vec4(normal, 0.0)).xyz);

	// Regional colour as a tint over the detail, so biome and ground texture agree instead of one
	// covering the other.
	vec3 macro = texture(albedo_map, map_uv).rgb;
	vec3 tint = macro / max(dot(macro, vec3(0.3333)), 0.001);
	vec3 albedo = detail * mix(vec3(1.0), tint, 0.45);
	// A touch of desaturation: saturated albedo is the single loudest tell of a painted surface.
	albedo = mix(albedo, vec3(dot(albedo, vec3(0.299, 0.587, 0.114))), 0.12);
	// The macro albedo is painted for a green map, so winter has to be taken out of it here as well
	// — otherwise summer's biome colour bleeds back through the snow.
	albedo = mix(albedo, vec3(dot(albedo, vec3(0.3333))), winter * 0.3);

	// Borders are drawn here rather than baked into the albedo, so they stay a crisp line at any
	// zoom instead of turning into a blurred smear when the camera comes down.
	vec4 here = texture(id_map, map_uv);
	vec2 line_step = 1.6 / vec2(textureSize(id_map, 0));
	vec4 n_right = texture(id_map, map_uv + vec2(line_step.x, 0.0));
	vec4 n_left = texture(id_map, map_uv - vec2(line_step.x, 0.0));
	vec4 n_down = texture(id_map, map_uv + vec2(0.0, line_step.y));
	vec4 n_up = texture(id_map, map_uv - vec2(0.0, line_step.y));

	// A neighbour only counts if it is land as well: otherwise every coastline reads as a border,
	// which painted the whole shore with frontier colour.
	float on_land = step(0.5, here.r * 255.0);
	float province_edge = on_land * min(1.0,
		step(0.001, abs(n_right.r - here.r)) * step(0.5, n_right.r * 255.0)
		+ step(0.001, abs(n_left.r - here.r)) * step(0.5, n_left.r * 255.0)
		+ step(0.001, abs(n_down.r - here.r)) * step(0.5, n_down.r * 255.0)
		+ step(0.001, abs(n_up.r - here.r)) * step(0.5, n_up.r * 255.0));
	// A realm frontier only counts where both sides are land: a coastline is not a border.
	float realm_edge = province_edge * min(1.0,
		step(0.001, abs(n_right.g - here.g)) * step(0.5, n_right.g * 255.0)
		+ step(0.001, abs(n_left.g - here.g)) * step(0.5, n_left.g * 255.0)
		+ step(0.001, abs(n_down.g - here.g)) * step(0.5, n_down.g * 255.0)
		+ step(0.001, abs(n_up.g - here.g)) * step(0.5, n_up.g * 255.0));

	float province = round(here.r * 255.0);
	float is_selected = step(0.5, selected_index) * step(abs(province - selected_index), 0.5);
	float is_hovered = step(0.5, hovered_index) * step(abs(province - hovered_index), 0.5);
	albedo = mix(albedo, albedo * selected_tint * 1.9, is_selected * 0.5);
	albedo = mix(albedo, albedo * selected_tint * 1.4, is_hovered * (1.0 - is_selected) * 0.35);

	// Each side of a contested border is painted in its own realm's colour, so the frontier reads
	// as two claims meeting rather than one neutral line.
	float owner = here.g * 255.0;
	vec3 claim = owner < 1.5 ? realm_one_color : (owner < 2.5 ? realm_two_color : neutral_claim_color);
	// A wider, fainter band inside the line, sampled further out: it is what keeps the frontier
	// legible over snow and forest when the camera pulls back.
	vec2 glow_step = 6.0 / vec2(textureSize(id_map, 0));
	vec4 g_right = texture(id_map, map_uv + vec2(glow_step.x, 0.0));
	vec4 g_left = texture(id_map, map_uv - vec2(glow_step.x, 0.0));
	vec4 g_down = texture(id_map, map_uv + vec2(0.0, glow_step.y));
	vec4 g_up = texture(id_map, map_uv - vec2(0.0, glow_step.y));
	float realm_glow = min(1.0,
		step(0.001, abs(g_right.g - here.g)) * step(0.5, g_right.g * 255.0)
		+ step(0.001, abs(g_left.g - here.g)) * step(0.5, g_left.g * 255.0)
		+ step(0.001, abs(g_down.g - here.g)) * step(0.5, g_down.g * 255.0)
		+ step(0.001, abs(g_up.g - here.g)) * step(0.5, g_up.g * 255.0)) * step(0.5, here.g * 255.0);

	albedo = mix(albedo, border_color, province_edge * 0.4);
	albedo = mix(albedo, claim, realm_glow * 0.12);
	// The frontier itself: a dark line carrying a hint of whose it is, never a painted stripe.
	albedo = mix(albedo, mix(border_color, claim, 0.6), realm_edge * 0.7);

	ALBEDO = albedo;
	ROUGHNESS = 0.92;
	SPECULAR = 0.12;
}
