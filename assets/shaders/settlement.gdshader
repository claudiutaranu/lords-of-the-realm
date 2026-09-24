// A village on the campaign map: the baked Meshy hamlet, its banners in its lord's colour, and the
// year laid over it the way it is laid over the land round it.
shader_type spatial;
render_mode cull_back;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;
// The normals of the original two-million-triangle model, baked into the forty thousand this one has
// (tools/decimate_meshy.py). In the model's own space, not the surface's: the unwrap that carries
// them is cut into charts wherever the packer found room, and a tangent-space map would have to
// agree with every one of those seams.
uniform sampler2D relief_texture : filter_linear_mipmap_anisotropic;
uniform float relief_strength = 0.8;

// 0 spring, 1 summer, 2 autumn, 3 winter — MapDecoration.SetSeason.
uniform float season = 1.0;
uniform vec3 snow_color : source_color = vec3(0.88, 0.91, 0.95);

// Whose it is. Per instance, so every seat on the map flies its own colour off the one material.
instance uniform vec3 lord_color : source_color = vec3(0.70, 0.23, 0.23);
// The painted cloth's median brightness (linear blue), which the lord's colour is pinned to, so the
// folds keep their light and shade.
const float CLOTH_VALUE = 0.53;

// The flag in the wind. tools/decimate_meshy.py bakes into the texture's alpha how far along the
// flag each vertex lies — 0 at the pole, 1 at the fly end, 0 off the flag — so the cloth waves
// harder the further it is from where it is fastened, and nothing else moves. The pole is the
// model's highest point (MapDecoration finds it); the waves run out from it along the cloth and
// push it to either side of the line it flies along. All in the model's own units.
uniform vec2 flag_pole = vec2(0.0);
// How far the flag is turned round its pole to fly with the wind, since every village is set a few
// degrees off it (MapDecoration). Per instance, like the lord's colour.
instance uniform float flag_turn = 0.0;
// Whether the flag is flying at all. A castle with nobody on its walls flies nothing, so an empty
// castle reads as one from the next valley: the cloth is simply not drawn, and its pole stands bare.
// Not by folding the cloth onto the pole — a few vertices off the flag read the flag's mark from the
// texture across a chart's border, and folding them in drew spikes across the walls. Per instance;
// a village always flies its lord's.
instance uniform float flag_flown = 1.0;
uniform float flag_amplitude = 0.05;
uniform float flag_wavelength = 0.32;
uniform float flag_speed = 2.6;
uniform float fold_shading = 0.18;

varying vec3 world_normal;
varying float fold;
varying float on_flag;

void vertex() {
	float sway = textureLod(albedo_texture, UV, 0.0).a;
	fold = 0.0;
	on_flag = sway;
	if (sway > 0.0) {
		mat2 turn = mat2(vec2(cos(flag_turn), sin(flag_turn)), vec2(-sin(flag_turn), cos(flag_turn)));
		VERTEX.xz = flag_pole + turn * (VERTEX.xz - flag_pole);
		NORMAL.xz = turn * NORMAL.xz;
		vec2 out_from_pole = VERTEX.xz - flag_pole;
		float reach = length(out_from_pole);
		vec2 aside = vec2(-out_from_pole.y, out_from_pole.x) / max(reach, 1e-4);
		// Every village on its own beat, off where it stands, so neighbours do not wave in step.
		float time = TIME + dot(MODEL_MATRIX[3].xz, vec2(0.61, 0.37));
		float gust = 0.65 + 0.35 * sin(time * 0.37) * sin(time * 0.19 + 0.8);
		float phase = TAU * reach / flag_wavelength - time * flag_speed;
		float lift = flag_amplitude * sway * gust;
		VERTEX.xz += aside * sin(phase) * lift;
		VERTEX.y += cos(phase) * lift * 0.3;
		fold = cos(phase) * sway * gust;
	}

	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

void fragment() {
	vec3 colour = texture(albedo_texture, UV).rgb;

	// The thatch, the roof tiles, the courses of stone: the relief the collapse could not keep.
	vec3 relief = texture(relief_texture, UV).rgb * 2.0 - 1.0;
	if (dot(relief, relief) > 0.01) {
		vec3 modelled = normalize((VIEW_MATRIX * MODEL_MATRIX * vec4(relief, 0.0)).xyz);
		NORMAL = normalize(mix(NORMAL, modelled, relief_strength));
	}

	// The cloth. Meshy painted every flag and banner the one pure blue, and nothing else in the
	// village comes near it — measured on the bake, the count of blue texels barely moves between a
	// margin of 0.03 and 0.16 — so the colour alone picks it out.
	float cloth = smoothstep(0.03, 0.10, colour.b - max(colour.r, colour.g));
	// The flying flag, and only it: blue cloth the flag's mark reaches. The banners hung on the
	// walls carry no mark and stay. Any cloth at all, not most of it: the forts' flags are painted a
	// darker blue than the village's and sit low on the ramp, and a half-measure left them flying.
	if (flag_flown < 0.5 && on_flag > 0.0 && cloth > 0.0) {
		discard;
	}
	colour = mix(colour, lord_color * (colour.b / CLOTH_VALUE), cloth);

	float autumn = clamp(1.0 - abs(season - 2.0), 0.0, 1.0);
	float winter = clamp(1.0 - abs(season - 3.0), 0.0, 1.0);

	// The leaves turn as the woods round it do (tree-foliage.gdshader): green picked out by colour,
	// its light kept and its hue given to the season.
	float leaf = smoothstep(0.004, 0.035, colour.g - max(colour.r, colour.b)) * (1.0 - cloth);
	float light = dot(colour, vec3(0.2126, 0.7152, 0.0722));
	vec3 turned = light * (autumn * vec3(2.10, 1.05, 0.22) + winter * vec3(1.15, 0.95, 0.72) * 0.72);
	colour = mix(colour, turned, leaf * (autumn + winter) * 0.8);

	// Snow lies on whatever faces the sky — the roofs, the yard, the tops of the hedges — and not
	// on walls, the same as on the ground outside the fence.
	float sky = smoothstep(0.45, 0.85, normalize(world_normal).y);
	colour = mix(colour, snow_color, sky * winter * 0.85 * (1.0 - cloth));

	// The folds: the crest of each wave a shade lighter, the trough a shade darker.
	ALBEDO = colour * (1.0 + fold * fold_shading);
	ROUGHNESS = mix(0.9, 0.8, sky * winter);
	SPECULAR = 0.3;
}
