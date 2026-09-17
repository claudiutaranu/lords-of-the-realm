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

varying vec2 map_uv;

float sample_height(vec2 uv) {
	return textureLod(height_map, uv, 0.0).r * height_scale;
}

void vertex() {
	map_uv = UV;
	VERTEX.y += sample_height(UV);
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
	NORMAL = normalize((VIEW_MATRIX * vec4(normal, 0.0)).xyz);

	vec3 albedo = texture(albedo_map, map_uv).rgb;

	float province = round(texture(id_map, map_uv).r * 255.0);
	float is_selected = step(0.5, selected_index) * step(abs(province - selected_index), 0.5);
	float is_hovered = step(0.5, hovered_index) * step(abs(province - hovered_index), 0.5);
	albedo = mix(albedo, albedo * selected_tint * 1.9, is_selected * 0.5);
	albedo = mix(albedo, albedo * selected_tint * 1.4, is_hovered * (1.0 - is_selected) * 0.35);

	ALBEDO = albedo;
	ROUGHNESS = 0.92;
	SPECULAR = 0.12;
}
