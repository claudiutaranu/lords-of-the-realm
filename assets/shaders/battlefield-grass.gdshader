// The grass on the field a battle is fought on: tufts of blades, dark at the root and lighter at the
// tip, leaning with the wind — more at the tip than the root, and each tuft on its own beat. Short
// turf everywhere the ground is not bare, and taller grass in stands (BattlefieldGrass).
shader_type spatial;
render_mode cull_disabled, diffuse_lambert_wrap;

uniform vec3 root_colour : source_color = vec3(0.22, 0.27, 0.12);
uniform vec3 tip_colour : source_color = vec3(0.50, 0.55, 0.30);
uniform vec2 wind = vec2(1.0, 0.3);
uniform float sway = 0.05; // metres at the tip
// Far off a blade is thinner than a pixel and flickers as the eye moves: so between these distances
// the tufts sink into the turf, and past the second they are the ground's colour and nothing more.
uniform float sink_from = 28.0;
uniform float sink_to = 70.0;

void vertex() {
	// UV.y is how far up the blade this is: 0 at the root, 1 at the tip.
	float up = UV.y;
	vec3 base = MODEL_MATRIX[3].xyz;
	float beat = TIME * 1.7 + dot(base.xz, vec2(0.23, 0.17));
	float gust = 0.6 + 0.4 * sin(TIME * 0.45 + base.x * 0.02);
	vec2 lean = wind * (sin(beat) * 0.5 + 0.7) * gust * sway * up * up;
	// The world lean, in the model's own space: through the inverse of its basis, scale and all.
	// MODEL_NORMAL_MATRIX carries the instance's scale the wrong way round for this — a tuft cut to
	// a hand's height barely stirred, and the trees, seventeen metres tall, swam.
	VERTEX += inverse(mat3(MODEL_MATRIX)) * vec3(lean.x, 0.0, lean.y);
	float far = distance(base, (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz);
	VERTEX.y *= 1.0 - smoothstep(sink_from, sink_to, far);
}

void fragment() {
	// Lit as if the tuft were the ground it grows from, from whichever side it is seen: lit blade by
	// blade, the backs of half of them came out black.
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	ALBEDO = mix(root_colour, tip_colour, UV.y) * COLOR.rgb;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
}
