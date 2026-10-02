// A soldier baked from his clips (tools/bake_figure.py): where each of his vertices is at every frame
// of every clip is in `positions`, one row a frame, and his normals in `turned`. Each man on the
// field says which frame he is at in his place's custom data — the row, with the fraction toward
// the next — and is drawn blended between the two; the rest is his livery, and his going once he
// has lain dead long enough (INSTANCE_CUSTOM.w below zero).
shader_type spatial;
render_mode cull_disabled;

#include "res://assets/shaders/livery.gdshaderinc"

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;
// Which of him is metal and how polished (roughness in green, metal in blue, as glTF keeps them),
// where he was baked with it: a knight's plate catches the sky, his horse's blanket does not.
uniform sampler2D material_texture : filter_linear_mipmap_anisotropic;
uniform bool has_material = false;
uniform sampler2D positions : filter_nearest;
uniform sampler2D turned : filter_nearest;

instance uniform vec3 lord_color : source_color = vec3(0.70, 0.23, 0.23);

// The painted blue's median brightness on this figure, which the lord's colour is pinned to, and how
// much brighter than that the livery is worn: a baked figure's cloth is painted dark, and pinned
// to it exactly, a lord's gold came out the olive of a wet sack.
uniform float cloth_value = 0.11;
uniform float livery_lift = 1.6;

// How many vertices the figure has, and how wide its baked textures are laid out: frame after
// frame, vertex after vertex, wrapped into rows (tools/bake_figure.py).
uniform int vertex_count = 1;
uniform int row_width = 4096;

varying float vanish;

ivec2 texel(int index) {
	return ivec2(index % row_width, index / row_width);
}

// Where a vertex is at a frame (a row, with the fraction toward the next), and its normal — the
// normal read off the nearer frame only: it only lights him, and blending it too doubled the reads.
vec3 pose(float frame, int id, out vec3 normal) {
	int row = int(floor(frame));
	float toward = fract(frame);
	ivec2 here = texel((row * vertex_count) + id);
	ivec2 next = texel(((row + 1) * vertex_count) + id);
	normal = texelFetch(turned, toward < 0.5 ? here : next, 0).xyz * 2.0 - 1.0;
	return mix(texelFetch(positions, here, 0).xyz, texelFetch(positions, next, 0).xyz, toward);
}

void vertex() {
	// The frame he is at — and, only while he is turning from one clip into the next, the frame of
	// the clip he is fading out of (INSTANCE_CUSTOM.z, how much of the old is left): read every frame
	// for every man, it cost as much again as the man himself.
	vec3 normal_now;
	VERTEX = pose(INSTANCE_CUSTOM.x, VERTEX_ID, normal_now);
	NORMAL = normal_now;
	if (INSTANCE_CUSTOM.z > 0.001) {
		vec3 normal_was;
		vec3 was = pose(INSTANCE_CUSTOM.y, VERTEX_ID, normal_was);
		VERTEX = mix(VERTEX, was, INSTANCE_CUSTOM.z);
		NORMAL = normalize(mix(normal_now, normal_was, INSTANCE_CUSTOM.z));
	}

	vanish = max(0.0, -INSTANCE_CUSTOM.w);
}

void fragment() {
	if (dissolved(FRAGCOORD.xy, vanish)) {
		discard;
	}

	// Nothing metal is cloth, whatever its colour.
	vec3 surface = has_material ? texture(material_texture, UV).rgb : vec3(0.0, 0.8, 0.0);
	// Metal or not, by a threshold: the map is coarse, and read as a fraction the helmet's rim made
	// half-metal of the cheek beside it. What is not metal is matte — skin and cloth under a sun as
	// smooth as the map had them came out pale and glossy, the faces white.
	float metal = smoothstep(0.35, 0.65, surface.b);
	vec3 paint = texture(albedo_texture, UV).rgb;
	ALBEDO = mix(livery(paint, lord_color, cloth_value / livery_lift), paint, metal);
	ROUGHNESS = mix(max(surface.g, 0.65), surface.g, metal);
	METALLIC = metal;
	SPECULAR = mix(0.3, 0.5, metal);
}
