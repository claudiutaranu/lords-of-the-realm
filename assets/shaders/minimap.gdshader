// Sidebar minimap: who holds what, and what is selected.
//
// Province shapes come from the campaign's ID map (R = province index + 1), the same image the
// terrain shader and the click raycast read, so the minimap can never disagree with the map.
// Ownership does not come from that image — it arrives as a strip of one texel per province, which
// the page rebuilds whenever a province changes hands: RGB is the realm's colour, and alpha carries
// the realm's index so the whole realm can be lit when one of its provinces is chosen.
shader_type canvas_item;

uniform sampler2D id_map : filter_nearest, repeat_disable;
uniform sampler2D owner_colors : filter_nearest, repeat_disable;
uniform float selected_index = 0.0;  // province index + 1, 0 for nothing selected
uniform float selected_realm = -1.0; // realm index of the selected province

uniform vec3 water_color : source_color = vec3(0.043, 0.071, 0.125);
uniform vec3 border_color : source_color = vec3(0.055, 0.047, 0.039);
uniform vec3 selected_color : source_color = vec3(1.0, 0.86, 0.45);

float province_at(vec2 uv) {
	return round(texture(id_map, uv).r * 255.0);
}

void fragment() {
	vec4 base = texture(TEXTURE, UV);
	float province = province_at(UV);

	// The terrain's own colour map is underneath, so ownership is washed over the ground rather
	// than replacing it: woods, snow and coastline stay legible at this size.
	vec3 terrain = base.rgb;

	vec3 color;
	float realm = -1.0;
	if (province < 0.5) {
		color = mix(terrain, water_color, 0.7);
	} else {
		vec4 owner = texelFetch(owner_colors, ivec2(int(province) - 1, 0), 0);
		realm = round(owner.a * 255.0);
		color = mix(terrain, owner.rgb, 0.42);
	}

	vec2 step_uv = 1.0 / vec2(textureSize(id_map, 0));
	float edge = min(1.0,
		step(0.001, abs(province_at(UV + vec2(step_uv.x, 0.0)) - province))
		+ step(0.001, abs(province_at(UV - vec2(step_uv.x, 0.0)) - province))
		+ step(0.001, abs(province_at(UV + vec2(0.0, step_uv.y)) - province))
		+ step(0.001, abs(province_at(UV - vec2(0.0, step_uv.y)) - province)));

	// Everything the selected province's lord holds is lifted, so the map answers "whose is this?"
	// as well as "which one is open".
	float same_realm = step(0.0, selected_realm) * step(abs(realm - selected_realm), 0.5);
	color = mix(color, color * 1.35 + vec3(0.04), same_realm * 0.55);

	color = mix(color, border_color, edge * 0.55);

	float is_selected = step(0.5, selected_index) * step(abs(province - selected_index), 0.5);
	color = mix(color, selected_color, is_selected * 0.22);
	color = mix(color, selected_color, is_selected * edge); // gold outline on the chosen province

	COLOR = vec4(color, base.a);
}
