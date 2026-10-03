// A guide painted on the battlefield (BattlefieldGuides): the bowmen's reach and the line to where a
// squad is sent. Lit like the turf it lies on, so a shadow or a wood crossing it darkens it, with a
// firm edge and a grain to it, as if laid down with a broad brush rather than drawn over the scene.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never;

// How far in from its edges the paint is solid, of the strip's half-width as the vertex colour's
// alpha carries it (nought at an edge, one down the middle), and how coarse and how strong its grain.
uniform float edge_from = 0.18;
uniform float edge_to = 0.42;
uniform float grain_size = 0.55;
uniform float grain = 0.35;

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
	float body = smoothstep(edge_from, edge_to, COLOR.a);
	float brush = noise(world.xz / grain_size) * 0.6 + noise(world.xz / (grain_size * 0.27)) * 0.4;
	ALBEDO = COLOR.rgb * (0.85 + 0.3 * brush);
	ALPHA = body * (1.0 - grain + grain * brush) * 0.9;
	// Up, whichever way its triangles happen to wind: lit from the back, half the strips were in the
	// dark of their own underside.
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
