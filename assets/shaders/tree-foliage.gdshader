// A tree on the campaign map: its own relief, and its leaves turning with the year.
//
// The baked trees carry crown and trunk in one texture, so there is no separate leaf material to
// repaint the way the old pack's trees had. Instead the leaves are picked out of the texture by their
// colour — where green stands clear of red and blue it is foliage, where it does not it is bark — and
// only those texels take the season. An evergreen is drawn by this shader too, for the relief, and
// keeps its needles: see the uniform below.
shader_type spatial;
render_mode cull_back;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;

// The normals of the original eight-million-triangle model, baked into the three thousand this one
// has (tools/decimate_meshy.py). Held in the model's own space rather than in the surface's, because
// the wrapped surface these were baked onto has no tangents worth the name — its UV charts are cut
// wherever the packer found room, and a tangent-space map would have to agree with them.
uniform sampler2D relief_texture : filter_linear_mipmap_anisotropic;
// How much of the original's relief is taken over the wrap's own soft shading, 0..1. All of it makes
// a crown of separate leaves at arm's length and a boiling mess at map height.
uniform float relief_strength = 0.7;

// 1 for a pine or a cypress: it keeps its needles, and only the snow touches it.
uniform float evergreen = 0.0;

// 0 spring, 1 summer, 2 autumn, 3 winter — MapDecoration.SetSeason.
uniform float season = 1.0;

void fragment() {
	vec3 colour = texture(albedo_texture, UV).rgb;

	// The baked relief, laid over the shading the wrap itself has.
	vec3 relief = texture(relief_texture, UV).rgb * 2.0 - 1.0;
	if (dot(relief, relief) > 0.01) {
		vec3 modelled = normalize((VIEW_MATRIX * MODEL_MATRIX * vec4(relief, 0.0)).xyz);
		NORMAL = normalize(mix(NORMAL, modelled, relief_strength));
	}

	// How much of this texel is leaf. The bark in these textures is brown and grey; the leaves are
	// green by a clear margin. The ramp is soft so the edge of a leaf mass does not show as a line.
	float leaf = smoothstep(0.004, 0.035, colour.g - max(colour.r, colour.b)) * (1.0 - evergreen);

	float spring = clamp(1.0 - abs(season), 0.0, 1.0);
	float summer = clamp(1.0 - abs(season - 1.0), 0.0, 1.0);
	float autumn = clamp(1.0 - abs(season - 2.0), 0.0, 1.0);
	float winter = clamp(1.0 - abs(season - 3.0), 0.0, 1.0);

	// The leaf keeps its own light and shade and takes the season's colour — the same way the
	// meadow does, and for the same reason: brightness is squashed by the tonemapper, hue is not.
	vec3 tone = spring * vec3(0.78, 1.40, 0.36)
		+ summer * vec3(0.52, 1.10, 0.40)
		+ autumn * vec3(2.10, 1.05, 0.22)
		+ winter * vec3(1.15, 0.95, 0.72);
	float light = dot(colour, vec3(0.2126, 0.7152, 0.0722));
	// Winter leaves are mostly gone: what is left of the crown is thin and brown, and darker.
	float strength = mix(0.8, 0.9, winter);
	vec3 seasoned = light * tone * mix(1.0, 0.72, winter);
	colour = mix(colour, seasoned, leaf * strength);

	ALBEDO = colour;
	ROUGHNESS = 0.9;
	SPECULAR = 0.25;
}
