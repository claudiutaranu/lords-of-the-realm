// A county's men on the campaign map: one figure with his lord's banner.
//
// The model has no skeleton and no animation — it is a Meshy scan, ten thousand triangles in one
// welded piece — so everything that moves here is moved by this shader:
//
//   * the banner flies downwind and ripples along its own length, fastened to its pole. Which of
//     its vertices are cloth is worked out when the figure is built (MapDecoration.ArmyMesh) and
//     handed over in the vertex colour: 0 at the pole, 1 at the fly end;
//   * his legs swing while he marches, about the line where they meet the body, one against the
//     other, and the swing fades out under the surcoat so nothing tears at the hem;
//   * every piece of blue cloth he wears — the banner, the surcoat, the shield — takes the colour
//     of whoever holds his county, and the emblem on it stays gold.
shader_type spatial;
// A pennant is cloth: it is seen from both sides, and it is thin enough that the back of it shows.
render_mode cull_disabled;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic;
// glTF's own packing: green is roughness, blue is metal — which is what makes his mail read as mail.
uniform sampler2D metal_rough_texture : filter_linear_mipmap_anisotropic;

// Whose men these are. Per instance, so every county's banner flies its own colour off one material.
instance uniform vec3 lord_color : source_color = vec3(0.70, 0.23, 0.23);
// 1 while he is on the road.
instance uniform float walking = 0.0;
// 1 for the man who carries the standard. A company is drawn as two or three figures and only one
// of them bears it: three identical banners over one army read as three armies. The others keep the
// shaft — what is left in the hand of a man whose cloth is struck is a spear.
instance uniform float banner = 1.0;

// The median brightness of the painted cloth, which the lord's colour is pinned to so the folds keep
// their light and shade. Measured off this model: its blue is a deep one.
const float CLOTH_VALUE = 0.11;

// The prevailing wind, on the ground plane (MapClouds.PrevailingWind): the banner flies with it
// whichever way the man happens to be facing, the same as every flag over a village.
uniform vec2 wind = vec2(1.0, 0.0);
// Where his pole stands in the model, the way the cloth hangs from it as it was made, and how the
// ripple runs along it. All in the model's own units — MapDecoration reads the first two off the mesh.
uniform vec2 flag_pole = vec2(0.0);
uniform float flag_rest = 0.0;
uniform float flag_amplitude = 0.05;
uniform float flag_wavelength = 0.30;
uniform float flag_speed = 2.4;

// The march: where the legs meet the body, how far they swing, how many strides a second, and how
// much the whole man rides up and down on them.
uniform float hip = 0.0;
uniform float sole = 0.0;
uniform float body_side = 0.0;
uniform float stride = 0.24;
uniform float pace = 4.2;
uniform float bob = 0.012;

varying float fold;

void vertex() {
	// Every figure on its own beat, off where it stands, so two armies do not march in step.
	float time = TIME + dot(MODEL_MATRIX[3].xz, vec2(0.61, 0.37));
	float gust = 0.65 + 0.35 * sin(time * 0.37) * sin(time * 0.19 + 0.8);
	float sway = COLOR.r;
	fold = 0.0;

	if (sway > 0.0 && banner < 0.5) {
		// No cloth on this one: it is folded onto its own pole, where it is a sliver nobody sees.
		VERTEX.xz = flag_pole;
		sway = 0.0;
	}

	if (sway > 0.0) {
		// Downwind, whichever way he is turned: the wind is a world direction, so it is brought back
		// into the model to be used here.
		vec2 blowing = normalize((inverse(MODEL_MATRIX) * vec4(wind.x, 0.0, wind.y, 0.0)).xz);
		float turn = atan(blowing.y, blowing.x) - flag_rest;
		mat2 spin = mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn)));
		VERTEX.xz = flag_pole + spin * (VERTEX.xz - flag_pole);
		NORMAL.xz = spin * NORMAL.xz;

		vec2 out_from_pole = VERTEX.xz - flag_pole;
		float reach = length(out_from_pole);
		vec2 aside = vec2(-out_from_pole.y, out_from_pole.x) / max(reach, 1e-4);
		float phase = TAU * reach / flag_wavelength - time * flag_speed;
		float lift = flag_amplitude * sway * gust;
		VERTEX.xz += aside * sin(phase) * lift;
		VERTEX.y += cos(phase) * lift * 0.3;
		fold = cos(phase) * sway * gust;
	}

	if (walking > 0.001) {
		// The legs, swung about the hip: all of it at the boot, none of it at the waist, so the
		// surcoat is not torn off the man wearing it.
		float swing = smoothstep(hip, sole, VERTEX.y);
		float step_angle = sin(time * pace) * stride * swing * walking
			* (VERTEX.x < body_side ? 1.0 : -1.0);
		float turn_c = cos(step_angle);
		float turn_s = sin(step_angle);
		vec2 about = vec2(VERTEX.y - hip, VERTEX.z);
		VERTEX.y = hip + about.x * turn_c - about.y * turn_s;
		VERTEX.z = about.x * turn_s + about.y * turn_c;
		NORMAL.yz = mat2(vec2(turn_c, turn_s), vec2(-turn_s, turn_c)) * NORMAL.yz;

		// And the man rides up and down on them, twice a stride.
		VERTEX.y += abs(sin(time * pace)) * bob * walking;
	}
}

void fragment() {
	vec3 colour = texture(albedo_texture, UV).rgb;

	// His livery: the banner, the surcoat, the shield. Picked out by colour — nothing else on him is
	// blue — and given the lord's hue while it keeps its own light, so the emblem stays gold and the
	// folds stay folds.
	float cloth = smoothstep(0.02, 0.06, colour.b - max(colour.r, colour.g));
	colour = mix(colour, lord_color * (colour.b / CLOTH_VALUE), cloth);
	// The crest of each wave a shade lighter, the trough a shade darker.
	ALBEDO = colour * (1.0 + fold * 0.18);

	NORMAL_MAP = texture(normal_texture, UV).rgb;
	vec2 worked = texture(metal_rough_texture, UV).gb;
	ROUGHNESS = worked.x;
	METALLIC = worked.y;
}
