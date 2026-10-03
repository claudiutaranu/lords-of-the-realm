// A guide painted on the battlefield (BattlefieldGuides): the bowmen's reach and the line to where a
// squad is sent. Lit like the turf it lies on, so a shadow or a wood crossing it darkens it, with a
// ragged edge and worn through in patches, like old paint scuffed into the turf, rather than drawn
// over the scene.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never;

// How far in from its edges the paint is solid, of the strip's half-width as the vertex colour's
// alpha carries it (nought at an edge, one down the middle), and how far the edge wanders.
uniform float edge_from = 0.22;
uniform float edge_to = 0.34;
uniform float edge_ragged = 0.45;
// How coarse the wear is, in metres, and how much of the paint it takes: where the scuffing falls
// under wear_from the turf shows through, and by wear_to the paint is whole.
uniform float wear_size = 0.32;
uniform float wear_from = 0.34;
uniform float wear_to = 0.46;
uniform float opacity = 0.7;

varying vec3 world;

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = fract(sin(dot(i, vec2(127.1, 311.7))) * 43758.5453);
	float b = fract(sin(dot(i + vec2(1.0, 0.0), vec2(127.1, 311.7))) * 43758.5453);
	float c = fract(sin(dot(i + vec2(0.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
	float d = fract(sin(dot(i + vec2(1.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void fragment() {
	float scuff = noise(world.xz / wear_size) * 0.55
		+ noise(world.xz / (wear_size * 0.4)) * 0.3
		+ noise(world.xz / (wear_size * 0.15)) * 0.15;
	float body = smoothstep(edge_from, edge_to, COLOR.a + (scuff - 0.5) * edge_ragged);
	float worn = smoothstep(wear_from, wear_to, scuff);
	ALBEDO = COLOR.rgb * (0.6 + 0.4 * scuff);
	ALPHA = body * worn * opacity * UV.x;
	// Up, whichever way its triangles happen to wind: lit from the back, half the strips were in the
	// dark of their own underside.
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
