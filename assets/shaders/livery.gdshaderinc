// What every soldier on the field wears and how every fallen one goes: shared by soldier.gdshader
// (the standard-bearer, moved by the shader) and battlefield-figure.gdshader (a baked figure, moved
// by its clips), so the two cannot come to disagree about a lord's colour or a dead man's going.

// A fallen man going, 0..1: dissolved rather than faded, so he stays opaque and is never sorted.
bool dissolved(vec2 fragment, float vanish) {
	return vanish > 0.0 && fract(sin(dot(fragment, vec2(12.9898, 78.233))) * 43758.5453) < vanish;
}

// His livery: the banner, the surcoat, the shield, the hood. Picked out by colour — nothing else on
// a man is painted blue — and given the lord's hue while it keeps its own light, pinned to the
// painted blue's median brightness (cloth_value), so an emblem stays gold and folds stay folds.
vec3 livery(vec3 colour, vec3 lord, float cloth_value) {
	float cloth = smoothstep(0.02, 0.06, colour.b - max(colour.r, colour.g));
	return mix(colour, lord * (colour.b / cloth_value), cloth);
}
