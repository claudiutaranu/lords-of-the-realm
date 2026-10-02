// What every soldier on the field wears and how every fallen one goes: shared by soldier.gdshader
// (the standard-bearer, moved by the shader) and battlefield-figure.gdshader (a baked figure, moved
// by its clips), so the two cannot come to disagree about a lord's colour or a dead man's going.

// A fallen man going, 0..1: dissolved rather than faded, so he stays opaque and is never sorted.
bool dissolved(vec2 fragment, float vanish) {
	return vanish > 0.0 && fract(sin(dot(fragment, vec2(12.9898, 78.233))) * 43758.5453) < vanish;
}

// His livery: the banner, the surcoat, the shield, the hood. Picked out by colour — nothing else on
// a man is painted blue — and given the lord's hue while it keeps its own light, pinned to the
// painted blue's median brightness (cloth_value), so an emblem stays gold and folds stay folds —
// but never more than half as bright again as the lord's own colour: the knight's blue has highlights
// three times its median, and a red lifted that far burst its channels into orange flecks.
const float LIVERY_BRIGHTEST = 1.5;

//
// Blue by a share of its own brightness and not by a fixed margin: steel is a bluish grey, and
// measured by the margin every bluer speck of the knight's mail passed for cloth and came out a red
// sparkle on his armour.
vec3 livery(vec3 colour, vec3 lord, float cloth_value) {
	float blue = colour.b - max(colour.r, colour.g);
	float cloth = smoothstep(0.02, 0.06, blue) * smoothstep(0.18, 0.32, blue / max(colour.b, 0.001));
	return mix(colour, lord * min(colour.b / cloth_value, LIVERY_BRIGHTEST), cloth);
}
