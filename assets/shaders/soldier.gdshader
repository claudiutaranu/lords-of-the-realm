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

#include "res://assets/shaders/livery.gdshaderinc"

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
uniform float stride = 0.17;
uniform float pace = 4.2;
uniform float bob = 0.012;
// How far from the pole's axis its shaft reaches, in the model's own units: kept out of the stride.
uniform float pole_radius = 0.0;
// How high a foot comes off the ground as it swings through, and how far he bends into a blow.
uniform float foot_lift = 0.02;
// How far the knee of the leg coming through bends, in radians, and how far he leans into his walk.
uniform float knee_bend = 0.7;
uniform float walk_lean = 0.08;
uniform float lean_angle = 0.5;

varying float fold;
// How far gone a fallen man is, 0..1: the field lets its dead go after a while (BattlefieldSquads),
// dissolving them rather than fading them, so they stay opaque and never have to be sorted.
varying float vanish;

void vertex() {
	// Every figure on its own beat, off where it stands, so two armies do not march in step.
	float time = TIME + dot(MODEL_MATRIX[3].xz, vec2(0.61, 0.37));
	float gust = 0.65 + 0.35 * sin(time * 0.37) * sin(time * 0.19 + 0.8);
	float sway = COLOR.r;
	// On the battlefield every man's gait is worked out for him (BattlefieldSquads) and comes in
	// with his place in the ranks: how much he is walking, where in his stride he is, how long his
	// stride is, and how far he is leaning into a blow. A figure drawn on its own — the map's —
	// has none of that, and walks off the clock at the one pace.
	bool driven = INSTANCE_CUSTOM.z > 0.0;
	float marching = driven ? INSTANCE_CUSTOM.x : walking;
	float gait = driven ? INSTANCE_CUSTOM.y : time * pace;
	float reach_of_step = driven ? INSTANCE_CUSTOM.z : stride;
	float lean = INSTANCE_CUSTOM.w;
	vanish = max(0.0, -INSTANCE_CUSTOM.w);
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

	if (lean > 0.001) {
		// Into the blow: everything above the hip bends forward about it, the head and the arms
		// most, the belt not at all.
		float bend = lean * lean_angle * smoothstep(hip, hip + (hip - sole), VERTEX.y);
		float lean_c = cos(bend);
		float lean_s = sin(bend);
		vec2 above = vec2(VERTEX.y - hip, VERTEX.z);
		VERTEX.y = hip + above.x * lean_c - above.y * lean_s;
		VERTEX.z = above.x * lean_s + above.y * lean_c;
		NORMAL.yz = mat2(vec2(lean_c, lean_s), vec2(-lean_s, lean_c)) * NORMAL.yz;
	}

	if (marching > 0.001) {
		// The legs, swung about the hip: all of it at the boot, none of it at the waist, so the
		// surcoat is not torn off the man wearing it. The one swinging forward is lifted off the
		// ground as it goes — a leg that only swings is a pendulum, not a step.
		// Not the pole he carries, though: its foot hangs below his hip beside his legs, and swung
		// with them it wagged like a third leg.
		float off_pole = smoothstep(pole_radius, pole_radius * 2.0, length(VERTEX.xz - flag_pole));
		float swing = smoothstep(hip, sole, VERTEX.y) * off_pole;
		float leg = VERTEX.x < body_side ? 1.0 : -1.0;
		float step_angle = sin(gait) * reach_of_step * swing * marching * leg;

		// The knee: the leg coming through bends at it, the foot trailing behind and clear of the
		// ground, and straightens as it reaches forward to land. A leg swung stiff from the hip is a
		// compass walking, not a man.
		float knee = sole + (hip - sole) * 0.45;
		float shin = smoothstep(knee, sole, VERTEX.y) * off_pole;
		float bend = max(0.0, -cos(gait) * leg) * knee_bend * shin * marching;
		vec2 below = vec2(VERTEX.y - knee, VERTEX.z);
		float bend_c = cos(-bend);
		float bend_s = sin(-bend);
		VERTEX.y = knee + below.x * bend_c - below.y * bend_s;
		VERTEX.z = below.x * bend_s + below.y * bend_c;
		VERTEX.y += max(0.0, -cos(gait) * leg) * foot_lift * swing * marching;
		float turn_c = cos(step_angle);
		float turn_s = sin(step_angle);
		vec2 about = vec2(VERTEX.y - hip, VERTEX.z);
		VERTEX.y = hip + about.x * turn_c - about.y * turn_s;
		VERTEX.z = about.x * turn_s + about.y * turn_c;
		NORMAL.yz = mat2(vec2(turn_c, turn_s), vec2(-turn_s, turn_c)) * NORMAL.yz;

		// And the man rides up and down on them, twice a stride, leaning a little into his walk.
		VERTEX.y += abs(sin(gait)) * bob * marching;
		float torso = smoothstep(hip, hip + (hip - sole), VERTEX.y) * off_pole * walk_lean * marching;
		VERTEX.z += (VERTEX.y - hip) * torso;
	}
}

void fragment() {
	if (dissolved(FRAGCOORD.xy, vanish)) {
		discard;
	}

	vec3 colour = livery(texture(albedo_texture, UV).rgb, lord_color, CLOTH_VALUE);
	// The crest of each wave a shade lighter, the trough a shade darker.
	ALBEDO = colour * (1.0 + fold * 0.18);

	NORMAL_MAP = texture(normal_texture, UV).rgb;
	vec2 worked = texture(metal_rough_texture, UV).gb;
	ROUGHNESS = worked.x;
	METALLIC = worked.y;
}
